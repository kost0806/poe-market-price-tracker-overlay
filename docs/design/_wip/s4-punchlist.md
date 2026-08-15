# S4 제2판 마감 지시서 — 리뷰 3종 통합

`04-dld.md` 초판에 대한 리뷰 3종(csharp-reviewer 프로브 빌드 · silent-failure-hunter · 완성도/표식 감사)을 호출자가 판정했다. **기각된 지적은 없다.**

**초판은 표식 해소에 성공했다** — `→ S4` 표식 24개 중 **18 해소 · 4 부분 · 0 누락**. API 계약·측정 문서에 대한 충실도는 **깨끗하다**(§H). 문제는 표식이 아니라 **문서가 스스로 도입한 컴파일 차단**과 **빈 상수**다.

**절 번호를 바꾸지 마라.** 목표는 `04-dld.md` **제2판**이며, S2에 대한 개정 요구 2건이 딸려 있다.

---

## §A. 컴파일 차단

### A1. `partial` 선언의 접근 한정자 충돌 — **CS0262 4건, Core 8모듈 중 넷**

| 타입 | 현재 |
|---|---|
| `Store` | §8.3 `public sealed class` / §8.4 `public sealed partial` / §8.5 `internal sealed partial` |
| `PollingService` | §9.1 `public` / §9.2 `internal partial` |
| `SettingsStore` | §10.5 / §10.6 · §10.9 |
| `SnapshotFanout` | §11.2 |

두 가지가 겹쳤다 — **첫 선언에 `partial`이 없고**, 이어지는 선언들의 **접근 한정자가 다르다**.

**조치** — 모든 부분 선언에 `partial`을 붙이고 접근 한정자를 통일하라. 문서를 여러 절에 나눠 싣더라도 **선언 헤더는 문자 단위로 같아야 한다**는 규칙을 §2에 한 줄로 명시하라.

### A2. `SettingsWriteDto` 계열이 컴파일되지 않는다 — **CS8618 7건** 【측정】

§2.2의 `Directory.Build.props`와 `.editorconfig`를 그대로 적용해 §10.7의 세 클래스를 빌드하면 `Language`·`DefaultDisplayCurrency`·`Window`·`Watchlist`·`WindowWriteDto.HeightMode`·`WatchlistEntryWriteDto.Id`·`.Category`에서 **CS8618이 오류로** 뜬다. 생성자도 `required`도 없는 비널 `{ get; init; }`이 `WarningsAsErrors=Nullable` 아래 전부 걸린다. §1이 `null!`을 금지하므로 우회로도 없다.

**이것이 §19.1이 "닫았다"고 주장한 결함과 같은 계열의 재발이다.** 그 수정본이 자기가 고치려던 문제와 같은 방식으로 깨져 있고, **§16.6의 회귀 테스트가 애초에 실행될 수 없다.**

**조치** — 일곱 속성에 `required`를 붙이거나 생성자를 부여하라.

### A3. Shell의 타입 오류 셋

| 위치 | 오류 |
|---|---|
| §12.3 | `internal static class OverlayGeometryValidator`를 `DisplayChangeWatcher`의 **매개변수 타입**으로 넘긴다 — 정적 타입은 매개변수가 될 수 없다 |
| `OverlayWindow` | **public 생성자가 internal 델리게이트**(`ExtendedStyleGate.Factory`)를 받는다 — **CS0051** |
| `Win32Constants` / `ExtendedStyleBits` | 본문이 *"15절 상수표 참조"*로 생략됐는데 **§15에 Win32 값이 하나도 없다** |

### A4. `InstanceSignal.TrySend(int firstProcessId, …)`가 S3 §3.2와 모순된다

S3 §3.2는 발견을 **`FindWindowEx(HWND_MESSAGE, null, className, null)` — 클래스 이름 기반**으로 결정했고, 두 번째 인스턴스가 첫 인스턴스의 **PID를 알아낼 수단이 어디에도 없다.** 명세대로 구현할 수 없다.

**조치** — PID 매개변수를 제거하고 클래스 이름 탐색으로 정렬하라.

---

## §B. 선언은 있는데 쓸 수 없는 것들

