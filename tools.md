# 개발 도구

## 처음 열거나 개발 환경이 바뀌었을 때

프로젝트 루트에서 PowerShell로 [점검 스크립트](scripts/harness.ps1)를 실행한다.

```powershell
.\scripts\harness.ps1 doctor
```

Unity Editor, .NET SDK·런타임, Git 설치를 점검한다. 프로젝트가 사용하는 에디터 버전은 [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt)에 있다. Unity Hub에서 이 폴더를 프로젝트로 추가하고 해당 버전으로 연다. Hub의 기본 위치 외에 에디터를 설치했다면 현재 셸에서 `$env:UNITY_EDITOR_PATH`를 해당 `Unity.exe`의 전체 경로로 지정한 뒤 점검한다.

## Core 규칙을 검증할 때

```powershell
.\scripts\harness.ps1 test
```

Unity를 열지 않고 `Assets/Game/Core`의 실제 소스를 컴파일해 NUnit 테스트를 실행한다. Core는 .NET Standard 2.1과 C# 9를 사용하며, 테스트에는 .NET 10을 빌드할 수 있는 SDK와 .NET 10 런타임이 필요하다. 처음 실행할 때는 NuGet 패키지를 복원할 네트워크 연결이 필요하다.

특정 판정만 반복 확인할 때는 같은 테스트 프로젝트에 필터를 적용한다. 아래 예시는 d20의 성공·실패와 입력 범위를 검사한다.

```powershell
dotnet test DotNet/DungeonTycoon.Core.Tests/DungeonTycoon.Core.Tests.csproj --filter FullyQualifiedName~D20CheckTests
```

## 씬·프리팹·Unity 연동을 확인할 때

