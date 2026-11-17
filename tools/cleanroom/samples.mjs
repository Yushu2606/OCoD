#!/usr/bin/env node
/**
 * OCoD cleanroom sample generator / verifier.
 *
 * This tool is analysis-side tooling. It does not read the unpacked original.
 * It implements the container and payload rules from docs/specs/.
 *
 * Usage:
 *   node tools/cleanroom/samples.mjs generate
 *   node tools/cleanroom/samples.mjs verify
 *   node tools/cleanroom/samples.mjs inspect <file>
 */
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import zlib from 'node:zlib';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const REPO = path.resolve(__dirname, '..', '..');
const SAMPLES = path.join(REPO, 'samples');

const TRANSPORT = Buffer.from(
  '73ca62079026ebb61663058e0d16e32be688bc36a8677299b75251a27d6ea2ba',
  'hex',
);
const KINDS = {
  clk: {
    magic: Buffer.from('BCVCLK2\n', 'latin1'),
    format: 'bug-clicker',
    version: 2,
    info: 'cvclk-v2',
    max: 419430400,
  },
  part: {
    magic: Buffer.from('BCVPART1', 'latin1'),
    format: 'bug-clicker-part',
    version: 1,
    info: 'cvpart-v1',
    max: 67108864,
  },
};
const APP = '0.1.0';
const STAMP = '2026-11-17T00:00:00.000Z';

// ---------------------------------------------------------------------------
// Small utilities
// ---------------------------------------------------------------------------
const sha256 = (buf) => crypto.createHash('sha256').update(buf).digest('hex');

function u32be(n) {
  const b = Buffer.alloc(4);
  b.writeUInt32BE(n >>> 0, 0);
  return b;
}