| # | 문제 |
|---|---|
| B1 | **`MoveModeWatchdog`에 생성자와 `Dispose()`밖에 없다.** 드래그·리사이즈 활동으로 **유휴 타이머를 리셋할 방법이 없고**, S3 §4.6.1이 `LostMouseCapture`에게 나중에 소비하라고 요구한 **"만료됨" 플래그를 세울 방법도 없다** |
| B2 | **`SettingsViewModel`**: `CancellationToken windowScopeToken`을 받는데 공급할 팩터리가 없고, §11.5의 생성자 목록이 **§19.4가 추가한 매개변수를 빠뜨렸으며**, §8.4는 이 뷰모델이 `Store.SetFetchedListing`을 부른다고 하는데 **생성자에 `Store`도 그것을 노출하는 인터페이스도 없다** |
| B3 | **`SnapshotFanout.Attach`를 부르는 곳이 §12 어디에도 없다** |
| B4 | `RoundTrigger{Startup,Scheduled,Repoll,LeagueChanged}`(Domain)와 `PollingTriggerKind{Scheduled,Repoll}`(§9.1)이 **사상 없이 공존**하고 `RoundTrigger`에는 소비자가 없다 |
| B5 | `MessageOnlyWindowFactory.cs`·`FirstRunGate.cs`가 §2.1 배치에 있는데 **시그니처가 어디에도 없다.** 그리고 **FR-08-6의 「첫 실행 시 설정 창 자동 표시」에 주체가 없다**(뷰모델의 `ShowFirstRunBanner`/`DismissFirstRunBannerCommand`뿐) |
| B6 | **S3 §3.3의 a~f 종료 순서를 수행할 호스트 타입이 없다.** `DispatcherUnhandledException` 핸들러와 트레이 폐기 멱등 가드도 어느 타입에도 배정되지 않았다(`App.xaml.cs`는 *"리소스 딕셔너리 병합용 최소 코드비하인드"*로만 기술) |

---

## §C. 값이 없는 상수 — S2/S3가 명시적으로 요구한 것들

| 상수 | 요구처 |
|---|---|
| **User-Agent 문자열** | S2 §5.8이 *"식별 가능한 고정 문자열"*을 요구 |
| **설정 flush 실패 흔적 파일 이름** | S2 §8.6 — 종료 시 쓰고 기동 시 읽는다. DLD는 **경로 속성만** 명명 |
| **Win32 수치 상수 전부** | A3 |
| **poe.ninja 엔드포인트 URL 템플릿 둘** | `00-api-contract.md` §1에 있으나 DLD 상수표에 **재기술되지 않음** |
| **`ExchangeCategory` 멤버 → `type=` 질의 토큰 사상** | S2 §2.2가 *"열거 멤버 이름이 곧 질의 문자열"*이라 했으나 DLD는 **설정 파일 맥락에서만** 함의 |
| 검색 결과 `Limit` | §6.7 상한 200 |
| `BootWatchdog` 15초 | §18.1이 *"15절에 추가"*라 적었으나 **추가되지 않음** |

---

## §D. 조용한 실패

### D1. 지역화 카탈로그가 자기 폴백 상수와 어긋난다 — **세 리뷰 전부 지적**

§16.2가 **문자 단위 일치**를 단언하는 테스트를 요구하는데, 같은 문서 안에 답이 둘이다:

| 키 | §11.8 | §14.3 |
|---|---|---|
| `PollingStoppedStale` | `"polling delayed. last attempt {0}m ago"` | `"updates are delayed. last attempt {0}"` |
| `RatePendingWithDuration` | `"rate pending {0}m"` | `"rate pending for {0}"` |
| `PollingStoppedExited` | `"polling stopped. restart the app"` | `"updates have stopped. restart the app"` |

**첫 행은 인자 모양까지 다르다.** §18.4의 의사코드가 넘기는 것은 `Pricing.Relative(...)`의 출력, 즉 `"3m ago"`라는 **완성된 구절**이다. §11.8 상수로 렌더하면 **`"polling delayed. last attempt 3m ago ago"`**가 된다 — 사용자가 가격이 얼마나 낡았는지 판단하려는 바로 그 순간에.

**§16.2의 테스트가 이것을 잡지 못한다.** 문서에 답이 둘이므로 구현자가 어느 쪽을 옮겨 적든 통과한다. 폴백 경로는 지역화가 이미 실패했을 때만 도니 QA에도 안 보인다.

