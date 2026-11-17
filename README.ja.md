# OCoD

[English](README.md) | [简体中文](README.zh-Hans.md) | 日本語 | [한국어](README.ko.md)

OCoD（Original Character on Desktop）は、Bug Clicker を挙動の参照元とするクリーンルーム方式のデスクトップキャラクタープロジェクトです。
設計ベースラインは [docs/design-baseline.md](docs/design-baseline.md)、用語集は [GLOSSARY.md](GLOSSARY.md) にあります。

本リポジトリは分析側が提供する仕様と固定テストサンプルのみを受け取り、原作のコード、アンパックされた原作内容、原作素材は含みません。

## リポジトリ構成

| パス | 内容 |
| --- | --- |
| `src/OCoD.Launcher` | 開発用ランチャー：グローバルデーモンの起動、トポロジ管理、トポロジのスモークテスト |
| `src/OCoD.GlobalDaemon` | ユーザーごとのグローバルデーモン：トレイ/メニューバー、管理ウィンドウ、コンポーネント監視 |
| `src/OCoD.CharacterHost` | キャラクターランタイム：透明なプレースホルダーウィンドウ、ウィンドウメニュー、制御チャネル |
| `src/OCoD.LibraryDaemon` | キャラクターライブラリごとに 1 つのライブラリデーモン。ライブラリへの唯一の書き込み元 |
| `src/OCoD.LibraryClient` | ライブラリクライアントとライブラリデーモンの監視 |
| `src/OCoD.Formats` | `.cvclk` コンテナと通常キャラクターのペイロード解析 |
| `src/OCoD.Protocol` | IPC 契約、パス解決、子プロセスと単一インスタンスの基盤 |
| `src/OCoD.Desktop` | キャラクターランタイムとグローバルデーモンが共有する Avalonia リソース |
| `src/OCoD.Localization` | 4 言語のリソース入口：簡体字中国語、英語、日本語、韓国語、および中国語地域のフォールバック |
| `tests/` | ローカライズ、IPC 契約、`.cvclk` コンテナ/インポートのテスト |
| `docs/adr` | 初版の設計判断 |

## 実装状況

チケット #4（デスクトップキャラクターシェル）、#5（プロセストポロジ、ライブラリデーモンと管理ウィンドウ）、#6（通常キャラクターパッケージのインポートと永続化）は実装済みです：

- 1 つのソリューションで Windows と macOS の Native AOT 発行ターゲットを提供します。対象マシンに .NET ランタイムは不要です。
- 透明で枠なしのキャラクターウィンドウ。ドラッグ、端でのリサイズ、左右反転、位置とサイズのロック、最前面固定、マウスクリックスルーに対応します。
- 通常の `.cvclk` コンテナを復号し、gzip 展開してキャラクターのフィールドと画像を解析します。複数キャラクターのパッケージは 1 体ずつインポートされ、失敗したキャラクターは他に影響しません。
- キャラクターはローカル ID でライブラリに保存されます。管理ウィンドウからインポート、一覧表示、起動、停止ができ、インポート後は自動的にデスクトップに表示されます。
- サイズ、位置、反転、最前面固定、ロックはキャラクターごとに保存され、再起動後に復元されます。同じキャラクターを再度起動した場合は既存のウィンドウにフォーカスするだけです。
- 各キャラクターライブラリにはライブラリデーモンが 1 つだけ存在し、すべての書き込みを一括してコミットします。キャラクターランタイムと管理ウィンドウは IPC でライブラリにアクセスします。
- 最後のクライアントが切断し、保留中の書き込みが完了すると、ライブラリデーモンは自動的に終了します。キャラクターランタイムは管理ウィンドウから個別に停止、起動、再起動できます。
- グローバルデーモンは macOS のメニューバー / Windows の通知領域アイコンとライブラリ管理ウィンドウを提供します。空のライブラリも扱え、各プロセスの PID、バージョン、状態、ライブラリクライアントを確認できます。
- 1 つのグローバルデーモンが複数のキャラクターライブラリを並行して扱えます。ライブラリごとに専用のライブラリデーモン、ランタイム、管理ウィンドウを持ち、トレイのサブメニューからライブラリごとに開けます。1 つのライブラリを閉じても他に影響しません。
- 通常の `.cvclk` インポートは、保護マーカー（`header.locked`、`header.owner`、ペイロードの `editLock`）が 1 つでもあればファイル全体を拒否します。保護パッケージの完全な対応は今後のチケットです。
- 簡体字中国語、英語、日本語、韓国語のリソース入口。中国語の任意の地域は簡体字中国語にフォールバックし、グローバルデーモンが UI 言語の変更をキャラクターランタイムへ配信します。
- 保護パッケージ、旧版 JSON、パーツパッケージ/セリフファイル、クリック時・アイドル時のアニメーションとセリフ、再エクスポート、インストール/更新、エディターは今後のチケットです。

## クイックスタート

```bash
dotnet test

dotnet run --project src/OCoD.Launcher -- --open   # トポロジを起動し管理ウィンドウを開く
dotnet run --project src/OCoD.Launcher -- --status
dotnet run --project src/OCoD.Launcher -- --stop

dotnet run --project src/OCoD.Launcher -- --import samples/cvclk/plain-minimal.cvclk
dotnet run --project src/OCoD.Launcher -- --characters

dotnet run --project src/OCoD.Launcher -- --smoke-test
```

キャラクターウィンドウだけを確認する場合：

```bash
dotnet run --project src/OCoD.CharacterHost -- --smoke-test --lang ja
```

Native AOT の発行には対象プラットフォームのネイティブツールチェーンが必要で、プラットフォーム間のクロス発行はできません。
4 つのコンポーネントを同じディレクトリへ発行します：

```bash
for p in OCoD.Launcher OCoD.LibraryDaemon OCoD.CharacterHost OCoD.GlobalDaemon; do
  dotnet publish src/$p/$p.csproj -c Release -r osx-arm64 -o artifacts/osx-arm64
done

./artifacts/osx-arm64/OCoD.Launcher --smoke-test
```

完全なコマンド、前提条件、受け入れ手順は [docs/build-and-run.md](docs/build-and-run.md)、プロセストポロジとデータディレクトリは
[docs/process-topology.md](docs/process-topology.md)、キャラクターのインポートとライブラリ配置は
[docs/character-library.md](docs/character-library.md)、ローカライズ規約は [docs/localization.md](docs/localization.md) にあります。