function crc32(buf) {
  if (!crc32.table) {
    crc32.table = new Uint32Array(256);
    for (let n = 0; n < 256; n++) {
      let c = n;
      for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
      crc32.table[n] = c >>> 0;
    }
  }
  let c = 0xffffffff;
  for (let i = 0; i < buf.length; i++) c = crc32.table[(c ^ buf[i]) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

function adler32(buf) {
  let a = 1;
  let b = 0;
  for (let i = 0; i < buf.length; i++) {
    a = (a + buf[i]) % 65521;
    b = (b + a) % 65521;
  }
  return ((b << 16) | a) >>> 0;
}

// ---------------------------------------------------------------------------
// Deterministic DEFLATE (fixed Huffman + run-length matches)
// ---------------------------------------------------------------------------
class BitWriter {
  constructor() {
    this.out = [];
    this.buf = 0;
    this.n = 0;
  }
  bit(b) {
    this.buf |= (b & 1) << this.n;
    this.n++;
    if (this.n === 8) {
      this.out.push(this.buf);
      this.buf = 0;
      this.n = 0;
    }
  }
  huff(code, len) {
    for (let i = len - 1; i >= 0; i--) this.bit((code >> i) & 1);
  }
  align() {
    if (this.n) {
      this.out.push(this.buf);
      this.buf = 0;
      this.n = 0;
    }
  }
  bytes() {
    this.align();
    return Buffer.from(this.out);
  }
}

const LEN_TABLE = (() => {
  const bases = [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];
  const extra = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
  return bases.map((base, i) => ({
    code: 257 + i,
    base,
    extra: extra[i],
    max: i === 28 ? 258 : base + (1 << extra[i]) - 1,
  }));
})();

const DIST_TABLE = (() => {
  const bases = [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577];
  const extra = [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];
  return bases.map((base, i) => ({
    code: i,
    base,
    extra: extra[i],
    max: i === 29 ? 32768 : base + (1 << extra[i]) - 1,
  }));
})();

function lengthCode(n) {
  for (const e of LEN_TABLE) if (n <= e.max) return e;
  throw new Error('bad length');
}

function distanceCode(n) {
  for (const e of DIST_TABLE) if (n <= e.max) return e;
  throw new Error('bad distance');
}

function litCode(w, v) {
  if (v <= 143) w.huff(0x30 + v, 8);
  else if (v <= 255) w.huff(0x190 + (v - 144), 9);
  else if (v <= 279) w.huff(v - 256, 7);
  else w.huff(0xc0 + (v - 280), 8);
}

// Greedy LZ77 with fixed Huffman codes. The hash table stores the most recent
// 4-byte sequence; that is enough for the flat-color art used by the samples.
// It is deterministic and independent of the host zlib version.
function deflateFixedLZ(data) {
  const w = new BitWriter();
  w.bit(1);
  w.bit(1);
  w.bit(0); // BFINAL=1, BTYPE=01 (fixed Huffman)
  const map = new Map();
  const MAX_DIST = 32768;
  const key = (i) =>
    (((data[i] << 24) | (data[i + 1] << 16) | (data[i + 2] << 8) | data[i + 3]) >>> 0);
  let i = 0;
  while (i < data.length) {
    let bestLen = 0;
    let bestDist = 0;
    if (i + 4 <= data.length) {
      const prev = map.get(key(i));
      if (prev !== undefined && i - prev <= MAX_DIST) {
        const max = Math.min(258, data.length - i);
        let len = 0;
        while (len < max && data[i + len] === data[prev + len]) len++;
        if (len >= 4) {
          bestLen = len;
          bestDist = i - prev;
        }
      }
    }
    if (bestLen >= 4) {
      const le = lengthCode(bestLen);
      litCode(w, le.code);
      if (le.extra) {
        let x = bestLen - le.base;
        for (let b = 0; b < le.extra; b++) {
          w.bit(x & 1);
          x >>= 1;
        }
      }
      const de = distanceCode(bestDist);
      w.huff(de.code, 5);
      if (de.extra) {
        let x = bestDist - de.base;
        for (let b = 0; b < de.extra; b++) {
          w.bit(x & 1);
          x >>= 1;
        }
      }
      for (let j = 0; j < bestLen; j++) {
        const p = i + j;
        if (p + 4 <= data.length) map.set(key(p), p);
      }
      i += bestLen;
    } else {
      litCode(w, data[i]);
      if (i + 4 <= data.length) map.set(key(i), i);
      i++;
    }
  }
  litCode(w, 256); // end of block
  return w.bytes();
}

function zlibDeflate(data) {
  const d = deflateFixedLZ(data);
  const out = Buffer.alloc(2 + d.length + 4);
  out[0] = 0x78;
  out[1] = 0x01;
  d.copy(out, 2);
  out.writeUInt32BE(adler32(data), 2 + d.length);
  return out;
}

function gzipDeflate(data) {
  const header = Buffer.from([0x1f, 0x8b, 0x08, 0x00, 0, 0, 0, 0, 0x00, 0xff]);
  const d = deflateFixedLZ(data);
  const trailer = Buffer.alloc(8);
  trailer.writeUInt32LE(crc32(data), 0);
  trailer.writeUInt32LE(data.length >>> 0, 4);
  return Buffer.concat([header, d, trailer]);
}

// ---------------------------------------------------------------------------
// Container
// ---------------------------------------------------------------------------
function hkdf(ikm, salt, info) {
  return Buffer.from(crypto.hkdfSync('sha256', ikm, salt, Buffer.from(info), 32));
}

function seal(key, plain, aad, nonce) {
  const c = crypto.createCipheriv('aes-256-gcm', key, nonce);
  if (aad) c.setAAD(aad);
  const ct = Buffer.concat([c.update(plain), c.final()]);
  return Buffer.concat([nonce, c.getAuthTag(), ct]);
}

function packContainer(payload, opts) {
  const kind = KINDS[opts.kind || 'clk'];
  const header = {
    format: kind.format,
    version: kind.version,
    locked: !!opts.locked,
  };
  if (opts.owner) header.owner = opts.owner;
  const headerBytes = Buffer.from(JSON.stringify(header), 'utf8');
  if (headerBytes.length > 4096) throw new Error('header too large');
  const seed = String(opts.seed || 'ocod-sample');
  const salt = crypto.createHash('sha256').update('salt:' + seed).digest().subarray(0, 16);
  const nonce = crypto.createHash('sha256').update('nonce:' + seed).digest().subarray(0, 12);
  const key = hkdf(TRANSPORT, salt, kind.info);
  const plain = Buffer.from(JSON.stringify(payload), 'utf8');
  const gz = gzipDeflate(plain);
  const sealed = seal(key, gz, headerBytes, nonce);
  return Buffer.concat([kind.magic, u32be(headerBytes.length), headerBytes, salt, sealed]);
}

function unpackContainer(buf) {
  let kindName = null;
  for (const [name, kind] of Object.entries(KINDS)) {
    if (buf.length > kind.magic.length + 4 && buf.subarray(0, kind.magic.length).equals(kind.magic)) {
      kindName = name;
      break;
    }
  }
  if (!kindName) throw new Error('not a container');
  const kind = KINDS[kindName];
  let off = kind.magic.length;
  const n = buf.readUInt32BE(off);
  off += 4;
  if (n > 4096) throw new Error('header too large');
  const headerBytes = buf.subarray(off, off + n);
  off += n;
  const salt = buf.subarray(off, off + 16);
  off += 16;
  const nonce = buf.subarray(off, off + 12);
  const tag = buf.subarray(off + 12, off + 28);
  const ct = buf.subarray(off + 28);
  const key = hkdf(TRANSPORT, salt, kind.info);
  const d = crypto.createDecipheriv('aes-256-gcm', key, nonce);
  d.setAAD(headerBytes);
  d.setAuthTag(tag);
  const gz = Buffer.concat([d.update(ct), d.final()]);
  const plain = zlib.gunzipSync(gz, { maxOutputLength: kind.max });
  return {
    kind: kindName,
    header: JSON.parse(headerBytes.toString('utf8')),
    payload: JSON.parse(plain.toString('utf8')),
  };
}

// ---------------------------------------------------------------------------
// PNG encoder (RGBA, filter 0, deterministic zlib)
// ---------------------------------------------------------------------------
function pngChunk(type, data) {
  const t = Buffer.from(type, 'latin1');
  const len = u32be(data.length);
  const crc = u32be(crc32(Buffer.concat([t, data])));
  return Buffer.concat([len, t, data, crc]);
}

function pngEncode(w, h, rgba) {
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0);
  ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8; // bit depth
  ihdr[9] = 6; // RGBA
  ihdr[10] = 0;
  ihdr[11] = 0;
  ihdr[12] = 0;
  const raw = Buffer.alloc((w * 4 + 1) * h);
  for (let y = 0; y < h; y++) {
    raw[y * (w * 4 + 1)] = 0;
    rgba.copy(raw, y * (w * 4 + 1) + 1, y * w * 4, (y + 1) * w * 4);
  }
  return Buffer.concat([
    Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    pngChunk('IHDR', ihdr),
    pngChunk('IDAT', zlibDeflate(raw)),
    pngChunk('IEND', Buffer.alloc(0)),
  ]);
}

// ---------------------------------------------------------------------------
// Canvas / drawing
// ---------------------------------------------------------------------------
function parseColor(c) {
  const s = String(c || '#000000');
  if (s.startsWith('#') && (s.length === 7 || s.length === 9)) {
    return [
      parseInt(s.slice(1, 3), 16),
      parseInt(s.slice(3, 5), 16),
      parseInt(s.slice(5, 7), 16),
      s.length === 9 ? parseInt(s.slice(7, 9), 16) : 255,
    ];
  }
  return [0, 0, 0, 255];
}

class Canvas {
  constructor(w, h) {
    this.w = w;
    this.h = h;
    this.data = Buffer.alloc(w * h * 4);
  }
  blend(x, y, color) {
    x = Math.round(x);
    y = Math.round(y);
    if (x < 0 || y < 0 || x >= this.w || y >= this.h) return;
    const [sr, sg, sb, sa] = color;
    if (sa <= 0) return;
    const i = (y * this.w + x) * 4;
    const da = this.data[i + 3] / 255;
    const sAlpha = sa / 255;
    const oa = sAlpha + da * (1 - sAlpha);
    if (oa <= 0) return;
    this.data[i] = Math.round((sr * sAlpha + this.data[i] * da * (1 - sAlpha)) / oa);
    this.data[i + 1] = Math.round((sg * sAlpha + this.data[i + 1] * da * (1 - sAlpha)) / oa);
    this.data[i + 2] = Math.round((sb * sAlpha + this.data[i + 2] * da * (1 - sAlpha)) / oa);
    this.data[i + 3] = Math.round(oa * 255);
  }
  fillRect(x, y, w, h, color) {
    const c = parseColor(color);
    for (let yy = y; yy < y + h; yy++)
      for (let xx = x; xx < x + w; xx++) this.blend(xx, yy, c);
  }
  fillCircle(cx, cy, r, color) {
    const c = parseColor(color);
    const r2 = r * r;
    for (let y = Math.floor(cy - r); y <= Math.ceil(cy + r); y++)
      for (let x = Math.floor(cx - r); x <= Math.ceil(cx + r); x++) {
        const dx = x - cx;
        const dy = y - cy;
        if (dx * dx + dy * dy <= r2) this.blend(x, y, c);
      }
  }
  fillEllipse(cx, cy, rx, ry, color) {
    const c = parseColor(color);
    for (let y = Math.floor(cy - ry); y <= Math.ceil(cy + ry); y++)
      for (let x = Math.floor(cx - rx); x <= Math.ceil(cx + rx); x++) {
        const dx = (x - cx) / rx;
        const dy = (y - cy) / ry;
        if (dx * dx + dy * dy <= 1) this.blend(x, y, c);
      }
  }
  ring(cx, cy, r, width, color) {
    const c = parseColor(color);
    const inner = Math.max(0, r - width / 2);
    const outer = r + width / 2;
    for (let y = Math.floor(cy - outer); y <= Math.ceil(cy + outer); y++)
      for (let x = Math.floor(cx - outer); x <= Math.ceil(cx + outer); x++) {
        const d = Math.hypot(x - cx, y - cy);
        if (d >= inner && d <= outer) this.blend(x, y, c);
      }
  }
  fillPoly(points, color) {
    const c = parseColor(color);
    let minY = Infinity;
    let maxY = -Infinity;
    for (const [, y] of points) {
      minY = Math.min(minY, y);
      maxY = Math.max(maxY, y);
    }
    for (let y = Math.floor(minY); y <= Math.ceil(maxY); y++) {
      const xs = [];
      for (let i = 0; i < points.length; i++) {
        const [x1, y1] = points[i];
        const [x2, y2] = points[(i + 1) % points.length];
        if ((y1 <= y && y2 > y) || (y2 <= y && y1 > y)) {
          xs.push(x1 + ((y - y1) / (y2 - y1)) * (x2 - x1));
        }
      }
      xs.sort((a, b) => a - b);
      for (let i = 0; i + 1 < xs.length; i += 2)
        for (let x = Math.floor(xs[i]); x <= Math.ceil(xs[i + 1]); x++) this.blend(x, y, c);
    }
  }
  thickLine(x1, y1, x2, y2, width, color) {
    const c = parseColor(color);
    const len = Math.hypot(x2 - x1, y2 - y1);
    const steps = Math.max(1, Math.ceil(len));
    for (let i = 0; i <= steps; i++) {
      const t = i / steps;
      this.fillCircle(x1 + (x2 - x1) * t, y1 + (y2 - y1) * t, width / 2, color);
    }
  }
  roundRect(cx, cy, w, h, r, color) {
    const c = parseColor(color);
    const x0 = cx - w / 2;
    const y0 = cy - h / 2;
    const x1 = cx + w / 2;
    const y1 = cy + h / 2;
    for (let y = Math.floor(y0); y <= Math.ceil(y1); y++)
      for (let x = Math.floor(x0); x <= Math.ceil(x1); x++) {
        if (x < x0 || x > x1 || y < y0 || y > y1) continue;
        const qx = Math.max(x0 + r - x, 0, x - (x1 - r));
        const qy = Math.max(y0 + r - y, 0, y - (y1 - r));
        if (Math.hypot(qx, qy) <= r) this.blend(x, y, c);
      }
  }
}

const GLYPHS = {
  A: ['010', '101', '111', '101', '101'],
  B: ['110', '101', '110', '101', '110'],
  C: ['011', '100', '100', '100', '011'],
  D: ['110', '101', '101', '101', '110'],
  E: ['111', '100', '110', '100', '111'],
  F: ['111', '100', '110', '100', '100'],
  G: ['011', '100', '101', '101', '011'],
  H: ['101', '101', '111', '101', '101'],
  I: ['111', '010', '010', '010', '111'],
  J: ['001', '001', '001', '101', '010'],
  K: ['101', '101', '110', '101', '101'],
  L: ['100', '100', '100', '100', '111'],
  M: ['101', '111', '111', '101', '101'],
  N: ['101', '111', '111', '111', '101'],
  O: ['010', '101', '101', '101', '010'],
  P: ['110', '101', '110', '100', '100'],
  Q: ['010', '101', '101', '111', '011'],
  R: ['110', '101', '110', '101', '101'],
  S: ['011', '100', '010', '001', '110'],
  T: ['111', '010', '010', '010', '010'],
  U: ['101', '101', '101', '101', '111'],
  V: ['101', '101', '101', '101', '010'],
  W: ['101', '101', '111', '111', '101'],
  X: ['101', '101', '010', '101', '101'],
  Y: ['101', '101', '010', '010', '010'],
  Z: ['111', '001', '010', '100', '111'],
  0: ['111', '101', '101', '101', '111'],
  1: ['010', '110', '010', '010', '111'],
  2: ['111', '001', '111', '100', '111'],
  3: ['111', '001', '111', '001', '111'],
  4: ['101', '101', '111', '001', '001'],
  5: ['111', '100', '111', '001', '111'],
  6: ['111', '100', '111', '101', '111'],
  7: ['111', '001', '010', '010', '010'],
  8: ['111', '101', '111', '101', '111'],
  9: ['111', '101', '111', '001', '111'],
  '-': ['000', '000', '111', '000', '000'],
  '.': ['000', '000', '000', '000', '010'],
  ' ': ['000', '000', '000', '000', '000'],
};

function textWidth(s, scale) {
  return Math.max(0, s.length * 4 * scale - scale);
}

function drawText(c, cx, cy, text, scale, color) {
  const w = textWidth(text, scale);
  const x0 = cx - w / 2;
  const y0 = cy - (5 * scale) / 2;
  for (let i = 0; i < text.length; i++) {
    const g = GLYPHS[text[i]] || GLYPHS[' '];
    const gx = x0 + i * 4 * scale;
    for (let row = 0; row < 5; row++) {
      for (let col = 0; col < 3; col++) {
        if (g[row][col] === '1') {
          c.fillRect(gx + col * scale, y0 + row * scale, scale, scale, color);
        }
      }
    }
  }
}

function label(c, text, opts = {}) {
  const scale = opts.scale || 6;
  const w = textWidth(text, scale);
  const h = 5 * scale;
  const cx = opts.x ?? c.w / 2;
  const cy = opts.y ?? c.h / 2;
  const [r, g, b] = [16, 16, 16];
  const alpha = opts.alpha ?? 190;
  for (let y = cy - h / 2 - 9; y <= cy + h / 2 + 9; y++)
    for (let x = cx - w / 2 - 12; x <= cx + w / 2 + 12; x++) c.blend(x, y, [r, g, b, alpha]);
  drawText(c, cx + 2, cy + 2, text, scale, '#000000');
  drawText(c, cx, cy, text, scale, opts.color || '#ffffff');
}

// ---------------------------------------------------------------------------
// Generated art
// ---------------------------------------------------------------------------
const W = 480;
const H = 480;

function palette(name) {
  const palettes = {
    orange: {
      base: '#e07a3f',
      dark: '#b85a28',
      light: '#f2b36b',
      hair: '#8a4b22',
      hairDark: '#6f3a18',
      accent: '#5b3a29',
    },
    lime: {
      base: '#7ac943',
      dark: '#4f8f2a',
      light: '#b7e86a',
      hair: '#5a8f2a',
      hairDark: '#3f6b1d',
      accent: '#2f4a1a',
    },
    grape: {
      base: '#8a63d2',
      dark: '#6240a8',
      light: '#c3a6f0',
      hair: '#5a3f8f',
      hairDark: '#412d6b',
      accent: '#33234f',
    },
  };
  return palettes[name] || palettes.orange;
}

function face(c, pal, kind) {
  c.fillCircle(195, 165, 9, '#2b1b12');
  c.fillCircle(285, 165, 9, '#2b1b12');
  if (kind === 'pressed') {
    c.thickLine(180, 155, 205, 175, 6, '#2b1b12');
    c.thickLine(205, 155, 180, 175, 6, '#2b1b12');
    c.thickLine(270, 155, 295, 175, 6, '#2b1b12');
    c.thickLine(295, 155, 270, 175, 6, '#2b1b12');
    c.thickLine(220, 215, 260, 215, 6, '#2b1b12');
  } else if (kind === 'blink') {
    c.thickLine(178, 170, 212, 170, 6, '#2b1b12');
    c.thickLine(268, 170, 302, 170, 6, '#2b1b12');
    c.fillCircle(240, 215, 12, '#2b1b12');
  } else if (kind === 'smile') {
    c.fillCircle(240, 215, 12, '#2b1b12');
    c.fillCircle(240, 202, 12, pal.light);
    c.thickLine(220, 215, 260, 215, 6, '#2b1b12');
  } else if (kind === 'surprised') {
    c.ring(240, 215, 16, 6, '#2b1b12');
  } else {
    c.fillCircle(240, 215, 12, '#2b1b12');
    c.fillCircle(240, 202, 12, pal.light);
    c.thickLine(220, 215, 260, 215, 6, '#2b1b12');
  }
}

function makeAsset(name, palName) {
  const pal = palette(palName);
  const c = new Canvas(W, H);
  switch (name) {
    case 'body':
      c.roundRect(240, 335, 220, 230, 60, pal.base);
      c.roundRect(240, 250, 120, 40, 20, pal.dark);
      label(c, 'BODY', { y: 350, scale: 6 });
      break;
    case 'body2':
      c.roundRect(240, 345, 250, 240, 60, pal.dark);
      label(c, 'BODY2', { y: 360, scale: 6 });
      break;
    case 'head1':
      c.fillCircle(240, 165, 125, pal.light);
      c.fillEllipse(240, 95, 120, 55, pal.hair);
      face(c, pal, 'normal');
      label(c, 'HEAD1', { y: 275, scale: 6 });
      break;
    case 'head2':
      c.fillEllipse(240, 185, 140, 105, pal.dark);
      c.fillEllipse(240, 125, 130, 45, pal.hairDark);
      face(c, pal, 'pressed');
      label(c, 'HEAD2', { y: 275, scale: 6 });
      break;
    case 'head3':
      c.fillCircle(240, 165, 125, pal.light);
      c.fillEllipse(240, 95, 120, 55, pal.hair);
      face(c, pal, 'blink');
      label(c, 'HEAD3', { y: 275, scale: 6 });
      break;
    case 'head4':
      c.fillCircle(240, 165, 125, pal.light);
      c.fillEllipse(240, 95, 120, 55, pal.hair);
      face(c, pal, 'smile');
      label(c, 'HEAD4', { y: 275, scale: 6 });
      break;
    case 'head5':
      c.fillCircle(240, 165, 125, pal.light);
      c.fillEllipse(240, 95, 120, 55, pal.hair);
      face(c, pal, 'surprised');
      label(c, 'HEAD5', { y: 275, scale: 6 });
      break;
    case 'back1':
      c.fillEllipse(240, 175, 160, 150, pal.hair);
      label(c, 'BACK1', { y: 45, scale: 6 });
      break;
    case 'back2':
      c.fillEllipse(240, 180, 165, 150, pal.hairDark);
      label(c, 'BACK2', { y: 45, scale: 6 });
      break;
    case 'side1':
      c.fillEllipse(110, 175, 45, 95, pal.hair);
      c.fillEllipse(370, 175, 45, 95, pal.hair);
      label(c, 'SIDE1', { y: 330, scale: 6 });
      break;
    case 'side2':
      c.fillEllipse(110, 185, 50, 90, pal.hairDark);
      c.fillEllipse(370, 185, 50, 90, pal.hairDark);
      label(c, 'SIDE2', { y: 330, scale: 6 });
      break;
    case 'ex1':
    case 'ex1p':
    case 'ex2':
    case 'ex2p':
    case 'ex3':
    case 'ex3p':
    case 'ex4':
    case 'ex4p': {
      const positions = {
        ex1: [80, 90],
        ex2: [400, 100],
        ex3: [90, 390],
        ex4: [400, 380],
      };
      const [x, y] = positions[name.replace(/p$/, '')];
      const colors = { ex1: '#ffd447', ex2: '#5be7f0', ex3: '#ff7ad9', ex4: '#8dff7a' };
      const col = name.endsWith('p') ? pal.dark : colors[name.replace(/p$/, '')];
      const pts = [];
      for (let i = 0; i < 10; i++) {
        const r = i % 2 === 0 ? 45 : 18;
        const a = (-Math.PI / 2) + (i * Math.PI) / 5;
        pts.push([x + Math.cos(a) * r, y + Math.sin(a) * r]);
      }
      c.fillPoly(pts, col);
      label(c, name.toUpperCase(), { x, y: y + 70, scale: 5 });
      break;
    }
    case 'partTop':
      c.fillPoly([[240, 25], [150, 125], [330, 125]], '#d94b4b');
      c.fillRect(145, 120, 190, 18, '#7a1f1f');
      c.fillCircle(240, 24, 16, '#ffd447');
      label(c, 'TOP', { y: 160, scale: 6 });
      break;
    case 'partTopShadow':
      for (let y = 378; y <= 422; y++)
        for (let x = 140; x <= 340; x++)
          if (((x - 240) / 90) ** 2 + ((y - 400) / 22) ** 2 <= 1)
            c.blend(x, y, [0, 0, 0, 90]);
      label(c, 'SHADOW', { y: 400, scale: 4 });
      break;
    case 'partTopBlink':
      c.fillPoly([[240, 25], [150, 125], [330, 125]], '#a83a3a');
      c.fillRect(145, 120, 190, 18, '#5a1717');
      c.thickLine(200, 70, 280, 70, 8, '#ffd447');
      label(c, 'BLINK', { y: 160, scale: 6 });
      break;
    case 'partTop2':
      c.ring(240, 80, 60, 14, '#ffd447');
      c.ring(240, 80, 46, 4, '#fff3b0');
      label(c, 'TOP2', { y: 180, scale: 6 });
      break;
    case 'partTop3':
      c.fillRect(232, 25, 10, 160, '#5b3a29');
      c.fillPoly([[242, 35], [360, 70], [242, 105]], '#4bb7d9');
      label(c, 'TOP3', { y: 200, scale: 6 });
      break;
    case 'partTop3Blink':
      c.fillRect(232, 25, 10, 160, '#5b3a29');
      c.fillPoly([[242, 35], [360, 70], [242, 105]], '#2d7f99');
      c.fillCircle(300, 70, 12, '#ffd447');
      label(c, 'BLINK3', { y: 200, scale: 5 });
      break;
    case 'bbTL':
      c.fillPoly([[0, 0], [130, 0], [0, 130]], '#ffd447');
      label(c, 'BBTL', { x: 80, y: 80, scale: 5 });
      break;
    case 'bbTR':
      c.fillPoly([[480, 0], [350, 0], [480, 130]], '#5be7f0');
      label(c, 'BBTR', { x: 400, y: 80, scale: 5 });
      break;
    case 'bbBL':
      c.fillPoly([[0, 480], [130, 480], [0, 350]], '#ff7ad9');
      label(c, 'BBBL', { x: 80, y: 400, scale: 5 });
      break;
    case 'bbBR':
      c.fillPoly([[480, 480], [350, 480], [480, 350]], '#8dff7a');
      label(c, 'BBBR', { x: 400, y: 400, scale: 5 });
      break;
    case 'bbSkin':
      c.fillRect(0, 0, 480, 24, '#3b3b4f');
      c.fillRect(0, 456, 480, 24, '#3b3b4f');
      c.fillRect(0, 0, 24, 480, '#3b3b4f');
      c.fillRect(456, 0, 24, 480, '#3b3b4f');
      for (let x = 40; x < 460; x += 40) c.fillCircle(x, 12, 6, '#ffd447');
      for (let x = 40; x < 460; x += 40) c.fillCircle(x, 468, 6, '#ffd447');
      label(c, 'SKIN', { x: 240, y: 240, scale: 8 });
      break;
    case 'pet1':
      c.fillCircle(210, 210, 55, '#ff5b8a');
      c.fillCircle(270, 210, 55, '#ff5b8a');
      c.fillPoly([[155, 220], [325, 220], [240, 360]], '#ff5b8a');
      label(c, 'PET1', { y: 400, scale: 5 });
      break;
    case 'pet2':
      c.fillEllipse(220, 270, 30, 22, '#5be7f0');
      c.fillRect(245, 160, 12, 120, '#5be7f0');
      c.fillPoly([[257, 160], [330, 190], [257, 220]], '#5be7f0');
      label(c, 'PET2', { y: 400, scale: 5 });
      break;
    case 'pet3': {
      const pts = [];
      for (let i = 0; i < 10; i++) {
        const r = i % 2 === 0 ? 85 : 35;
        const a = (-Math.PI / 2) + (i * Math.PI) / 5;
        pts.push([240 + Math.cos(a) * r, 240 + Math.sin(a) * r]);
      }
      c.fillPoly(pts, '#ffd447');
      label(c, 'PET3', { y: 400, scale: 5 });
      break;
    }
    case 'ear':
      c.fillPoly([[150, 140], [185, 20], [245, 120]], pal.base);
      c.fillPoly([[330, 140], [295, 20], [235, 120]], pal.base);
      c.fillPoly([[165, 135], [188, 55], [225, 120]], '#ffb7c9');
      c.fillPoly([[315, 135], [292, 55], [255, 120]], '#ffb7c9');
      label(c, 'EAR', { y: 190, scale: 6 });
      break;
    case 'tail':
      for (let i = 0; i <= 80; i++) {
        const t = i / 80;
        const x = 310 + 130 * t;
        const y = 360 - 160 * Math.sin((t * Math.PI) / 2);
        c.fillCircle(x, y, 22 - 8 * t, pal.hair);
      }
      c.fillCircle(440, 200, 12, pal.hairDark);
      label(c, 'TAIL', { y: 420, scale: 6 });
      break;
    case 'wing':
      c.fillPoly([[120, 250], [20, 170], [60, 250], [20, 330], [120, 300]], '#9ad7ff');
      c.fillPoly([[130, 260], [70, 220], [90, 260], [70, 300], [130, 290]], '#d6f0ff');
      label(c, 'WING', { y: 360, scale: 6 });
      break;
    case 'prop':
      c.fillCircle(360, 360, 48, '#ffd447');
      c.fillCircle(345, 345, 14, '#fff3b0');
      label(c, 'PROP', { y: 430, scale: 5 });
      break;
    default:
      c.fillRect(0, 0, W, H, '#ff00ff');
      label(c, name.toUpperCase(), { scale: 5 });
  }
  return pngEncode(W, H, c.data);
}

function makeAssets(palName, names) {
  const out = {};
  for (const name of names) out[name] = makeAsset(name, palName);
  return out;
}

// ---------------------------------------------------------------------------
// Audio
// ---------------------------------------------------------------------------
function wav(freq, dur = 0.22, rate = 8000) {
  const n = Math.floor(rate * dur);
  const data = Buffer.alloc(44 + n * 2);
  data.write('RIFF', 0);
  data.writeUInt32LE(36 + n * 2, 4);
  data.write('WAVE', 8);
  data.write('fmt ', 12);
  data.writeUInt32LE(16, 16);
  data.writeUInt16LE(1, 20);
  data.writeUInt16LE(1, 22);
  data.writeUInt32LE(rate, 24);
  data.writeUInt32LE(rate * 2, 28);
  data.writeUInt16LE(2, 32);
  data.writeUInt16LE(16, 34);
  data.write('data', 36);
  data.writeUInt32LE(n * 2, 40);
  for (let i = 0; i < n; i++) {
    const env = Math.min(1, i / (rate * 0.01), (n - i) / (rate * 0.02));
    const v = Math.sin((2 * Math.PI * freq * i) / rate) * 9000 * env;
    data.writeInt16LE(Math.round(v), 44 + i * 2);
  }
  return data;
}

// ---------------------------------------------------------------------------
// Minimal animated GIF encoder (2 colors + transparent index, 4-color table)
// ---------------------------------------------------------------------------
function gifLzw(indices, minCodeSize = 2) {
  const clear = 1 << minCodeSize;
  const end = clear + 1;
  const codeSize = minCodeSize + 1;
  const out = [];
  let buf = 0;
  let n = 0;
  const emit = (code) => {
    for (let i = 0; i < codeSize; i++) {
      buf |= ((code >> i) & 1) << n;
      n++;
      if (n === 8) {
        out.push(buf);
        buf = 0;
        n = 0;
      }
    }
  };
  // Clear before every pixel keeps the LZW code size fixed at 3 bits.
  for (const v of indices) {
    emit(clear);
    emit(v);
  }
  emit(end);
  if (n) out.push(buf);
  return out;
}

function gifEncode(w, h, frames, palette, delay = 50) {
  const chunks = [];
  const u16 = (v) => Buffer.from([v & 255, (v >> 8) & 255]);
  chunks.push(Buffer.from('GIF89a', 'latin1'));
  chunks.push(Buffer.from([w & 255, (w >> 8) & 255, h & 255, (h >> 8) & 255, 0x81, 0, 0]));
  for (let i = 0; i < 4; i++) {
    const c = palette[i] || [0, 0, 0];
    chunks.push(Buffer.from([c[0], c[1], c[2]]));
  }
  // Netscape loop extension: loop forever.
  chunks.push(Buffer.from([0x21, 0xff, 0x0b]));
  chunks.push(Buffer.from('NETSCAPE2.0', 'latin1'));
  chunks.push(Buffer.from([0x03, 0x01, 0x00, 0x00, 0x00]));
  for (const frame of frames) {
    // Graphic control extension: disposal=2, transparent index 0.
    chunks.push(Buffer.from([0x21, 0xf9, 0x04, 0x09]));
    chunks.push(Buffer.from([delay & 255, (delay >> 8) & 255, 0x00, 0x00]));
    // Image descriptor.
    chunks.push(Buffer.from([0x2c]));
    chunks.push(Buffer.from([0, 0, 0, 0]));
    chunks.push(u16(w), u16(h), Buffer.from([0x00]));
    // LZW minimum code size.
    chunks.push(Buffer.from([0x02]));
    const bytes = gifLzw(frame, 2);
    for (let i = 0; i < bytes.length; i += 255) {
      const part = bytes.slice(i, i + 255);
      chunks.push(Buffer.from([part.length]));
      chunks.push(Buffer.from(part));
    }
    chunks.push(Buffer.from([0x00]));
  }
  chunks.push(Buffer.from([0x3b]));
  return Buffer.concat(chunks);
}

function canvasToGifIndices(c, palette) {
  const idx = new Uint8Array(c.w * c.h);
  for (let i = 0; i < idx.length; i++) {
    const a = c.data[i * 4 + 3];
    if (a < 128) {
      idx[i] = 0;
      continue;
    }
    const r = c.data[i * 4];
    const g = c.data[i * 4 + 1];
    const b = c.data[i * 4 + 2];
    let best = 1;
    let bestD = Infinity;
    for (let p = 1; p < 4; p++) {
      const [pr, pg, pb] = palette[p];
      const d = (r - pr) ** 2 + (g - pg) ** 2 + (b - pb) ** 2;
      if (d < bestD) {
        bestD = d;
        best = p;
      }
    }
    idx[i] = best;
  }
  return idx;
}

function makeBlinkGif() {
  const palette = [
    [0, 0, 0],
    [242, 179, 107],
    [43, 27, 18],
    [255, 255, 255],
  ];
  const frames = [];
  {
    const c = new Canvas(96, 96);
    c.fillCircle(48, 52, 38, '#f2b36b');
    c.fillCircle(36, 46, 5, '#2b1b12');
    c.fillCircle(60, 46, 5, '#2b1b12');
    c.fillRect(44, 66, 8, 3, '#2b1b12');
    frames.push(canvasToGifIndices(c, palette));
  }
  {
    const c = new Canvas(96, 96);
    c.fillCircle(48, 52, 38, '#f2b36b');
    c.fillRect(30, 44, 12, 4, '#2b1b12');
    c.fillRect(54, 44, 12, 4, '#2b1b12');
    c.fillRect(44, 66, 8, 3, '#2b1b12');
    frames.push(canvasToGifIndices(c, palette));
  }
  return gifEncode(96, 96, frames, palette, 50);
}

// ---------------------------------------------------------------------------
// Data URL helpers
// ---------------------------------------------------------------------------
function pngDataUrl(buf) {
  return 'data:image/png;base64,' + buf.toString('base64');
}
function audioDataUrl(buf) {
  return 'data:audio/wav;base64,' + buf.toString('base64');
}
function gifDataUrl(buf) {
  return 'data:image/gif;base64,' + buf.toString('base64');
}
function dataUrlSummary(v) {
  const m = /^data:([^;]+);base64,([\s\S]+)$/i.exec(String(v || ''));
  if (!m) return { invalid: true };
  const buf = Buffer.from(m[2], 'base64');
  return { mime: m[1].toLowerCase(), bytes: buf.length, sha256: sha256(buf) };
}

// ---------------------------------------------------------------------------
// Protected-package owner hash (matches the original's scrypt parameters)
// ---------------------------------------------------------------------------
function ownerFor(code, seed) {
  const t = String(code || '').trim();
  if (t.length < 4) return null;
  const salt = crypto.createHash('sha256').update('owner-salt:' + seed).digest().subarray(0, 16);
  const h = crypto.scryptSync(t.normalize('NFC'), salt, 32, {
    N: 16384,
    r: 8,
    p: 1,
    maxmem: 67108864,
  });
  return { s: salt.toString('base64'), h: Buffer.from(h).toString('base64') };
}

// ---------------------------------------------------------------------------
// Payload builders
// ---------------------------------------------------------------------------
function author(overrides = {}) {
  return {
    name: 'OCoD Cleanroom',
    link: '',
    stream: '',
    edit: '',
    redistribute: '',
    credit: '',
    memo: '',
    warn: [],
    warnEtc: '',
    ...overrides,
  };
}

function imageMap(assets, slots) {
  const images = {};
  for (const slot of slots) images[slot] = pngDataUrl(assets[slot]);
  return images;
}

const ALL_SLOTS = [
  'back1', 'back2', 'head1', 'head2', 'head3', 'head4', 'head5',
  'body', 'body2', 'side1', 'side2',
  'ex1', 'ex1p', 'ex2', 'ex2p', 'ex3', 'ex3p', 'ex4', 'ex4p',
];

function minimalCharacter({ id, name, pal, lines, idleLines, authorObj, extra = {} }) {
  const assets = makeAssets(pal, ['body', 'head1', 'head2']);
  return {
    id,
    name,
    author: author(authorObj),
    idleMs: 60000,
    bubbleMs: 1500,
    inOrder: true,
    noRepeat: true,
    bubble: true,
    sound: false,
    soundType: 'click',
    lines,
    idleLines,
    images: imageMap(assets, ['body', 'head1', 'head2']),
    imageUrls: {},
    ...extra,
  };
}

function rigDefaults(kind) {
  const s = (n) => Array.from({ length: n }, () => 1 / n);
  if (kind === 'ear') {
    return {
      off: [[0, 0], [0, 0]],
      mirror: true,
      tilt: 0,
      bones: s(3),
      range: 35,
      amp: 25,
      period: 1.2,
      soft: 40,
      bounce: 89,
      kick: 94,
      wobble: 49,
      opp: true,
      scale: 100,
      fade: 0,
      when: 'down',
      onDrag: true,
      onBeat: true,
      beatSway: false,
      onKey: true,
      name: '',
      onPet: 'none',
      petAmt: 50,
      stiff: 0,
      drop: false,
      ant: false,
      split: false,
      tiltL: null,
      pinAt: null,
      tipAt: null,
      pinAtL: null,
      tipAtL: null,
    };
  }
  if (kind === 'tail') {
    return {
      name: '',
      root: [400, 650],
      dir: 8,
      curve: -14,
      bones: s(12),
      range: 90,
      amp: 90,
      period: 2.7,
      soft: 88,
      bounce: 69,
      kick: 67,
      wobble: 55,
      scale: 100,
      fade: 0,
      flip: false,
      fur: 'L',
      when: 'down',
      onBeat: true,
      head: false,
      rel: null,
      rev: false,
      vtip: false,
      onPet: 'none',
      petAmt: 50,
      pinAt: null,
      tipAt: null,
    };
  }
  if (kind === 'wing') {
    return {
      name: '',
      style: 'bat',
      x: 0,
      y: -27,
      tilt: -19,
      bones: s(6),
      range: 30,
      amp: 64,
      period: 1.4,
      down: 50,
      soft: 41,
      bounce: 79,
      depth: 76,
      flaps: 1,
      flapT: 0.45,
      scale: 100,
      fade: 0,
      when: 'down',
      split: false,
      sym: true,
      xL: null,
      yL: null,
      tiltL: null,
      pinAt: null,
      tipAt: null,
      pinAtL: null,
      tipAtL: null,
    };
  }
  return {
    name: '',
    anim: 'hop',
    pos: [0, 0],
    scale: 100,
    when: 'down',
    follow: 'none',
    hopH: 60,
    hopEvery: 1.6,
    hopSquash: 30,
    flRx: 40,
    flRy: 24,
    flPeriod: 4,
    shAmp: 8,
    shDeg: 6,
    shDur: 0.6,
    shEvery: 3,
    rlX1: -120,
    rlX2: 120,
    rlSpeed: 150,
    rlPause: 0.8,
    pkDx: 90,
    pkDy: -70,
    pkWait: 2.2,
    pkStay: 1.2,
  };
}

function partSettingsHeadTop(overrides = {}) {
  return {
    On: true,
    Size: 100,
    X: 0,
    Y: 0,
    Hop: 14,
    Soft: 30,
    Squish: 35,
    Every: 1600,
    Press: true,
    When: 'down',
    Beat: true,
    Type: false,
    ShOn: true,
    ShShape: 'oval',
    ShColor: '#000000',
    ShAlpha: 0.3,
    ShSize: 100,
    ShBlur: 3,
    ShY: 0,
    ShFloat: true,
    ...overrides,
  };
}

function fullCharacter() {
  const pal = 'orange';
  const assets = makeAssets(pal, [
    ...ALL_SLOTS,
    'partTop', 'partTopShadow', 'partTopBlink',
    'partTop2', 'partTop3', 'partTop3Blink',
    'bbTL', 'bbTR', 'bbBL', 'bbBR', 'bbSkin',
    'pet1', 'pet2', 'pet3',
    'ear', 'tail', 'wing', 'prop',
  ]);
  const sound = wav(440);
  const ls1 = wav(660);
  const ls2 = wav(880);
  const ls3 = wav(330);
  const parts = [
    {
      slot: 'headTop',
      name: '蝴蝶结',
      settings: partSettingsHeadTop({
        Size: 120,
        Y: -10,
        Hop: 20,
        Soft: 40,
        Squish: 40,
        Every: 1800,
        When: 'up',
        Beat: false,
        ShShape: 'soft',
        ShColor: '#552200',
        ShAlpha: 0.35,
        ShSize: 110,
        ShBlur: 4,
        ShY: 2,
      }),
      image: pngDataUrl(assets.partTop),
      shadowImage: pngDataUrl(assets.partTopShadow),
      blinkImage: pngDataUrl(assets.partTopBlink),
    },
    {
      slot: 'headTop2',
      name: '光环',
      settings: partSettingsHeadTop({
        Size: 90,
        X: 0,
        Y: -30,
        Every: 2200,
        ShShape: 'dot',
        ShColor: '#aa8800',
        ShAlpha: 0.25,
      }),
      image: pngDataUrl(assets.partTop2),
    },
    {
      slot: 'headTop3',
      name: '小旗',
      settings: partSettingsHeadTop({
        Size: 80,
        X: 20,
        Y: -20,
        Hop: 8,
        Soft: 20,
        Squish: 20,
        Every: 1200,
        Beat: true,
        ShShape: 'png',
      }),
      image: pngDataUrl(assets.partTop3),
      blinkImage: pngDataUrl(assets.partTop3Blink),
    },
    {
      slot: 'ear',
      name: '',
      rigId: 'r1',
      on: true,
      at: 'back',
      settings: {
        ...rigDefaults('ear'),
        name: '猫耳',
        soft: 55,
        bounce: 70,
        drop: true,
      },
      image: pngDataUrl(assets.ear),
    },
    {
      slot: 'tail',
      name: '',
      rigId: 'r2',
      on: true,
      at: 'back',
      settings: {
        ...rigDefaults('tail'),
        name: '尾巴',
        dir: 20,
        curve: -30,
        fur: 'R',
        onPet: 'wag',
      },
      image: pngDataUrl(assets.tail),
    },
    {
      slot: 'wing',
      name: '',
      rigId: 'r3',
      on: true,
      at: 'back',
      settings: {
        ...rigDefaults('wing'),
        name: '翅膀',
        flaps: 2,
        y: -40,
      },
      image: pngDataUrl(assets.wing),
    },
    {
      slot: 'prop',
      name: '',
      rigId: 'r4',
      on: true,
      at: 'front',
      settings: {
        ...rigDefaults('prop'),
        name: '小球',
        anim: 'hop',
        hopH: 80,
        hopEvery: 2,
      },
      image: pngDataUrl(assets.prop),
    },
  ];
  return {
    id: 'sample-full',
    name: '净室样本·彩绘',
    author: author({
      name: '净室作者',
      link: 'https://x.com/ocod_cleanroom',
      stream: 'yes',
      edit: 'no',
      redistribute: 'no',
      credit: 'yes',
      memo: 'cleanroom full sample',
      warn: ['flash', 'bugs'],
      warnEtc: 'sample warning',
    }),
    depth: 7,
    hold: 140,
    squash: 4,
    bubbleMs: 1800,
    volume: 0.5,
    idleMs: 45000,
    bubble: true,
    breathe: true,
    breathMs: 3600,
    breathAmp: 14,
    blinkMs: 3600,
    sound: audioDataUrl(sound),
    soundType: 'custom',
    soundPitch: 2,
    noRepeat: true,
    inOrder: true,
    reactStyle: 'bob',
    reactAmp: 1.4,
    beatMotion: {
      style: 'bob', amp: 1.4, dy: -3.2, rot: 0, alt: false, dur: 180, blinkEvery: 2,
    },
    keyMotion: {
      style: 'press', amp: 1.1, dy: 3, rot: 0, alt: false, dur: 120, blinkEvery: 0, fast: 'cap', gap: 220,
    },
    editLock: false,
    shiverHead: 2,
    shiverBody: 1,
    neckOn: true,
    neckDepth: 5,
    neckColor: '#112233',
    neckAlpha: 0.25,
    floorOn: true,
    floorDepth: 4,
    floorColor: '#223344',
    floorAlpha: 0.3,
    olMode: 'line',
    olColor: '#ffffff',
    olWidth: 4,
    olAlpha: 0.8,
    bubbleStyle: {
      preset: 'dark',
      bg: '#101018',
      fg: '#f0f0f0',
      line: '#f0f0f024',
      radius: 20,
      font: 15,
      size: 110,
      weight: 700,
      shadow: true,
      tail: true,
      gap: 14,
      align: 'left',
      family: '',
      grad: true,
      bg2: '#202038',
      gdir: 'v',
      frame: 'solid',
      lineW: 3,
      lineC: '#8899ff',
      cSize: 32,
      cOver: 'half',
      cPop: true,
      cLayer: 'front',
      skSlice: 24,
      skW: 16,
      skRep: 'stretch',
      cUse: true,
      skUse: true,
      shape: 'hand',
      appear: 'pop',
      textFx: 'wave',
      speed: 1.2,
      split: true,
      speedA: 1.1,
      speedT: 1.3,
      speedS: 1.4,
    },
    bubbleStyles: [
      {
        id: 'u_glow',
        name: 'Glow',
        o: {
          bg: '#101022',
          fg: '#aaddff',
          shape: 'focus',
          appear: 'fade',
          textFx: 'blur',
          speed: 1.3,
          grad: true,
          bg2: '#223355',
          gdir: 'd',
          lineC: '#88ccff',
          lineW: 3,
          radius: 22,
        },
      },
      {
        id: 'u_paper',
        name: 'Paper',
        o: {
          bg: '#f3ead6',
          fg: '#3a2f1f',
          shape: 'paper',
          frame: 'double',
          lineW: 3,
          align: 'center',
        },
      },
    ],
    lineStyles: {
      lines: ['', 'u_glow', 'p_shout', '', 'u_paper'],
      idleLines: ['', 'p_think', ''],
      sp_alarm_before: ['p_shout', ''],
      sp_todo_after: ['', 'p_think'],
    },
    petOn: true,
    petKinds: ['heart', 'note', 'img1'],
    dragOn: true,
    dragSwing: 120,
    dragBodyY: 8,
    dropSquish: 90,
    dropWobble: 80,
    liftShadow: true,
    liftShadowY: 3,
    dragBodyS: 95,
    liftHide: [],
    liftStill: ['r1', 'L:side'],
    liftAmt: { r2: 60 },
    liftSad: ['r3'],
    liftOnly: ['r4'],
    liftAux: { r2: { drop: 30, spd: 40, fol: 50 }, 'L:side': { fol: 20 } },
    startSize: 120,
    petSizeMode: 'fixed',
    petSize: 30,
    petFill: '#ff00ff',
    petLine: '#00ffff',
    petColorMode: 'each',
    petKindColors: { heart: '#ff0000', note: '#00ff00' },
    petLineFollow: true,
    petLineW: 3,
    petGlow: false,
    petEdge: 'line',
    petGlowSize: 6,
    petSway: 4,
    petAmount: 5,
    petSpeed: 1200,
    petTilt: 4,
    petArea: 'head',
    petCut: 20,
    bubbleX: 12,
    bubbleY: -6,
    bubbleBehind: false,
    soundIdle: true,
    headBack: false,
    lineSoundOn: true,
    lineSoundVol: 0.8,
    partOrder: [
      'r1', 'r2', 'r3', 'L:back', 'body', 'L:ex1', 'head', 'L:side',
      'L:ex2', 'L:ex3', 'L:ex4', 'r4',
    ],
    layers: {
      backPos: 'behindBody', backMove: 'fixed', backX: 12, backY: -8,
      sidePos: 'front', sideMove: 'head',
      ex1Pos: 'behindHead', ex1Move: 'fixed', ex1X: 6, ex1Y: 4,
      ex4Pos: 'front', ex4Move: 'head', ex4X: -10, ex4Y: 6,
    },
    fontName: '',
    lines: [
      '你好，我是彩绘样本。',
      '这条台词使用 Glow 气泡样式。',
      '第三行\n带有换行。',
      '这条使用内置大喊样式。',
      '逗号,不是列,会原样保留。',
    ],
    idleLines: ['（发呆）……', '这条自语使用内置自言自语样式。', '要不要休息一下？'],
    sp_alarm_before: ['闹钟要响了。{memo}', '还有一分钟。'],
    sp_alarm_after: ['关掉了。', '再睡五分钟……'],
    sp_pill_before: ['该吃药了。{memo}', '别忘了喝水。'],
    sp_pill_after: ['吃完了。', '今天的药也吃了。'],
    sp_water_before: ['该喝水了。', '{memo}'],
    sp_water_after: ['喝完了。', '水分补充完成。'],
    sp_todo_before: ['还有事情没做。{memo}', '清单在这里。'],
    sp_todo_after: ['完成一件。', '做得不错。'],
    images: imageMap(assets, ALL_SLOTS),
    imageUrls: {},
    lineSounds: {
      lines: [
        { f: 'lsnd_click1.wav', a: 0.02, b: 0.18, v: 1.2 },
        null,
        { f: 'lsnd_click2.wav', a: 0, b: 0, v: 0.8 },
        null,
        null,
      ],
      idleLines: [null, { f: 'lsnd_idle01.wav', a: 0.01, b: 0, v: 0.6 }, null],
    },
    lineSoundFiles: {
      'lsnd_click1.wav': audioDataUrl(ls1),
      'lsnd_click2.wav': audioDataUrl(ls2),
      'lsnd_idle01.wav': audioDataUrl(ls3),
    },
    parts,
    rigCommon: { mesh: 14, joint: 55, tex: 0.7, lag: 5 },
    bubbleImages: {
      tl: pngDataUrl(assets.bbTL),
      tr: pngDataUrl(assets.bbTR),
      bl: pngDataUrl(assets.bbBL),
      br: pngDataUrl(assets.bbBR),
      skin: pngDataUrl(assets.bbSkin),
    },
    petImages: {
      1: pngDataUrl(assets.pet1),
      2: pngDataUrl(assets.pet2),
      3: pngDataUrl(assets.pet3),
    },
  };
}

function multiCharacters() {
  const lime = makeAssets('lime', ['body', 'head1', 'head2']);
  const grape = makeAssets('grape', ['body', 'head1', 'head2']);
  const orange = makeAssets('orange', ['body', 'head1', 'head2']);
  return [
    {
      id: 'sample-lime',
      name: 'Cleanroom Sample Lime',
      author: author({ name: 'Cleanroom', memo: 'English sample' }),
      idleMs: 30000,
      bubbleMs: 1400,
      inOrder: true,
      bubble: true,
      sound: false,
      soundType: 'click',
      lines: ['Hello from the lime sample.', 'This is the second click line.'],
      idleLines: ['The lime sample is idling.'],
      images: {
        body: pngDataUrl(lime.body),
        head1: pngDataUrl(lime.head1),
        head2: pngDataUrl(lime.head2),
      },
      imageUrls: {},
    },
    {
      id: 'sample-jp',
      name: 'クリーンルーム見本',
      author: author({ name: 'Cleanroom' }),
      idleMs: 20000,
      bubbleMs: 1400,
      inOrder: true,
      bubble: true,
      sound: false,
      soundType: 'click',
      lines: ['クリーンルーム見本です。', '2 行目のクリック台詞です。'],
      idleLines: ['ひとりごとです。'],
      // head2 is intentionally absent: the importer must fall back head1.
      images: {
        body: pngDataUrl(grape.body),
        head1: pngDataUrl(grape.head1),
      },
      imageUrls: {},
    },
    {
      id: 'unnamed-sample-orange',
      // name is intentionally absent: the importer falls back to id.
      idleMs: 0,
      inOrder: true,
      bubble: true,
      sound: false,
      lines: ['未命名角色也能导入。'],
      idleLines: [],
      images: {
        body: pngDataUrl(orange.body),
        head1: pngDataUrl(orange.head1),
        head2: pngDataUrl(orange.head2),
      },
      imageUrls: {},
    },
  ];
}

function headtopPartPackage() {
  const assets = makeAssets('orange', ['partTop', 'partTopShadow', 'partTopBlink', 'partTop2', 'partTop3', 'partTop3Blink']);
  return {
    format: 'bug-clicker-part',
    version: 1,
    exportedAt: STAMP,
    from: '净室样本·彩绘',
    parts: [
      {
        slot: 'headTop',
        name: '蝴蝶结',
        settings: partSettingsHeadTop({
          Size: 120,
          X: 0,
          Y: -10,
          Hop: 20,
          Soft: 40,
          Squish: 40,
          Every: 1800,
          When: 'up',
          Beat: false,
          ShShape: 'soft',
          ShColor: '#552200',
          ShAlpha: 0.35,
          ShSize: 110,
          ShBlur: 4,
          ShY: 2,
        }),
        image: pngDataUrl(assets.partTop),
        shadowImage: pngDataUrl(assets.partTopShadow),
        blinkImage: pngDataUrl(assets.partTopBlink),
      },
      {
        slot: 'headTop2',
        name: '光环',
        settings: partSettingsHeadTop({
          Size: 90,
          Y: -30,
          Every: 2200,
          ShShape: 'dot',
          ShColor: '#aa8800',
          ShAlpha: 0.25,
        }),
        image: pngDataUrl(assets.partTop2),
      },
      {
        slot: 'headTop3',
        name: '小旗',
        settings: partSettingsHeadTop({
          Size: 80,
          X: 20,
          Y: -20,
          Hop: 8,
          Soft: 20,
          Squish: 20,
          Every: 1200,
          ShShape: 'png',
        }),
        image: pngDataUrl(assets.partTop3),
        blinkImage: pngDataUrl(assets.partTop3Blink),
      },
    ],
    rigCommon: { mesh: 11, joint: 60, tex: 0.6, lag: 0 },
  };
}

function rigPartPackage() {
  const assets = makeAssets('orange', ['ear', 'tail', 'wing', 'prop']);
  return {
    format: 'bug-clicker-part',
    version: 1,
    exportedAt: STAMP,
    from: '净室样本·彩绘',
    parts: [
      {
        slot: 'ear',
        name: '',
        rigId: 'r1',
        on: true,
        at: 'mid',
        settings: { ...rigDefaults('ear'), name: '猫耳', soft: 55, bounce: 70, drop: true },
        image: pngDataUrl(assets.ear),
      },
      {
        slot: 'tail',
        name: '',
        rigId: 'r2',
        on: true,
        at: 'back',
        settings: { ...rigDefaults('tail'), name: '尾巴', dir: 20, curve: -30, fur: 'R', onPet: 'wag' },
        image: pngDataUrl(assets.tail),
      },
      {
        slot: 'wing',
        name: '',
        rigId: 'r3',
        on: true,
        at: 'back',
        settings: { ...rigDefaults('wing'), name: '翅膀', flaps: 2, y: -40 },
        image: pngDataUrl(assets.wing),
      },
      {
        slot: 'prop',
        name: '',
        rigId: 'r4',
        on: false,
        at: 'front',
        settings: { ...rigDefaults('prop'), name: '小球', anim: 'hop', hopH: 80, hopEvery: 2 },
        image: pngDataUrl(assets.prop),
      },
    ],
    rigCommon: { mesh: 14, joint: 55, tex: 0.7, lag: 5 },
  };
}

function legacyCharacter(name, pal, withDefaultsOnly) {
  const assets = makeAssets(pal, ['body', 'head1', 'head2']);
  const base = {
    name,
    images: withDefaultsOnly
      ? { body: pngDataUrl(assets.body), head1: pngDataUrl(assets.head1) }
      : {
          body: pngDataUrl(assets.body),
          head1: pngDataUrl(assets.head1),
          head2: pngDataUrl(assets.head2),
        },
  };
  if (withDefaultsOnly) return base;
  return {
    ...base,
    author: author({ name: 'Legacy Author', memo: 'legacy sample' }),
    idleMs: 45000,
    bubble: true,
    sound: false,
    inOrder: true,
    lines: ['旧版点击台词一。', '旧版点击台词二。'],
    idleLines: ['旧版自言自语。'],
  };
}

// ---------------------------------------------------------------------------
// Line file parser (same grammar as docs/specs/lines-files.md)
// ---------------------------------------------------------------------------
function parseLines(text) {
  const clickRe = /^\s*\[\s*(누를\s*때|누를때|클릭|lines)[^\]]*\]\s*$/i;
  const idleRe = /^\s*\[\s*(혼잣말|혼자말|idle)[^\]]*\]\s*$/i;
  const styleRe = /^\{([^{}\n]{1,20})\}\s*([\s\S]*)$/;
  const memoRe = /^\s*(메모|memo|メモ|備註)\s*$/i;
  const result = {
    sections: false,
    lines: [],
    idleLines: [],
    plain: [],
    items: { lines: [], idleLines: [], plain: [] },
  };
  let current = 'plain';
  String(text || '')
    .replace(/^﻿/, '')
    .split(/\r\n|\r|\n/)
    .forEach((rawLine) => {
      const line = rawLine.trim();
      if (!line) return;
      if (clickRe.test(line)) {
        current = 'lines';
        result.sections = true;
        return;
      }
      if (idleRe.test(line)) {
        current = 'idleLines';
        result.sections = true;
        return;
      }
      if (line.startsWith('#') || line.startsWith('//')) return;
      const unescaped = line.replace(/\\n/g, '\n');
      const m = styleRe.exec(unescaped);
      const item =
        m && !memoRe.test(m[1]) && m[2].trim()
          ? { raw: unescaped, text: m[2].trim(), style: m[1].trim() }
          : { raw: unescaped, text: unescaped, style: '' };
      result[current].push(unescaped);
      result.items[current].push(item);
    });
  return result;
}