**조치** — **§14.3을 정본으로 §11.8을 맞춰라**(호출부 인자 모양이 §14.3과 일치한다). **§14.4의 자기 규칙 위반도 함께 고쳐라** — *"자리표시자가 없는 키는 상수를 두지 않는다"*고 해 놓고 `PollingStoppedExited`·`CommitRejectedBanner`·`RateInheritedFooter`·`ItemDroppedRow`·`ItemUnresolvedRow`에 상수를 뒀다. 규칙을 고치든 상수를 빼든 하나로 정하라.

### D2. `RollingFileSink`가 조용히 로그를 버린다

§4.3: *"상한 초과를 감지하면 가장 오래된 항목을 버리고 유실 전용 `LogEntry`(`Code="LogBufferOverflow"`)를 상한을 무시하고 큐에 넣는다."* **그 항목의 `LogLevel`이 명시돼 있지 않고**, 어떤 조건도 세우지 않는다. 레벨이 Warning 미만이면 §4.2의 최근 오류 링에도 실리지 않는다.

실패 폭풍 — 로그가 가장 필요한 순간 — 에 만 개 버퍼가 차면서 진단 흔적이 조각나는데 배너도 트레이 색도 변하지 않는다. §9.6이 *"로깅 실패는 사용자가 알아야 할 가장 중요한 것"*이라 적어 놓고 **"로거는 도는데 버린다"는 별개 양식은 덮지 못한다.**

**조치** — 유실 항목의 레벨을 **Warning 이상으로 명시**하고, 세션 중 첫 유실 시 조건(기존 `LoggingUnavailable` 재사용 또는 신설)을 세워라.

### D3. `AppConditionKind.FetchFailed`가 고아다

저장 그룹으로 선언됐으나 **생산자도 소비자도 없다.** 실제 실패 목록 표시는 `CategoryStatuses`에서 `DerivedConditions.ClassifyRow(...)`로 파생되며 `Conditions`를 건드리지 않는다. `snapshot.Conditions[FetchFailed]`는 **영원히 부재**한다.

**S4가 `ElementFault`에 대해 스스로 수행한 추적(§19.2)을 자기 자신에게는 적용하지 않았다.**

**조치** — 파생 그룹으로 옮기거나 생산자·소비자와 회귀 테스트를 추가하라. 어느 쪽이든 S2 §2.11 개정 요구가 된다.

---

## §E. 테스트

| # | 문제 |
|---|---|
| E1 | **M7 테스트가 공허하게 통과할 수 있다.** §16.3이 *"M7(사전 1회 구축 계수 단언 포함)"*이라 적었으나 **조회 횟수를 노출하는 곳이 없다** — 조인은 `MarketClient`의 private 경로 안이고 `CategorySnapshot`은 `JoinMissCount`만 갖는다 |
| E2 | **배정되지 않은 S2 테스트 ID 다섯**: `M8` · `M10′` · `M12′` · `M12″` · `S2‴`. 특히 **`S2‴`는 §13.4가 코드를 정의한 `EmptyItemId` 거부**다 |
| E3 | **`tests/…/Diagnostics/` 폴더가 §2.1에 있는데 §16에 Diagnostics 표가 없다** — §4.1의 새 로그 와이어 형식과 D-DG1의 유실 동작이 무검증이다 |
| E4 | `SnapshotFanoutReentrancyTests`가 *"상한 패스 수"*를 단언한다면서 **그 상한을 주지 않는다.** 측정값은 7이다(`00-shell-measurements.md` §10.3) |

---

## §F. 인용·성격 규정 정정

| # | 조치 |
|---|---|
| F1 | **§19.1의 성격 규정이 틀렸다.** *"컴파일되지 않는다"*가 아니라 **컴파일은 되고 잘못된 JSON을 낸다** — `{"id":{"value":"divine"},"category":{"raw":"Currency","known":1}}`에 숫자 열거값. 쓰기 DTO가 필요하다는 **결론은 옳다.** 문장만 정정하라 |
| F2 | **§10.9의 CA2007 근거가 거꾸로다** 【측정】. *"CA2007이 `await using`을 잡지 않으므로"*라 했으나 **`await using`은 잡힌다.** 안 잡히는 것은 **`await foreach` 하나뿐**이다. 결론(수동 부착)은 유지하되 근거를 정정하고, **`await foreach`의 규약 준수는 분석기가 강제하지 않으므로 리뷰에서 잡아야 한다**고 명시하라 — 하필 `Store.ConsumeAsync`와 `RollingFileSink`의 소비 루프가 그 형태다 |
| F3 | **§15.1의 컬러키 비충돌 주장을 잠정으로 표기하라.** 같은 문서 §19.5가 **팔레트 전체를 나중으로 미룬다.** 존재하지 않는 팔레트에 대해 비충돌은 논증될 수 없다 |
| F4 | §18.1이 *"정상 기동은 8~11단계를 밀리초~수백 밀리초 안에 통과"*를 **HLD §3.5**에 귀속시켰다 — 그 문장은 **S3 §3.2**다 |
| F5 | §17 표가 **S3 §14-13을 누락**했다. §2.4가 *"Shell 전용 테스트 프로젝트는 만들지 않는다"*로 함의만 하고 명명하지 않았다 |

