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

## 밸런스 수치를 바꿀 때

몬스터·보스 능력치, 경험치와 레벨 성장, 위협 예산, 역경 난이도, 상자 희귀 확률, 샘물 회복량은 [balance.json](Assets/Game/Unity/Resources/Balance/balance.json) 한 파일에 있다. 값을 바꾸고 아래 명령으로 결과를 본다.

```powershell
.\scripts\harness.ps1 balance
```

10~20초 안에 표 두 개가 나온다. 첫 표는 빈 던전과 길목 배치의 원정 승률, 최종 보스 앞 레벨, 최종 보스전 시간이다. 둘째 표는 스테이지마다 예산을 다 쓴 배치에서 가장 약해진 용사의 체력과 쓰러짐·전멸 수다. 시드를 늘려 더 안정된 숫자를 보려면 `-Seeds 12`를 붙인다. 시드별 자세한 기록은 `BalanceProbe`의 `PrintExpeditions`, `PrintFullBudgetFights`를 필터로 실행해 본다.

- 키 이름은 [BalanceTable](Assets/Game/Core/BalanceTable.cs)의 필드 이름이다. 예산·역경 난이도·상자 희귀 확률은 1·2·3스테이지 순서의 목록이고, 일반 몬스터 능력치는 모든 스테이지에 같은 한 값이다. `armorScale`은 피해 공식의 방어 기준값이다. Unity의 JsonUtility가 주석을 읽지 못하므로 파일에 주석을 넣지 않는다.
- 게임은 새 원정을 시작할 때마다 파일을 다시 읽는다. 에디터에서 파일을 고친 뒤 Unity가 다시 임포트하면 다음 원정부터 적용된다. 파일이 없거나 값이 잘못되면 콘솔에 이유를 남기고 코드의 기본값을 쓴다.
- Core 테스트도 같은 파일을 읽는다. 값을 바꾸면 `.\scripts\harness.ps1 test`로 규칙 검증(빈 던전 패배·길목 배치 승리 등)을 다시 확인한다. 파일에 빠진 키나 오타가 있으면 `BalanceFileTests`가 실패한다.
- 수치를 확정하면 [게임 규칙의 조정할 값](gamerules.md#조정할-값)에 지금 값과 측정 결과를 옮긴다.

## 씬·프리팹·Unity 연동을 확인할 때

1. 프로젝트 루트에서 `unity pipeline editors`로 경로가 일치하는 실행 중인 에디터를 확인한다. 공식 Unity CLI가 필요하면 Windows에서 `winget install --id Unity.CLI --exact`로 설치한다. 프로젝트에 Pipeline이 없으면 `unity pipeline install`로 연결 패키지를 준비한다. [Unity 공식 연결 안내](https://docs.unity.com/en-us/unity-cli/reference/editor-project-commands)
2. `unity command`로 해당 에디터에서 제공하는 명령을 확인한다. [시작 씬](Assets/Scenes/SampleScene.unity)의 임포트와 컴파일이 끝나면 `unity command editor.playmode start`로 실행하고 CLI로 변경한 동작을 재현한다. 같은 경로로 여러 인스턴스가 잡히면 `unity command --pid <PID> editor.playmode start`처럼 대상을 지정한다.
3. 대화 무대(갈림길 논의, 역경, 전리품 분배)를 기다리지 않으려면 Play를 시작한 뒤 `unity command script eval --file <파일>`로 `DungeonTycoon.Unity.DialogueStage.FastForward = true;`를 실행한다. 대사가 한 번에 나오고 줄 사이 대기와 주사위 연출을 건너뛰며, 신탁은 `DialogueStage.FastOraclePick`(기본 -1, 넘어간다)에 정한 번호를 바로 고른다. 기도 장면도 같은 설정으로 바로 넘어가고, 응답은 `PrayerScene.FastAnswer`(0 치유의 기적, 1 안식의 기적, 기본 2 외면한다)를 고른다. 신앙이 모자라 고를 수 없는 기적이면 외면한다. 결과는 Core가 먼저 정하므로 판정은 같고, 값은 저장되지 않으며 Play를 다시 시작하면 꺼진다. AI 응답 대기는 그대로다. 스크립트에 `"`가 들어가면 PowerShell이 `--code` 인자를 망가뜨리므로 파일로 넘긴다.
4. CLI에서 씬 이름, Play 상태, 게임 상태 변화와 콘솔 오류를 확인한다. 완료 보고에 수행한 조작과 결과를 적고, 연결이나 컴파일에서 막혔다면 첫 관련 오류를 기록한다.

화면이 뿌옇게 보이면 코드를 고치기 전에 Game 뷰부터 확인한다. 고정 해상도(예: Full HD)가 창보다 크면 Unity가 프레임 전체를 축소해 보여 주고, Windows 배율이 100%가 아닐 때 Low Resolution Aspect Ratios가 켜져 있으면 작게 그린 뒤 늘려서 보여 준다. 선명도를 판단할 때는 Free Aspect에 Low Resolution을 끈 상태로 본다.

## HUD를 고칠 때

HUD는 UI Toolkit으로 만든다. 모양은 [DungeonHud.uxml](Assets/Game/Unity/Resources/Hud/DungeonHud.uxml)(구조)과 [DungeonHud.uss](Assets/Game/Unity/Resources/Hud/DungeonHud.uss)(색·크기·위치)에 있고, Unity 에디터에서 UXML을 더블클릭하면 UI Builder에서 바로 고칠 수 있다. [DungeonHud.cs](Assets/Game/Unity/DungeonHud.cs)는 이름(`name`)으로 요소를 찾아 값과 클래스만 바꾸므로, 요소를 옮기거나 스타일을 바꿔도 이름은 유지한다.

- 크기 단위는 화면 높이 약 640 단위 기준이다. 실행 중에는 `DungeonHud.cs`가 [HudPanel](Assets/Game/Unity/Resources/Hud/HudPanel.asset)의 복사본 배율을 화면에 맞게 바꾸고, 에셋 자체는 배율 1로 둔다.
- `picking-mode="Ignore"`인 요소는 마우스를 아래 던전으로 통과시킨다. 글자만 있는 표시나 말풍선처럼 던전 조작을 막지 않아야 하는 요소에 쓴다.
- 카메라는 용사 카드 열(`party`), 보스 바(`boss-bar`), 아래 패널(`bottom`)의 실제 위치를 읽어 가려지지 않는 영역을 정한다. 이 요소들의 이름을 바꾸면 스크립트도 같이 바꾼다.
- 게임 글꼴은 [Mona10](Assets/Art/Fonts/Mona10/)의 동적 SDF 글꼴 에셋이다. `DungeonHud.uss`의 `.hud`가 기본으로 Regular(`Mona10-SDF`)를 쓰고, Regular 에셋의 굵기 표에 Bold 에셋(`Mona10-Bold-SDF`)이 연결되어 있어 USS의 `-unity-font-style: bold`가 실제 굵은 글꼴로 그려진다. 대화·기도 화면도 `hud` 안에 복제되므로 같은 글꼴을 쓴다.
- Mona10에 없는 글자는 대체 글꼴 표에 연결된 [Noto Sans KR](Assets/Art/Fonts/NotoSansKR/)(SIL Open Font License, [OFL.txt](Assets/Art/Fonts/NotoSansKR/OFL.txt))로 그린다(Regular→Noto Regular, Bold→Noto Bold). Noto 원본 OTF는 [notofonts/noto-cjk](https://github.com/notofonts/noto-cjk)의 `Sans/SubsetOTF/KR`에서 받았다.

## 웹 프로토타입으로 규칙을 빠르게 시험할 때

[Prototype](Prototype/)은 한 판 흐름을 브라우저에서 바로 돌려 보는 HTML 프로토타입이다. Unity 구현과 별개이며, 수치는 모두 조정용 임시값이다. 지금은 [게임 규칙](gamerules.md)으로 바뀌기 전의 직선 경로 4칸·라운드제 전투 규칙을 따른다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Prototype/serve.ps1
```

실행 후 `http://localhost:8765/`에서 플레이하고, `http://localhost:8765/sim.html`에서 첫 플레이 검증 기준(빈 던전 패배·성장한 파티 승리)을 시드 200개로 확인한다. 규칙 로직은 `Prototype/core.js`, 화면과 실시간 진행·모의 AI는 `Prototype/index.html`에 있다.

연출을 정하려고 만드는 일회용 HTML 프로토타입은 `Prototypes/`에 두고, 실제 초상화(`Assets/Art`)를 쓰도록 저장소 루트를 띄운다. Claude 앱에서는 `.claude/launch.json`의 `repo-prototypes` 설정이 같은 일을 한다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Prototype/serve.ps1 -Port 8766 -Root .
```

결정이 나면 프로토타입은 `prototype/<주제>` 브랜치에 커밋해 보관하고 main에서는 지운다. 결정 내용만 [게임 규칙](gamerules.md)에 남긴다. 보관 중인 브랜치는 `prototype/dialogue-dice`(대화 무대), `prototype/loot-oracle-fatigue`(전리품 분배·신탁·피로), `prototype/prayer`(기도 장면), `prototype/decisions-tactics`(전투 작전의 Decisions API와 Responses API 속도·답 비교, `node`로 실행하는 스크립트)다. 다시 보려면 해당 브랜치의 파일을 꺼내 위 명령으로 띄운다.

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

기본 실행은 역할별 로컬 전술로 동작한다. 에디터에서 OpenAI의 행동 선언을 사용하려면 Unity Editor 프로세스가 시작될 때 `OPENAI_API_KEY` 환경 변수를 상속하도록 설정한다. Unity Hub에서 실행한다면 환경 변수 설정 후 Hub까지 완전히 종료하고 다시 연다. `OPENAI_MODEL`로 모델을 선택할 수 있으며 기본값은 `gpt-5.4-mini`다. API 크레딧은 키를 만든 조직에서 차감되므로, 크레딧이 있는 조직(예: Personal)을 고른 상태에서 키를 만든다. ChatGPT 구독 크레딧은 API에 쓰이지 않는다.

```powershell
[Environment]::SetEnvironmentVariable('OPENAI_API_KEY', '<발급한 키>', 'User')
```

`gpt-5.`으로 시작하는 모델에는 추론 강도 `none`을 보내 짧은 JSON 응답이 빨리 오게 한다. 이전 모델에는 이 값을 보내지 않는다. 요청이 실패하거나 응답이 잘리면 Unity 콘솔에 이유(HTTP 상태, 잘못된 키나 모델, 시간 초과 등)를 경고로 남기고, 그 판단은 로컬 전술로 진행한다.

전투 작전만은 같은 키로 [Decisions API](https://developers.openai.com/api/docs/guides/decisions)(`POST /v1/decisions`, 모델 `gpt-6-luna`, `OPENAI_MODEL`과 상관없음)에 보낸다. 대사를 쓰지 않고 주어진 보기 중 하나를 확신도와 함께 고르는 엔드포인트다. 비교 시험에서 중앙값 약 0.27초로, Responses API의 약 1.5초보다 빨랐다. 출력 토큰 요금이 없어 요청 한 번에 약 $0.0002다. Decisions 요청이 실패하면 30초 동안 전투 작전만 로컬 전술로 진행하고, 다른 요청은 그대로 보낸다.

전투가 시작되거나 몬스터가 합류하거나 용사가 위독해지거나 쓰러지는 등 전투 상황이 바뀌면, 살아 있는 용사마다 먼저 노릴 적을, 힐러에게는 먼저 지킬 동료도 한 Decisions 요청으로 받는다. 대사는 Core가 정해 둔 말에서 고른다. 응답을 기다리는 동안에도 전투는 로컬 전술로 계속되고, 그사이 상황이 다시 바뀌었다면 늦게 온 응답은 버린다. 낙석을 선언하면 대표 용사의 대응을 별도로 요청한다. 파티가 갈림길에 멈추면 각 용사가 고를 길과 주장을 한 요청으로 받는다. 이 요청에는 파티가 보거나 기억하는 정보만 담는다. 전투와 주사위 결과, 의견이 갈렸을 때의 설득 판정은 로컬 C# 규칙이 정한다. 응답 지연·요청 실패·유효하지 않은 행동이나 의견에는 로컬 전술과 성격별 기본 의견이 적용된다. 플레이어 빌드에서는 로컬 전술을 사용한다.