function lineSummary(text) {
  const p = parseLines(text);
  return {
    sections: p.sections,
    lines: p.items.lines.map((x) => x.text),
    linesRaw: p.items.lines.map((x) => x.raw),
    lineStyles: p.items.lines.map((x) => x.style),
    idleLines: p.items.idleLines.map((x) => x.text),
    idleLinesRaw: p.items.idleLines.map((x) => x.raw),
    idleStyles: p.items.idleLines.map((x) => x.style),
    plain: p.items.plain.map((x) => x.text),
  };
}

// ---------------------------------------------------------------------------
// Protected-package payload builders
// ---------------------------------------------------------------------------
function protectedCharacter({ id, name, pal = 'orange', lines, idleLines, editLock = false, editBase, editSty }) {
  const assets = makeAssets(pal, ['body', 'head1', 'head2']);
  const c = {
    id,
    name,
    author: author({
      name: '净室作者',
      link: 'https://x.com/ocod_cleanroom',
      memo: 'protected cleanroom sample',
      stream: 'yes',
      edit: 'no',
      redistribute: 'no',
      credit: 'yes',
    }),
    depth: 6,
    hold: 130,
    bubbleMs: 1600,
    idleMs: 45000,
    inOrder: true,
    noRepeat: true,
    bubble: true,
    sound: false,
    soundType: 'click',
    lines,
    idleLines,
    images: imageMap(assets, ['body', 'head1', 'head2']),
    imageUrls: {},
  };
  if (editLock) {
    c.editLock = true;
    c.editBase = editBase;
    c.editSty = editSty;
    c.lineStyles = editSty;
  }
  return c;
}

