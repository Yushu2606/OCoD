# OCoD

[English](README.md) | [简体中文](README.zh-Hans.md) | [日本語](README.ja.md) | 한국어

OCoD(Original Character on Desktop)는 Bug Clicker를 동작 참조로 삼는 클린룸 방식의 데스크톱 캐릭터 프로젝트입니다.
설계 기준선은 [docs/design-baseline.md](docs/design-baseline.md), 용어는 [GLOSSARY.md](GLOSSARY.md)에 있습니다.

이 저장소는 분석 측이 제공하는 사양과 고정 테스트 샘플만 받으며, 원본 코드, 언패킹된 원본 내용, 원본 에셋을 포함하지 않습니다.

## 저장소 구조

| 경로 | 내용 |
| --- | --- |
| `src/OCoD.Launcher` | 개발용 런처: 전역 데몬 시작, 토폴로지 관리, 토폴로지 스모크 테스트 |
| `src/OCoD.GlobalDaemon` | 사용자별 전역 데몬: 트레이/메뉴 막대, 관리 창, 컴포넌트 감독 |
| `src/OCoD.CharacterHost` | 캐릭터 런타임: 투명 자리 표시 창, 창 메뉴, 제어 채널 |
| `src/OCoD.LibraryDaemon` | 캐릭터 라이브러리마다 하나씩 있는 라이브러리 데몬, 라이브러리의 유일한 기록자 |
| `src/OCoD.LibraryClient` | 라이브러리 클라이언트와 라이브러리 데몬 감독자 |
| `src/OCoD.Formats` | `.cvclk` 컨테이너와 일반 캐릭터 페이로드 파싱 |
| `src/OCoD.Protocol` | IPC 계약, 경로 해석, 자식 프로세스와 단일 인스턴스 기반 |
| `src/OCoD.Desktop` | 캐릭터 런타임과 전역 데몬이 공유하는 Avalonia 리소스 |
| `src/OCoD.Localization` | 4개 언어 리소스 진입점: 중국어 간체, 영어, 일본어, 한국어 및 중국어 지역 폴백 |
| `tests/` | 현지화, IPC 계약, `.cvclk` 컨테이너/가져오기 테스트 |
| `docs/adr` | 첫 릴리스 설계 결정 |

## 현재 구현 상태

티켓 #4(데스크톱 캐릭터 셸), #5(프로세스 토폴로지, 라이브러리 데몬과 관리 창), #6(일반 캐릭터 패키지 가져오기와 영속화)이 구현되었습니다:

- 하나의 솔루션이 Windows와 macOS의 Native AOT 게시 대상을 제공합니다. 대상 머신에 .NET 런타임이 필요 없습니다.
- 투명 테두리 없는 캐릭터 창으로 드래그, 가장자리 크기 조절, 좌우 반전, 위치/크기 잠금, 항상 위, 마우스 통과를 지원합니다.
- 일반 `.cvclk` 컨테이너를 복호화하고 gzip 해제한 뒤 캐릭터 필드와 이미지를 파싱합니다. 여러 캐릭터 패키지는 하나씩 가져오며, 실패한 캐릭터는 다른 캐릭터에 영향을 주지 않습니다.
- 캐릭터는 로컬 ID로 라이브러리에 저장됩니다. 관리 창에서 가져오기, 목록, 시작, 중지를 할 수 있고, 가져온 캐릭터는 데스크톱에 자동으로 표시됩니다.
- 크기, 위치, 반전, 항상 위, 잠금은 캐릭터별로 저장되어 재시작 후 복원됩니다. 이미 실행 중인 캐릭터를 다시 시작하면 기존 창에 포커스만 줍니다.
- 각 캐릭터 라이브러리에는 라이브러리 데몬이 하나만 있으며 모든 쓰기를 일괄 커밋합니다. 캐릭터 런타임과 관리 창은 IPC로 라이브러리에 접근합니다.
- 마지막 클라이언트가 연결을 끊고 대기 중인 쓰기가 끝나면 라이브러리 데몬은 스스로 종료합니다. 캐릭터 런타임은 관리 창에서 개별적으로 중지, 시작, 재시작할 수 있습니다.
- 전역 데몬은 macOS 메뉴 막대/Windows 알림 영역 아이콘과 라이브러리 관리 창을 제공합니다. 빈 라이브러리도 사용할 수 있고, 각 프로세스의 PID, 버전, 상태, 라이브러리 클라이언트를 볼 수 있습니다.
- 하나의 전역 데몬이 여러 캐릭터 라이브러리를 동시에 처리합니다. 라이브러리마다 별도의 라이브러리 데몬, 런타임, 관리 창이 있고, 트레이 하위 메뉴에서 라이브러리별로 열 수 있습니다. 한 라이브러리를 닫아도 다른 라이브러리에 영향을 주지 않습니다.
- 일반 `.cvclk` 가져오기는 보호 표시(`header.locked`, `header.owner`, 페이로드 `editLock`)가 하나라도 있으면 파일 전체를 거부합니다. 보호 패키지의 완전한 지원은 이후 티켓입니다.
- 중국어 간체, 영어, 일본어, 한국어 리소스 진입점. 중국어의 모든 지역은 중국어 간체로 폴백하며, 전역 데몬이 UI 언어 변경을 캐릭터 런타임에 전달합니다.
- 보호 패키지, 구버전 JSON, 파츠 패키지/대사 파일, 클릭·대기 애니메이션과 대사, 재내보내기, 설치/업데이트, 편집기는 이후 티켓입니다.

## 빠른 시작

```bash
dotnet test

dotnet run --project src/OCoD.Launcher -- --open   # 토폴로지를 시작하고 관리 창 열기
dotnet run --project src/OCoD.Launcher -- --status
dotnet run --project src/OCoD.Launcher -- --stop

dotnet run --project src/OCoD.Launcher -- --import samples/cvclk/plain-minimal.cvclk
dotnet run --project src/OCoD.Launcher -- --characters

dotnet run --project src/OCoD.Launcher -- --smoke-test
```

캐릭터 창만 따로 확인하려면:

```bash
dotnet run --project src/OCoD.CharacterHost -- --smoke-test --lang ja
```

Native AOT 게시에는 대상 플랫폼의 네이티브 도구 체인이 필요하며 플랫폼 간 교차 게시는 할 수 없습니다.
네 컴포넌트를 같은 디렉터리에 게시합니다:

```bash
for p in OCoD.Launcher OCoD.LibraryDaemon OCoD.CharacterHost OCoD.GlobalDaemon; do
  dotnet publish src/$p/$p.csproj -c Release -r osx-arm64 -o artifacts/osx-arm64
done

./artifacts/osx-arm64/OCoD.Launcher --smoke-test
```

전체 명령, 사전 조건, 인수 절차는 [docs/build-and-run.md](docs/build-and-run.md), 프로세스 토폴로지와 데이터 디렉터리는
[docs/process-topology.md](docs/process-topology.md), 캐릭터 가져오기와 라이브러리 배치는
[docs/character-library.md](docs/character-library.md), 현지화 규칙은 [docs/localization.md](docs/localization.md)에 있습니다.
