# OCoD

English | [简体中文](README.zh-Hans.md) | [日本語](README.ja.md) | [한국어](README.ko.md)

OCoD (Original Character on Desktop) is a clean-room desktop character project that uses Bug Clicker as its behavioral reference.
The design baseline lives in [docs/design-baseline.md](docs/design-baseline.md); domain terms are in [GLOSSARY.md](GLOSSARY.md).

This repository only receives specifications and fixed test samples from the analysis side. It contains no original
code, no unpacked original content and no original assets.

## Repository layout

| Path | Contents |
| --- | --- |
| `src/OCoD.Launcher` | Development launcher: starts the global daemon, manages topology, runs topology smoke tests |
| `src/OCoD.GlobalDaemon` | Per-user global daemon: tray/menu bar, management window, component supervision |
| `src/OCoD.CharacterHost` | Character runtime: transparent placeholder window, window menu, control channel |
| `src/OCoD.LibraryDaemon` | One library daemon per character library; the only writer to a library |
| `src/OCoD.LibraryClient` | Library client and library-daemon supervisor |
| `src/OCoD.Formats` | `.cvclk` container and ordinary character payload parsing |
| `src/OCoD.Protocol` | IPC contracts, path resolution, child-process and single-instance infrastructure |
| `src/OCoD.Desktop` | Avalonia assets shared by the character runtime and the global daemon |
| `src/OCoD.Localization` | Four-language resource entry points: Simplified Chinese, English, Japanese, Korean, with Chinese-region fallback |
| `tests/` | Localization, IPC contract and `.cvclk` container/import tests |
| `docs/adr` | Design decisions for the first release |

## Current status

Tickets #4 (desktop character shell), #5 (process topology, library daemon and management window) and #6 (ordinary
character package import and persistence) are implemented:

- One solution provides Native AOT publish targets for Windows and macOS; target machines need no .NET runtime.
- Transparent borderless character window with drag, edge resize, horizontal flip, position/size lock, always-on-top and click-through.
- Ordinary `.cvclk` containers are decrypted, gzip-decompressed and parsed into character fields and images; multi-character packages import one by one, and a failing character does not affect the others.
- Characters are stored in the library under local IDs; the management window can import, list, start and stop them; imported characters appear on the desktop automatically.
- Size, position, flip, always-on-top and lock are saved per character and restored after restart; launching an already-running character only focuses its existing window.
- Each character library has exactly one library daemon that commits all writes; the character runtime and management window reach the library over IPC.
- When the last client disconnects and pending writes are committed, the library daemon exits by itself; character runtimes can be stopped, started and restarted independently from the management window.
- The global daemon provides a macOS menu bar / Windows notification-area icon and the library management window; empty libraries are supported, and process PIDs, versions, states and library clients are visible.
- One global daemon serves several character libraries side by side: each library keeps its own library daemon, runtime and management window; the tray submenu opens each library, and closing one library does not affect the others.
- Simplified Chinese, English, Japanese and Korean resource entry points; any Chinese region falls back to Simplified Chinese; the global daemon pushes language changes to character runtimes.
- Normal `.cvclk` import rejects every protected marker (`header.locked`, `header.owner`, payload `editLock`) for the whole file; protected-package support remains a later ticket.
- Protected packages, legacy JSON, part packages / line files, idle and click animations, re-export, installation/update and the editor belong to later tickets.

## Quick start

```bash
dotnet test

dotnet run --project src/OCoD.Launcher -- --open   # start the topology and open the management window
dotnet run --project src/OCoD.Launcher -- --status
dotnet run --project src/OCoD.Launcher -- --stop

dotnet run --project src/OCoD.Launcher -- --import samples/cvclk/plain-minimal.cvclk
dotnet run --project src/OCoD.Launcher -- --characters

dotnet run --project src/OCoD.Launcher -- --smoke-test
```

To check the character window on its own:

```bash
dotnet run --project src/OCoD.CharacterHost -- --smoke-test --lang ja
```

Native AOT publishing needs the target platform's native toolchain and cannot cross-publish between platforms.
Publish all four components into the same directory:

```bash
for p in OCoD.Launcher OCoD.LibraryDaemon OCoD.CharacterHost OCoD.GlobalDaemon; do
  dotnet publish src/$p/$p.csproj -c Release -r osx-arm64 -o artifacts/osx-arm64
done

./artifacts/osx-arm64/OCoD.Launcher --smoke-test
```

Full commands, prerequisites and acceptance steps are in [docs/build-and-run.md](docs/build-and-run.md); process
topology and data directories are in [docs/process-topology.md](docs/process-topology.md); character import and
library layout are in [docs/character-library.md](docs/character-library.md); localization conventions are in
[docs/localization.md](docs/localization.md).