function protectedBaseLines() {
  return {
    lines: ['原始点击一。', '原始点击二。'],
    idleLines: ['原始自语一。'],
  };
}

function protectedFullLines() {
  return {
    lines: ['原始点击一。', '原始点击二。', '新增点击三。'],
    idleLines: ['原始自语一。', '新增自语二。'],
  };
}

function protectedBaselines() {
  return { lines: ['原始点击一。', '原始点击二。'], idleLines: ['原始自语一。'] };
}

function protectedStyles() {
  return { lines: ['p_shout', ''], idleLines: [''] };
}

function protectedFullStyles() {
  return { lines: ['p_shout', '', ''], idleLines: ['', ''] };
}

// ---------------------------------------------------------------------------
// Sample definitions
// ---------------------------------------------------------------------------
function buildSamples() {
  const minimalPayload = {
    format: 'bug-clicker',
    version: 1,
    app: APP,
    exportedAt: STAMP,
    characters: [
      minimalCharacter({
        id: 'sample-minimal',
        name: '净室样本·小橙',
        pal: 'orange',
        authorObj: { name: 'OCoD Cleanroom', memo: 'minimal sample' },
        lines: ['你好，我是小橙。', '这是一条点击台词。'],
        idleLines: ['（小橙在发呆。）'],
      }),
    ],
  };
  const fullPayload = {
    format: 'bug-clicker',
    version: 1,
    app: APP,
    exportedAt: STAMP,
    characters: [fullCharacter()],
  };
  const multiPayload = {
    format: 'bug-clicker',
    version: 1,
    app: APP,
    exportedAt: STAMP,
    characters: multiCharacters(),
  };
  const unknownChar = minimalCharacter({
    id: 'sample-unknown',
    name: '净室样本·未知字段',
    pal: 'orange',
    authorObj: { name: 'OCoD Cleanroom', memo: 'unknown-field sample' },
    lines: ['未知字段不应阻止导入。', '再导出时应保留未知字段。'],
    idleLines: ['（未知字段样本在发呆。）'],
  });
  unknownChar.x_future_field = 'keep-character';
  unknownChar.x_future_object = { nested: [1, 2, 3] };
  unknownChar.author.x_future_author = 'keep-author';
  unknownChar.bubbleStyle = { preset: 'dark', x_future_bubble: 42 };
  unknownChar.images.xFuture = pngDataUrl(makeAsset('body', 'orange'));
  unknownChar.partOrder = ['body', 'head', 'xFutureLayer'];
  unknownChar.lineStyles = { lines: ['', ''], x_future_lines: ['', ''] };
  unknownChar.groupNames = { g1: 'Future Group' };
  unknownChar.sp_g1_before = ['future special line'];
  const unknownPayload = {
    format: 'bug-clicker',
    version: 1,
    app: APP,
    exportedAt: STAMP,
    x_sample: { keep: true },
    characters: [unknownChar],
  };
  const gifChar = minimalCharacter({
    id: 'sample-gif',
    name: '净室样本·动图',
    pal: 'lime',
    authorObj: { name: 'OCoD Cleanroom', memo: 'animated GIF sample' },
    lines: ['这是一张会眨眼的动图。', 'head3 是 GIF 动图。'],
    idleLines: ['（动图样本在眨眼。）'],
  });
  gifChar.images.head3 = gifDataUrl(makeBlinkGif());
  gifChar.blinkMs = 1200;
  const gifPayload = {
    format: 'bug-clicker',
    version: 1,
    app: APP,
    exportedAt: STAMP,
    characters: [gifChar],
  };
  const ownerLocked = ownerFor('cleanroom-locked-2026', 'owner-locked');
  const ownerEditlock = ownerFor('cleanroom-editlock-2026', 'owner-editlock');
  const ownerCombined = ownerFor('cleanroom-combined-2026', 'owner-combined');
  const ownerReexport = ownerFor('cleanroom-reexport-2026', 'owner-reexport');
  const lockedChar = protectedCharacter({
    id: 'sample-locked',
    name: '净室样本·不可再导出',
    lines: ['这个角色不能再导出。', '但仍可正常使用。'],
    idleLines: ['（不可再导出样本在发呆。）'],
  });
  const lockedOwnerChar = protectedCharacter({
    id: 'sample-locked-owner',
    name: '净室样本·口令恢复',
    lines: ['这个角色带作者口令。', '输入正确口令后完全解锁。'],
    idleLines: ['（口令样本在发呆。）'],
  });
  const editlockChar = protectedCharacter({
    id: 'sample-editlock',
    name: '净室样本·锁定原文',
    lines: protectedFullLines().lines,
    idleLines: protectedFullLines().idleLines,
    editLock: true,
    editBase: protectedBaselines(),
    editSty: protectedFullStyles(),
  });
  const recoveryChar = protectedCharacter({
    id: 'sample-editlock-recovery',
    name: '净室样本·基线恢复',
    // The payload only carries the new lines; editBase carries the originals.
    lines: ['新增点击三。'],
    idleLines: ['新增自语二。'],
    editLock: true,
    editBase: protectedBaselines(),
    editSty: protectedStyles(),
  });
  const editlockOwnerChar = protectedCharacter({
    id: 'sample-editlock-owner',
    name: '净室样本·锁定原文·口令',
    lines: protectedFullLines().lines,
    idleLines: protectedFullLines().idleLines,
    editLock: true,
    editBase: protectedBaselines(),
    editSty: protectedFullStyles(),
  });
  const combinedChar = protectedCharacter({
    id: 'sample-combined',
    name: '净室样本·双重保护',
    lines: protectedFullLines().lines,
    idleLines: protectedFullLines().idleLines,
    editLock: true,
    editBase: protectedBaselines(),
    editSty: protectedFullStyles(),
  });
  const lockedPayload = {
    format: 'bug-clicker', version: 1, app: APP, exportedAt: STAMP, characters: [lockedChar],
  };
  const lockedOwnerPayload = {
    format: 'bug-clicker', version: 1, app: APP, exportedAt: STAMP, characters: [lockedOwnerChar],
  };
  const editlockPayload = {
    format: 'bug-clicker', version: 1, app: APP, exportedAt: STAMP, characters: [editlockChar],
  };
  const recoveryPayload = {
    format: 'bug-clicker', version: 1, app: APP, exportedAt: STAMP, characters: [recoveryChar],
  };
  const editlockOwnerPayload = {
    format: 'bug-clicker', version: 1, app: APP, exportedAt: STAMP, characters: [editlockOwnerChar],
  };
  const combinedPayload = {
    format: 'bug-clicker', version: 1, app: APP, exportedAt: STAMP, characters: [combinedChar],
  };
  const lockedPartPayload = headtopPartPackage();
  const headtop = headtopPartPackage();
  const rig = rigPartPackage();
  const legacy = {
    format: 'carnovella-clicker',
    version: 1,
    exportedAt: STAMP,
    characters: [legacyCharacter('Carnovella 旧角色', 'orange', false)],
  };
  const legacyDefaults = {
    format: 'carnovella-clicker',
    characters: [legacyCharacter('Legacy Defaults', 'lime', true)],
  };

  const lineCanonical = [
    '# 净室样本台词文件',
    '# 这是注释行',
    '// 这也是注释行',
    '',
    '[누를 때]',
    '你好，我是净室样本。',
    '{외침} 不要戳我！',
    '这一行有\\n换行。',
    '逗号,不是列,会原样保留。',
    '',
    '[혼잣말]',
    '（发呆）……',
    '{unknown} 这一行保留花括号。',
    '{메모} 这也是普通台词。',
    '',
  ].join('\n');
  const lineStylesFile = [
    '# 样式前缀边界',
    '[누를 때]',
    '{외침} 内置大喊',
    '{외침}',
    '{unknown} 未知样式',
    '{메모} 备忘词不是样式',
    '{123} 数字不是样式',
    '普通台词',
    '',
  ].join('\n');
  const lineUnsectioned = ['第一句', '第二句', '第三句', ''].join('\n');

  const files = [];
  const expected = [];

  function addBinary(rel, buf, expectedObj, extra = {}) {
    files.push({ rel, buf });
    expected.push({
      sample: rel,
      sha256: sha256(buf),
      ...expectedObj,
      ...extra,
    });
  }

  function addText(rel, text, expectedObj) {
    const buf = Buffer.from(text, 'utf8');
    files.push({ rel, buf });
    expected.push({
      sample: rel,
      sha256: sha256(buf),
      ...expectedObj,
    });
  }

  addBinary('cvclk/plain-minimal.cvclk', packContainer(minimalPayload, { seed: 'plain-minimal' }), {
    kind: 'cvclk',
    container: { format: 'bug-clicker', version: 2, locked: false },
    payload: { format: 'bug-clicker', version: 1, app: APP, characterCount: 1 },
    characters: minimalPayload.characters.map(summarizeCharacter),
  });
  addBinary('cvclk/plain-full.cvclk', packContainer(fullPayload, { seed: 'plain-full' }), {
    kind: 'cvclk',
    container: { format: 'bug-clicker', version: 2, locked: false },
    payload: { format: 'bug-clicker', version: 1, app: APP, characterCount: 1 },
    characters: fullPayload.characters.map(summarizeCharacter),
  });
  addBinary('cvclk/plain-multi.cvclk', packContainer(multiPayload, { seed: 'plain-multi' }), {
    kind: 'cvclk',
    container: { format: 'bug-clicker', version: 2, locked: false },
    payload: { format: 'bug-clicker', version: 1, app: APP, characterCount: 3 },
    characters: multiPayload.characters.map(summarizeCharacter),
  });
  addBinary('cvclk/unknown-fields.cvclk', packContainer(unknownPayload, { seed: 'unknown-fields' }), {
    kind: 'cvclk',
    container: { format: 'bug-clicker', version: 2, locked: false },
    payload: { format: 'bug-clicker', version: 1, app: APP, characterCount: 1 },
    payloadExtra: unknownPayload.x_sample,
    characters: unknownPayload.characters.map(summarizeCharacter),
  });
  addBinary('cvclk/animated-gif.cvclk', packContainer(gifPayload, { seed: 'animated-gif' }), {
    kind: 'cvclk',
    container: { format: 'bug-clicker', version: 2, locked: false },
    payload: { format: 'bug-clicker', version: 1, app: APP, characterCount: 1 },
    characters: gifPayload.characters.map(summarizeCharacter),
  });
  addBinary('protected/locked-no-owner.cvclk', packContainer(lockedPayload, { seed: 'protected-locked-no-owner', locked: true }), {
    kind: 'cvclk',
    container: { format: 'bug-clicker', version: 2, locked: true },
    payload: { format: 'bug-clicker', version: 1, app: APP, characterCount: 1 },
    characters: lockedPayload.characters.map(summarizeCharacter),
  });
  addBinary('protected/locked-owner.cvclk', packContainer(lockedOwnerPayload, { seed: 'protected-locked-owner', locked: true, owner: ownerLocked }), {
    kind: 'cvclk',
    container: { format: 'bug-clicker', version: 2, locked: true, owner: ownerLocked },
    ownerCode: 'cleanroom-locked-2026',
    payload: { format: 'bug-clicker', version: 1, app: APP, characterCount: 1 },
    characters: lockedOwnerPayload.characters.map(summarizeCharacter),
  });
  addBinary('protected/editlock.cvclk', packContainer(editlockPayload, { seed: 'protected-editlock', locked: false }), {
    kind: 'cvclk',
    container: { format: 'bug-clicker', version: 2, locked: false },
    payload: { format: 'bug-clicker', version: 1, app: APP, characterCount: 1 },
    characters: editlockPayload.characters.map(summarizeCharacter),
  });
  addBinary('protected/editlock-recovery.cvclk', packContainer(recoveryPayload, { seed: 'protected-editlock-recovery', locked: false }), {
    kind: 'cvclk',
    container: { format: 'bug-clicker', version: 2, locked: false },
    payload: { format: 'bug-clicker', version: 1, app: APP, characterCount: 1 },
    characters: recoveryPayload.characters.map(summarizeCharacter),
  });
  addBinary('protected/editlock-owner-reexport.cvclk', packContainer(editlockOwnerPayload, { seed: 'protected-editlock-owner-reexport', locked: false, owner: ownerReexport }), {
    kind: 'cvclk',
    container: { format: 'bug-clicker', version: 2, locked: false, owner: ownerReexport },
    ownerCode: 'cleanroom-reexport-2026',
    payload: { format: 'bug-clicker', version: 1, app: APP, characterCount: 1 },
    characters: editlockOwnerPayload.characters.map(summarizeCharacter),
  });
  addBinary('protected/locked-owner-editlock.cvclk', packContainer(combinedPayload, { seed: 'protected-locked-owner-editlock', locked: true, owner: ownerCombined }), {
    kind: 'cvclk',
    container: { format: 'bug-clicker', version: 2, locked: true, owner: ownerCombined },
    ownerCode: 'cleanroom-combined-2026',
    payload: { format: 'bug-clicker', version: 1, app: APP, characterCount: 1 },
    characters: combinedPayload.characters.map(summarizeCharacter),
  });
  addBinary('protected/locked-part.cvpart', packContainer(lockedPartPayload, { kind: 'part', seed: 'protected-locked-part', locked: true }), {
    kind: 'cvpart',
    container: { format: 'bug-clicker-part', version: 1, locked: true },
    payload: { format: 'bug-clicker-part', version: 1, from: lockedPartPayload.from, partCount: lockedPartPayload.parts.length },
    parts: lockedPartPayload.parts.map(summarizePart),
    rigCommon: lockedPartPayload.rigCommon,
  });
  addBinary('cvpart/headtop.cvpart', packContainer(headtop, { kind: 'part', seed: 'headtop' }), {
    kind: 'cvpart',
    container: { format: 'bug-clicker-part', version: 1, locked: false },
    payload: {
      format: 'bug-clicker-part',
      version: 1,
      from: headtop.from,
      partCount: headtop.parts.length,
    },
    parts: headtop.parts.map(summarizePart),
    rigCommon: headtop.rigCommon,
  });
  addBinary('cvpart/rig.cvpart', packContainer(rig, { kind: 'part', seed: 'rig' }), {
    kind: 'cvpart',
    container: { format: 'bug-clicker-part', version: 1, locked: false },
    payload: {
      format: 'bug-clicker-part',
      version: 1,
      from: rig.from,
      partCount: rig.parts.length,
    },
    parts: rig.parts.map(summarizePart),
    rigCommon: rig.rigCommon,
  });

  // The canonical line text is used by all three extensions to demonstrate
  // that the original parser only looks at the text, not the extension.
  addText('lines/click-idle.txt', '﻿' + lineCanonical.replace(/\n/g, '\r\n'), {
    kind: 'lines',
    ...lineSummary(lineCanonical),
  });
  addText('lines/click-idle.csv', lineCanonical, {
    kind: 'lines',
    ...lineSummary(lineCanonical),
  });
  addText('lines/click-idle.md', lineCanonical, {
    kind: 'lines',
    ...lineSummary(lineCanonical),
  });
  addText('lines/unsectioned.txt', lineUnsectioned, {
    kind: 'lines',
    ...lineSummary(lineUnsectioned),
  });
  addText('lines/styles.txt', '﻿' + lineStylesFile.replace(/\n/g, '\r\n'), {
    kind: 'lines',
    ...lineSummary(lineStylesFile),
  });

  addText('legacy/carnovella-clicker.json', JSON.stringify(legacy, null, 2) + '\n', {
    kind: 'legacy',
    format: 'carnovella-clicker',
    characterCount: 1,
    characters: legacy.characters.map(summarizeCharacter),
  });
  addText('legacy/carnovella-clicker-defaults.json', JSON.stringify(legacyDefaults, null, 2) + '\n', {
    kind: 'legacy',
    format: 'carnovella-clicker',
    characterCount: 1,
    characters: legacyDefaults.characters.map(summarizeCharacter),
  });

  return { files, expected };
}