1. 프로젝트 루트에서 `unity pipeline editors`로 경로가 일치하는 실행 중인 에디터를 확인한다. 공식 Unity CLI가 필요하면 Windows에서 `winget install --id Unity.CLI --exact`로 설치한다. 프로젝트에 Pipeline이 없으면 `unity pipeline install`로 연결 패키지를 준비한다. [Unity 공식 연결 안내](https://docs.unity.com/en-us/unity-cli/reference/editor-project-commands)
2. `unity command`로 해당 에디터에서 제공하는 명령을 확인한다. [시작 씬](Assets/Scenes/SampleScene.unity)의 임포트와 컴파일이 끝나면 `unity command editor.playmode start`로 실행하고 CLI로 변경한 동작을 재현한다. 같은 경로로 여러 인스턴스가 잡히면 `unity command --pid <PID> editor.playmode start`처럼 대상을 지정한다.
3. CLI에서 씬 이름, Play 상태, 게임 상태 변화와 콘솔 오류를 확인한다. 완료 보고에 수행한 조작과 결과를 적고, 연결이나 컴파일에서 막혔다면 첫 관련 오류를 기록한다.

## 웹 프로토타입으로 규칙을 빠르게 시험할 때

[Prototype](Prototype/)은 한 판 흐름을 브라우저에서 바로 돌려 보는 HTML 프로토타입이다. Unity 구현과 별개이며, 수치는 모두 조정용 임시값이다. 지금은 [게임 규칙](gamerules.md)으로 바뀌기 전의 직선 경로 4칸·라운드제 전투 규칙을 따른다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Prototype/serve.ps1
```

실행 후 `http://localhost:8765/`에서 플레이하고, `http://localhost:8765/sim.html`에서 첫 플레이 검증 기준(빈 던전 패배·성장한 파티 승리)을 시드 200개로 확인한다. 규칙 로직은 `Prototype/core.js`, 화면과 실시간 진행·모의 AI는 `Prototype/index.html`에 있다.

## Unity 패키지를 추가하거나 복원이 실패할 때

Unity Package Manager의 직접 의존성은 [manifest.json](Packages/manifest.json), 실제로 해석된 버전은 [packages-lock.json](Packages/packages-lock.json)에서 확인한다. 이 프로젝트는 렌더링에 URP, Unity 비동기 처리에 UniTask를 사용한다. UniTask는 Git URL로 고정되어 있으므로 처음 받으려면 Git과 원격 저장소에 대한 네트워크 연결이 필요하다.

## GitHub에 올리거나 받을 때

원격 저장소는 비공개 저장소 [Meerkat-GMD/DugeonTycoon](https://github.com/Meerkat-GMD/DugeonTycoon)이고 `origin`으로 연결되어 있다. 평소에는 `git push`와 `git pull`만 쓴다. 저장소 생성, PR, 이슈 같은 GitHub 작업에는 GitHub CLI(`gh`)를 쓴다. 없으면 아래 명령으로 설치한다. 설치 직후 열려 있던 셸은 PATH가 갱신되지 않으므로 다시 열거나 `C:\Program Files\GitHub CLI\gh.exe`를 직접 실행한다.

```powershell
winget install --id GitHub.cli --exact
```

처음 한 번은 `gh auth login --web`으로 로그인한다. 토큰은 OS 키링에 저장되며 git의 HTTPS 인증도 함께 처리한다. 로그인 상태는 `gh auth status`로 확인한다.

## Meshy로 3D 모델을 만들 때

프로젝트 루트의 [.mcp.json](.mcp.json)에 Meshy 공식 MCP 서버(`@meshy-ai/meshy-mcp-server`)가 버전 고정으로 등록되어 있다. Claude Code가 이 폴더에서 열리면 서버 사용 승인을 한 번 묻는다. 서버는 `npx`로 실행되므로 Node.js가 필요하다. 없으면 아래 명령으로 설치하고 셸을 다시 연다.

```powershell
winget install --id OpenJS.NodeJS.LTS --exact
```

API 키는 [Meshy 개발자 플랫폼](https://docs.meshy.ai/api/ai)의 API Keys에서 발급한다. 사용자 환경 변수 `MESHY_API_KEY`로 설정하고 Claude 앱을 완전히 종료한 뒤 다시 연다. 키는 `.mcp.json`이나 저장소 파일에 적지 않는다.

```powershell
[Environment]::SetEnvironmentVariable('MESHY_API_KEY', '<발급한 키>', 'User')
```

- MCP 호출도 REST API와 같은 크레딧이 차감되고 같은 rate limit을 받는다. 생성 전에 `meshy_check_balance`로 잔액을 확인한다. 대기 중인 작업만 취소·환불되고 진행 중인 작업은 취소할 수 없다.
- `meshy_download_model`은 기본적으로 실행 폴더의 `meshy_output/`에 저장하며, 이 폴더는 git에서 제외된다. Unity에 넣을 모델은 `format`을 `fbx`로 지정하고 `save_to`에 `Assets/` 아래 절대 경로를 준다. 이 프로젝트에는 glTF 임포터 패키지가 없어서 GLB는 바로 임포트되지 않는다.
- 탑다운 시점용 에셋은 `meshy_text_to_3d`의 `model_type`을 `smart-topology`로, `target_polycount`를 지정해 폴리곤 수를 제한한다. 리깅·애니메이션을 붙일 캐릭터는 `pose_mode`를 `t-pose`로 생성한다. 에셋별 폴리곤 예산은 아직 정하지 않았다.
- 서버 버전을 올릴 때는 `npm view @meshy-ai/meshy-mcp-server version`으로 최신 버전을 확인하고 `.mcp.json`의 버전을 바꾼다. 이름이 비슷한 비공식 패키지 `meshy-ai-mcp-server`와 혼동하지 않는다.

## OpenAI 용사 판단

기본 실행은 역할별 로컬 전술로 동작한다. 에디터에서 OpenAI의 행동 선언을 사용하려면 Unity Editor 프로세스가 시작될 때 `OPENAI_API_KEY` 환경 변수를 상속하도록 설정한다. Unity Hub에서 실행한다면 환경 변수 설정 후 Hub까지 완전히 종료하고 다시 연다. `OPENAI_MODEL`로 모델을 선택할 수 있으며 기본값은 `gpt-4.1-mini`다.

전투가 시작되거나 몬스터가 합류하거나 용사가 위독해지거나 쓰러지는 등 전투 상황이 바뀌면, 살아 있는 용사마다 먼저 노릴 적, 먼저 지킬 동료, 대사를 한 요청으로 받는다. 응답을 기다리는 동안에도 전투는 로컬 전술로 계속되고, 그사이 상황이 다시 바뀌었다면 늦게 온 응답은 버린다. 낙석을 선언하면 대표 용사의 대응을 별도로 요청한다. 파티가 갈림길에 멈추면 각 용사가 고를 길과 주장을 한 요청으로 받는다. 이 요청에는 파티가 보거나 기억하는 정보만 담는다. 전투와 주사위 결과, 의견이 갈렸을 때의 설득 판정은 로컬 C# 규칙이 정한다. 응답 지연·요청 실패·유효하지 않은 행동이나 의견에는 로컬 전술과 성격별 기본 의견이 적용된다. 플레이어 빌드에서는 로컬 전술을 사용한다.
