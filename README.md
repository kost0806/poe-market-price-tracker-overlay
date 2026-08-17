# PoE 시세 오버레이

Path of Exile 1을 하는 동안, 관심 있는 통화성 아이템(갑충석·정수·생기 등)의 시세를
**게임 화면 위에서 상시 확인**하는 Windows 오버레이. 시세는 [poe.ninja](https://poe.ninja)에서 받는다.

| | |
|---|---|
| 대상 게임 | Path of Exile **1** |
| 플랫폼 | Windows 10/11 · .NET 8 (`net8.0-windows`) · WPF |
| 데이터 소스 | poe.ninja exchange API 단독 (인증 불필요) |
| 상태 | 로컬 빌드로 동작. 테스트 643개(Core 558 · Shell 85) 통과 |
| 용도 | 본인 전용 로컬 빌드 — 설치 관리자·자동 업데이트는 범위 밖이다 |

---

## 무엇을 하는가

관심목록에 담은 아이템을 **5분마다**(설정 가능) 카테고리 단위로 조회해, 한 줄에 하나씩 그린다.

```
[아이콘]  뿔 갑충석 (역병)     359.7c (1.85d)   ▲2.9%
[아이콘]  혈기생기             1c당 15.5개      ▼6.2%
                                        마지막 갱신: 2분 전
```

- **아이콘 · 이름 · 현재가 · 변동률**, 그리고 화면 아래에 마지막 갱신 시각.
- **단가가 1 미만이면 역수로 적는다.** 생기는 개당 0.06카오스라 그대로 띄우면 읽히지 않는다.
- **표시 통화는 아이템별로 `자동 / 카오스 / 디바인`.** 기본 `자동`은 응답의 `maxVolumeCurrency`,
  즉 그 아이템이 실제로 가장 많이 거래되는 상대 통화를 따른다.
- **디바인 환산은 직접 계산한다.** API가 주는 반올림된 환산값을 역수로 뒤집으면 저가 아이템에서
  오차가 10%까지 벌어진다 (REQUIREMENTS FR-04-5).
- **아이콘은 앱과 함께 배포되는 로컬 파일**이다. 실행 중에 이미지를 받아오지 않는다.

### 화면은 셋이다

| | |
|---|---|
| **오버레이** | **표시 전용.** 마우스·키보드 입력을 일절 받지 않고(`WS_EX_TRANSPARENT`) 포커스도 갖지 않는다(`WS_EX_NOACTIVATE`). 클릭이 전부 게임으로 통과하므로 오버레이 때문에 게임이 최소화되는 일이 없다 |
| **트레이 아이콘** | 오버레이가 작업 표시줄에 나타나지 않으므로 **유일한 진입점**이다. 설정 창 열기와 종료가 여기 있다 |
| **설정 창** | 일반 창이다. 관심목록 검색·추가·제거, 리그, 갱신 주기, 표시 언어(영문/한글), 표시 통화, 투명도, 그리고 **위치 이동 모드** |

오버레이는 클릭을 받지 않으니 드래그로 옮길 수도 없다. 그래서 **위치 이동 모드**가 있다 —
설정 창에서 켜는 동안만 클릭 통과를 풀어 창을 옮기고 크기를 바꾼다. 끄면 즉시 되돌아가며,
이 상태는 저장하지 않는다(재시작하면 항상 꺼져 있다).

### 검색은 시세가 아니라 카탈로그를 뒤진다

한 번도 조회한 적 없는 카테고리의 아이템도 이름으로 찾히고, 그 자리에서 관심목록에 넣을 수 있다
(추가할 때 그 카테고리를 1회 조회한다). 아직 시세가 없는 행은 값 자리에 `시세 없음`이라고 적는다.

앱은 **동봉된 아이템 카탈로그**(957 슬러그 / 18 카테고리)를 가지고 있고, 검색은 그것과 받아 온
캐시를 함께 뒤진다. 카탈로그가 없던 시절에는 갑충석 이름 115개를 들고 있으면서도 "갑충석"을
검색하면 아무것도 나오지 않았다 — 사용자가 `Scarab` 카테고리를 먼저 조회할 줄 알아야 했기 때문이다.

---

## 시작하기

### 필요한 것

- **.NET 8 SDK** (검증에 쓴 버전 8.0.424)
- Windows. `PoeOverlay.Shell`은 `net8.0-windows`이므로 다른 OS에서는 빌드되지 않는다
- 게임을 **테두리 없는 창(Windowed Fullscreen)** 으로 실행할 것. 전용 전체화면에서는 어떤
  오버레이도 보이지 않으며, 이 앱은 안티치트 위험을 이유로 DirectX 훅을 쓰지 않는다 (NFR-04)

### 빌드 · 테스트 · 배포

`scripts/`가 정본이다. 넷 다 PowerShell이고 `-?`로 도움말이 나온다.

```powershell
./scripts/build.ps1                      # restore + build (기본 Debug)
./scripts/test.ps1                       # build + 전체 테스트
./scripts/test.ps1 -Project Core -Filter 'FullyQualifiedName~Pricing'
./scripts/publish.ps1                    # 테스트 후 artifacts/publish/Release/ 에 배포
./scripts/clean.ps1 -WhatIf              # bin/ obj/ artifacts/ 삭제, 미리보기
```

경고는 `Directory.Build.props`가 오류로 승격시킨다(`Nullable` · `CA2007` · `CA1031`).
스위치가 아니라 프로젝트 속성이므로 **어떤 방법으로 빌드해도 똑같이 실패한다.**

배포물은 프레임워크 의존 · RID 없음 · 단일 파일 아님이다. 사전(`Localization/*.json`)과
아이콘(`Icons/`), 카탈로그(`Catalog/`)가 exe 옆에 **파일로 남아야** 하기 때문이다 — 리그가 바뀌면
다시 빌드하지 않고 갈아 끼울 수 있는 것이 그것들을 파일로 둔 이유다.

```powershell
./scripts/publish.ps1
./artifacts/publish/Release/PoeOverlay.exe
```

### 처음 실행할 때

1. **트레이 아이콘이 안 보인다면 오버플로 안에 있다.** Windows 11은 새로 등록된 트레이 아이콘을
   플라이아웃에 숨긴다. 셰브론(`^`)을 눌러 찾은 뒤 **고정(pin)** 해 두는 것을 권한다.
2. **게임이 화면을 덮은 동안에는 트레이에 손이 닿지 않는다.** 작업 표시줄이 게임 아래로 내려가기
   때문이다. `Win` 키나 `alt-tab`으로 작업 표시줄을 먼저 띄운 뒤 아이콘을 누른다.
   전역 핫키는 의도적으로 넣지 않았다 (REQUIREMENTS E1) — 관심목록 편집은 상시 작업이 아니다.

첫 실행에는 **설정 창이 저절로 열리면서** 이 두 가지를 배너로 알린다. 확인을 누르면 설정에
기록되어 다시 뜨지 않는다.

### 설정 파일

`%APPDATA%\PoeOverlay\settings.json` — 로그는 `%APPDATA%\PoeOverlay\logs\`.

```jsonc
{
  "league": null,                     // null = 현재 챌린지 리그 자동
  "refreshIntervalMinutes": 5,
  "language": "en",                   // "ko" 로 전환 가능
  "defaultDisplayCurrency": "auto",   // "auto" | "chaos" | "divine"
  "window": { "x": 100, "y": 100, "width": 420, "height": 500, "opacity": 0.87 },
  "watchlist": [
    { "id": "vivid-lifeforce", "category": "Currency", "displayCurrency": "divine" },
    { "id": "essence-of-horror", "category": "Essence" }
  ]
}
```

아이템 식별자는 표시 이름이 아니라 **poe.ninja 슬러그**다(`vivid-lifeforce`). 이름은 리그·언어에
따라 바뀌지만 슬러그는 그대로다.

---

## 저장소 구조

```
src/PoeOverlay.Core/       플랫폼 독립 로직. Domain · Market · Store · Pricing · Polling
                           Settings · Localization · Catalog · Presentation(뷰모델)
src/PoeOverlay.Shell/      WPF와 Win32. Overlay · Tray · Settings(창) · Interop · Composition
tests/                     xUnit. Core.Tests(net8.0) · Shell.Tests(net8.0-windows)
docs/                      요구사항과 설계 문서
tools/                     동봉 데이터 생성기 (Python)
data/                      포획해 둔 원본 응답과 아이콘 PNG 676장(5.3 MB)
                           아이콘은 생성기의 입력이자 배포물 자체다 — 빌드가 여기서
                           출력 폴더의 Icons/ 로 복사한다. 저장소에 두 벌 두지 않는다
scripts/                   빌드·테스트·배포·정리
```

뷰모델이 `Core`에 있는 것은 의도다. 그 덕분에 오버레이와 설정 창의 표시 로직이 WPF 없이
테스트된다 — Core 테스트 558개 중 상당수가 그것이다.

### 동봉 데이터는 전부 생성물이다

세 가지가 저장소에 커밋되어 있고, 셋 다 **손으로 고치지 않는다.**

| 생성물 | 무엇 | 생성기 |
|---|---|---|
| `Localization/ko.json` | 슬러그 → 한글 아이템 이름 | `build-ko-dictionary.py` |
| `Icons/item-icons.json` | 슬러그 → 아이콘 파일 이름 (PNG는 `data/images/`) | `build-icon-manifest.py` |
| `Catalog/item-catalog.json` | 슬러그 → 카테고리 · 영문 이름 | `build-item-catalog.py` |

한글 이름의 출처는 **GGG 공식 한국 trade static API**다. poe.ninja는 어떤 매개변수로도 한글을
주지 않으며, poe.ninja의 한글 카테고리 라벨은 게임 내 용어와 다르다(Scarab을 "쇠똥구리"라고 한다).
GGG static은 한국어 클라이언트 데이터로 GGG가 직접 만든 값이라 이 문제가 없다.

리그가 바뀌었을 때의 재생성은 한 줄로 이어진다. **네트워크를 쓰는 것은 첫 스크립트뿐이다.**

```bash
python3 tools/fetch-ko-sources.py --icons   # 두 static 응답 + poe.ninja 오버뷰 18개 → data/
curl -K curl.cfg                            # 아이콘 내려받기
python3 tools/build-ko-dictionary.py
python3 tools/build-icon-manifest.py
python3 tools/build-item-catalog.py
```

카탈로그만 갱신하려면 `fetch-ko-sources.py --catalog-only` → `build-item-catalog.py`.
생성기 셋은 각자 **쓰기를 거부하는 조건**을 가지고 있다(알 수 없는 카테고리, 빈 이름, 항목 0개 등).
조용히 망가진 생성물이 커밋되는 것을 막기 위한 것이다.

---

## 설계 문서

이 저장소는 **설계가 코드보다 먼저 바뀐다.** 기능을 더하거나 동작을 바꿀 때는 문서를 먼저 고치고
코드가 따라간다. 권위는 위에서 아래로 흐른다.

| 문서 | 역할 |
|---|---|
| [`docs/REQUIREMENTS.md`](docs/REQUIREMENTS.md) | 무엇을 해야 하는가. 요구사항 ID 43개. 범위의 정본 |
| [`docs/design/00-api-contract.md`](docs/design/00-api-contract.md) | 실측한 poe.ninja 계약. **구속력 있음** |
| [`docs/design/00-shell-measurements.md`](docs/design/00-shell-measurements.md) | 실측한 Win32·렌더링 사실. **모든 설계 주장보다 우선한다** |
| [`docs/design/01-hld.md`](docs/design/01-hld.md) | 아키텍처와 결정 D1–D22 |
| [`docs/design/02-lld-core.md`](docs/design/02-lld-core.md) | Core의 여덟 모듈 |
| [`docs/design/03-lld-shell.md`](docs/design/03-lld-shell.md) | Shell과 Presentation |
| [`docs/design/04-dld.md`](docs/design/04-dld.md) | 시그니처·JSON 이름·오류 코드·테스트 배치·상수 |

**실측이 설계 주장을 이긴다.** 코드가 실측된 사실과 어긋나면 코드가 틀린 것이다 — 다시 재어 보고
그 사실이 성립하지 않는다면, 먼저 실측 문서를 고치고 무엇이 바뀌었는지 적는다.

작업 규칙(예외 대신 값으로 실패하기, `Presentation` 밖의 모든 `await`에 `ConfigureAwait(false)`,
시간은 `TimeProvider`로만)은 [`CLAUDE.md`](CLAUDE.md)에 있다.

---

## 범위 밖 · 알려진 제약

| | 이유 |
|---|---|
| 이력 가격 차트 | API가 절대 가격의 시계열을 주지 않는다. 변동률은 API가 계산해 준다 |
| 가격 알림 | 먼저 "보이는 것"을 완성한 뒤 판단한다 |
| 고유 아이템 · 젬 · 지도 · 착용 장비 | 모델이 복잡해지고 다른 API 형태가 필요하다 |
| 공식 Trade API | OAuth · 레이트리밋 · 약관. 스택 아이템에는 poe.ninja로 충분하다 |
| 전역 핫키 · 게임 프로세스 감지 | 감수한 비용이다. 위 「처음 실행할 때」 참조 |
| 전용 전체화면 지원 | 어떤 오버레이도 표시되지 않으며, 훅 인젝션은 안티치트 위험 때문에 쓰지 않는다 |

---

## 출처

- 시세는 **[poe.ninja](https://poe.ninja)** 에서 온다. poe.ninja는 **Grinding Gear Games와 무관한
  커뮤니티 사이트**다 (NFR-05).
- 한글 아이템 이름과 아이콘은 GGG 공식 trade static API에서 온다.
- Path of Exile은 Grinding Gear Games의 상표다. 이 프로젝트는 GGG와 아무 관계가 없다.