function summarizeCharacter(c) {
  return {
    id: c.id ?? null,
    name: c.name ?? null,
    author: c.author ?? null,
    images: Object.fromEntries(
      Object.entries(c.images || {}).map(([k, v]) => [k, dataUrlSummary(v)]),
    ),
    imageSlots: Object.keys(c.images || {}),
    lines: c.lines || [],
    idleLines: c.idleLines || [],
    lineStyles: c.lineStyles || null,
    editLock: c.editLock ?? null,
    editBase: c.editBase ?? null,
    editSty: c.editSty ?? null,
    specialLineCounts: Object.fromEntries(
      Object.keys(c)
        .filter((k) => /^sp_.*_(before|after)$/.test(k))
        .map((k) => [k, Array.isArray(c[k]) ? c[k].length : 0]),
    ),
    partSlots: Array.isArray(c.parts) ? c.parts.map((p) => p.slot) : [],
    bubbleStyle: c.bubbleStyle
      ? { preset: c.bubbleStyle.preset, shape: c.bubbleStyle.shape, appear: c.bubbleStyle.appear, textFx: c.bubbleStyle.textFx }
      : null,
    bubbleStyleExtra: c.bubbleStyle
      ? Object.fromEntries(Object.entries(c.bubbleStyle).filter(([k]) => k.startsWith('x_')))
      : null,
    extraFields: Object.fromEntries(
      Object.entries(c).filter(([k]) => k.startsWith('x_') || k === 'groupNames' || /^sp_g\d+_/.test(k)),
    ),
    sound: c.sound === undefined ? null : typeof c.sound === 'string' ? dataUrlSummary(c.sound) : c.sound,
    lineSoundFiles: Object.fromEntries(
      Object.entries(c.lineSoundFiles || {}).map(([k, v]) => [k, dataUrlSummary(v)]),
    ),
    lineSounds: c.lineSounds || null,
    partOrder: c.partOrder || null,
    layers: c.layers || null,
    rigCommon: c.rigCommon || null,
    petKinds: c.petKinds || null,
    petImages: Object.fromEntries(
      Object.entries(c.petImages || {}).map(([k, v]) => [k, dataUrlSummary(v)]),
    ),
    bubbleImages: Object.fromEntries(
      Object.entries(c.bubbleImages || {}).map(([k, v]) => [k, dataUrlSummary(v)]),
    ),
  };
}