---

## §G. 부분 해소된 표식 — 마저 닫아라

| 표식 | 남은 것 |
|---|---|
| S3 §9.3 | `Ui(key, fallbackConst, args)` **3층 헬퍼의 시그니처가 없다.** `PricingEngine.Tmpl`(private, Pricing)만 존재한다. S3 §9.3은 `Presentation` 쪽 헬퍼를 요구했다 |
| S3 §2.2 | 판정 술어는 깨끗하다(§18.4). **문구가 D1의 불일치에 걸려 있다** |
| S3 §14-12 (팔레트·폰트) | 재유예는 정당하나 **결정 주체가 없다**(*"구현 중 또는 S5"*). 폰트 크기는 `HasMinimumVisibleArea(…, Size footerSize)`의 실입력이다 — 최소한 **잠정값**을 주고 실험 후 교체하도록 하라 |
| S3 §10.2 | 허용 목록 추가 절차는 S3가 결정자를 명명했으므로 수용 가능. **핸들러의 소유 타입**은 B6에서 처리 |

---

## §H. 건드리지 말 것 — 리뷰가 확인한 것들

- **API 계약 충실도 완벽**: `[JsonPropertyName]` 전부가 `00-api-contract.md` §2.1 표와 일치, REQUIREMENTS §6의 틀린 이름은 어디에도 없음, **`core.rates`가 타입에 아예 없어** D1의 금지 경로가 구조적으로 도달 불가, `core.items` 조인·A3·A4 전부 반영.
- **측정 충실도 완벽**: S14(센티널 `0x3039` + raw 성공 동시 요구로 `DestroyWindow` 거짓 양성 차단) · S10(`AllowsTransparency=false` + `WS_EX_LAYERED`) · S15(N=5 경계+래치, **`>= N`으로 쓰면 실패하는 회귀 테스트 §16.7까지**) · S12(재적용 코드 없음) · S13(RTB 단언 없음) · S8(`AttachThreadInput` 없음).
- **시그니처 대부분이 0경고로 컴파일**된다 【측정】 — `ItemId`·`CategoryRef`·`EquatableArray<T>`·`AppSettings`·`MarketResult<T>`·`IUiDispatcher`/`UiPostPriority`·`IUiTicker`. **`MarketResult<T>`가 이전 구조체 판의 결함을 실제로 고쳤고** 어떤 호출자도 `!`를 강요받지 않는다.
- **JSON 라운드트립·엄격성 확인** 【측정】 — 실측 응답 형태에서 전 필드 정상 착지, `"lines": null`이 NRE 없이 처리, 문자열/숫자 불일치가 **해당 원소만** 실패, `PropertyNameCaseInsensitive=false` 확인.
- **`MinPrice = 1e-9m`** 【측정】 — 측정된 `OverflowException` 입력을 정확히 배제하면서 `decimal` 범위 안에 넉넉히 든다.
- **§19.2(`FailureKind.ElementFault` 도달 불가)는 독립 추적으로 옳다고 확인**됐다.
- `SettingsWriteDto` **매퍼의 왕복이 전량(total)이고 전용 테스트가 있다**(§16.6) — A2의 `required`만 고치면 된다.
- `ViewModelRefreshFailing` 저장 그룹 수용에 **회귀 테스트 S17이 있다**(§16.4). 이 설계에서 가장 비쌌던 near-miss가 이제 테스트로 지켜진다.

**S2 개정 요구 2건**을 §19에 등재하라 — ① `FailureKind.ElementFault` 제거(§19.2) ② `AppConditionKind.FetchFailed`의 그룹 재배치 또는 생산자 신설(D3).