function summarizePart(p) {
  return {
    slot: p.slot,
    name: p.name ?? '',
    rigId: p.rigId ?? null,
    on: p.on ?? null,
    at: p.at ?? null,
    settings: p.settings ?? null,
    image: dataUrlSummary(p.image),
    shadowImage: p.shadowImage ? dataUrlSummary(p.shadowImage) : null,
    blinkImage: p.blinkImage ? dataUrlSummary(p.blinkImage) : null,
  };
}

// ---------------------------------------------------------------------------
// Generate / verify / inspect
// ---------------------------------------------------------------------------
function writeFile(rel, buf) {
  const full = path.join(SAMPLES, rel);
  fs.mkdirSync(path.dirname(full), { recursive: true });
  fs.writeFileSync(full, buf);
}

function allSampleFiles() {
  const out = [];
  function walk(dir, rel) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const r = path.posix.join(rel, entry.name);
      // Analysis-side evidence lives outside the public repo and is not a sample.
      if (r === 'evidence' || r.startsWith('evidence/')) continue;
      if (entry.isDirectory()) walk(path.join(dir, entry.name), r);
      else out.push(r);
    }
  }
  if (fs.existsSync(SAMPLES)) walk(SAMPLES, '');
  return out
    .filter((r) => !['README.md', 'VERIFICATION.md', 'PROVENANCE.md', 'SHA256SUMS', 'SHA256SUMS.handoff'].includes(path.posix.basename(r)))
    .sort();
}

const HANDOFF_PREFIXES = ['cvclk/', 'cvpart/', 'lines/', 'legacy/', 'expected/', 'protected/'];
function handoffFiles() {
  return allSampleFiles().filter((rel) => HANDOFF_PREFIXES.some((p) => rel.startsWith(p)));
}

function generate() {
  const { files, expected } = buildSamples();
  const expectedPaths = new Set(expected.map((exp) => 'expected/' + exp.sample + '.expected.json'));
  for (const rel of allSampleFiles()) {
    if (rel.startsWith('expected/') && !expectedPaths.has(rel)) {
      fs.rmSync(path.join(SAMPLES, rel), { force: true });
    }
  }
  for (const { rel, buf } of files) writeFile(rel, buf);
  for (const exp of expected) {
    const rel = 'expected/' + exp.sample + '.expected.json';
    writeFile(rel, Buffer.from(JSON.stringify(exp, null, 2) + '\n', 'utf8'));
  }
  const lines = allSampleFiles().map((rel) => {
    const buf = fs.readFileSync(path.join(SAMPLES, rel));
    return `${sha256(buf)}  ${rel}`;
  });
  writeFile('SHA256SUMS', Buffer.from(lines.join('\n') + '\n', 'utf8'));
  const handoff = handoffFiles().map((rel) => {
    const buf = fs.readFileSync(path.join(SAMPLES, rel));
    return `${sha256(buf)}  ${rel}`;
  });
  writeFile('SHA256SUMS.handoff', Buffer.from(handoff.join('\n') + '\n', 'utf8'));
  console.log(`Generated ${files.length} samples and ${expected.length} expected summaries.`);
  console.log(`Wrote ${path.join(SAMPLES, 'SHA256SUMS')} and SHA256SUMS.handoff.`);
}

function verify() {
  const { files, expected } = buildSamples();
  let failures = 0;
  const fail = (msg) => {
    failures++;
    console.error('FAIL:', msg);
  };
  // 1. Regenerate in memory and compare bytes.
  for (const { rel, buf } of files) {
    const full = path.join(SAMPLES, rel);
    if (!fs.existsSync(full)) {
      fail(`${rel} missing`);
      continue;
    }
    const disk = fs.readFileSync(full);
    if (!disk.equals(buf)) fail(`${rel} bytes differ from generator`);
  }
  // 2. Compare expected summaries.
  for (const exp of expected) {
    const rel = 'expected/' + exp.sample + '.expected.json';
    const full = path.join(SAMPLES, rel);
    if (!fs.existsSync(full)) {
      fail(`${rel} missing`);
      continue;
    }
    const disk = JSON.parse(fs.readFileSync(full, 'utf8'));
    if (JSON.stringify(disk) !== JSON.stringify(exp)) fail(`${rel} summary differs`);
  }
  // 3. SHA256SUMS.
  const sumsPath = path.join(SAMPLES, 'SHA256SUMS');
  if (!fs.existsSync(sumsPath)) fail('SHA256SUMS missing');
  else {
    const sums = new Map();
    for (const line of fs.readFileSync(sumsPath, 'utf8').split(/\r?\n/)) {
      if (!line.trim()) continue;
      const m = /^([0-9a-f]{64})  (.+)$/.exec(line);
      if (!m) {
        fail('bad SHA256SUMS line: ' + line);
        continue;
      }
      sums.set(m[2], m[1]);
    }
    for (const rel of allSampleFiles()) {
      const want = sums.get(rel);
      if (!want) {
        fail(`${rel} not in SHA256SUMS`);
        continue;
      }
      const got = sha256(fs.readFileSync(path.join(SAMPLES, rel)));
      if (got !== want) fail(`${rel} SHA-256 mismatch`);
    }
    for (const rel of sums.keys()) {
      if (!fs.existsSync(path.join(SAMPLES, rel))) fail(`${rel} listed but missing`);
    }
  }
  // 4. Structural checks on containers.
  for (const { rel, buf } of files) {
    if (rel.endsWith('.cvclk') || rel.endsWith('.cvpart')) {
      try {
        const u = unpackContainer(buf);
        if (rel.endsWith('.cvclk')) {
          if (u.header.format !== 'bug-clicker' || u.header.version !== 2) fail(`${rel} bad header`);
          if (u.payload.format !== 'bug-clicker' || u.payload.version !== 1) fail(`${rel} bad payload`);
          if (!Array.isArray(u.payload.characters) || !u.payload.characters.length) fail(`${rel} no characters`);
        } else {
          if (u.header.format !== 'bug-clicker-part' || u.header.version !== 1) fail(`${rel} bad header`);
          if (u.payload.format !== 'bug-clicker-part' || u.payload.version !== 1) fail(`${rel} bad payload`);
          if (!Array.isArray(u.payload.parts) || u.payload.parts.length < 1 || u.payload.parts.length > 16) fail(`${rel} bad parts`);
        }
      } catch (e) {
        fail(`${rel} unpack: ${e.message}`);
      }
    }
  }
  // 5. Handoff manifest: only self-made samples + expected summaries.
  const handoffPath = path.join(SAMPLES, 'SHA256SUMS.handoff');
  if (!fs.existsSync(handoffPath)) fail('SHA256SUMS.handoff missing');
  else {
    const handoffSums = new Map();
    for (const line of fs.readFileSync(handoffPath, 'utf8').split(/\r?\n/)) {
      if (!line.trim()) continue;
      const m = /^([0-9a-f]{64})  (.+)$/.exec(line);
      if (!m) {
        fail('bad SHA256SUMS.handoff line: ' + line);
        continue;
      }
      handoffSums.set(m[2], m[1]);
      if (m[2].startsWith('verification/')) fail(`handoff manifest contains analysis-only path ${m[2]}`);
    }
    for (const rel of handoffFiles()) {
      const want = handoffSums.get(rel);
      if (!want) {
        fail(`${rel} not in SHA256SUMS.handoff`);
        continue;
      }
      const got = sha256(fs.readFileSync(path.join(SAMPLES, rel)));
      if (got !== want) fail(`${rel} handoff SHA-256 mismatch`);
    }
    if (handoffSums.size !== handoffFiles().length) fail('SHA256SUMS.handoff has extra or missing entries');
  }
  // 6. Original re-export fixtures must be readable containers.
  for (const rel of allSampleFiles()) {
    if (!rel.startsWith('verification/')) continue;
    if (!/\.(cvclk|cvpart)$/.test(rel)) continue;
    try {
      const u = unpackContainer(fs.readFileSync(path.join(SAMPLES, rel)));
      if (rel.endsWith('.cvclk')) {
        if (u.header.format !== 'bug-clicker' || u.payload.format !== 'bug-clicker') fail(`${rel} bad cvclk`);
      } else if (u.header.format !== 'bug-clicker-part' || u.payload.format !== 'bug-clicker-part') {
        fail(`${rel} bad cvpart`);
      }
    } catch (e) {
      fail(`${rel} unpack: ${e.message}`);
    }
  }
  // 7. Container tamper checks.
  const tamperBase = fs.readFileSync(path.join(SAMPLES, 'cvclk/plain-minimal.cvclk'));
  const tamperCases = [
    ['header', (b) => { b[12] ^= 0x01; }],
    ['salt', (b) => { b[12 + b.readUInt32BE(8) + 1] ^= 0x01; }],
    ['ciphertext', (b) => { b[b.length - 1] ^= 0x01; }],
    ['truncated', (b) => { b = b.subarray(0, b.length - 1); return b; }],
  ];
  for (const [name, mutate] of tamperCases) {
    const copy = Buffer.from(tamperBase);
    const mutated = mutate(copy) || copy;
    let threw = false;
    try {
      unpackContainer(mutated);
    } catch {
      threw = true;
    }
    if (!threw) fail(`container tamper (${name}) was accepted`);
  }
  if (failures) {
    console.error(`${failures} failure(s).`);
    process.exitCode = 1;
    return;
  }
  console.log(`OK: ${files.length} samples, ${expected.length} expected summaries, SHA256SUMS verified.`);
}

function summarizeJsonValue(v) {
  if (typeof v === 'string' && v.startsWith('data:')) return dataUrlSummary(v);
  if (Array.isArray(v)) return v.map(summarizeJsonValue);
  if (v && typeof v === 'object') {
    const out = {};
    for (const [k, x] of Object.entries(v)) out[k] = summarizeJsonValue(x);
    return out;
  }
  return v;
}

function inspect(file) {
  const full = path.resolve(file);
  const buf = fs.readFileSync(full);
  let obj;
  if (buf.subarray(0, 8).equals(KINDS.clk.magic) || buf.subarray(0, 8).equals(KINDS.part.magic)) {
    const u = unpackContainer(buf);
    obj = { kind: u.kind, header: u.header, payload: summarizeJsonValue(u.payload) };
  } else {
    const text = buf.toString('utf8').replace(/^﻿/, '');
    try {
      obj = JSON.parse(text);
    } catch {
      obj = lineSummary(text);
    }
  }
  console.log(JSON.stringify(obj, null, 2));
}

// ---------------------------------------------------------------------------
// CLI
// ---------------------------------------------------------------------------
const [, , cmd = 'generate', ...args] = process.argv;
if (cmd === 'generate') generate();
else if (cmd === 'verify') verify();
else if (cmd === 'inspect') {
  if (!args[0]) {
    console.error('usage: node tools/cleanroom/samples.mjs inspect <file>');
    process.exit(2);
  }
  inspect(args[0]);
} else {
  console.error('usage: node tools/cleanroom/samples.mjs [generate|verify|inspect <file>]');
  process.exit(2);
}
