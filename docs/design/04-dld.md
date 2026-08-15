# S4 — 상세 저수준 설계 (DLD): 구현 직전 마감

| | |
|---|---|
| 문서 상태 | **제2판** |
| 작성일 | 2026-08-16 (제2판) |
| 상위 문서 | `docs/design/03-lld-shell.md` 제5판(frozen) · `docs/design/02-lld-core.md` 제4판(frozen) · `docs/design/01-hld.md` 개정 7판(frozen, D1–D22) · `docs/design/00-shell-measurements.md`(측정 확정, 구속력 있음) · `docs/design/00-api-contract.md`(측정 확정, 구속력 있음) · `docs/REQUIREMENTS.md` 개정 2판 |
| 범위 | S2/S3가 유예한 전부 — 메서드 시그니처, JSON 속성명, 오류 코드 문자열, 지역화 키 카탈로그, 프로젝트·테스트 배치, 구체 상수. 새 설계 결정은 만들지 않는다 |
| 범위 밖 | 메서드 본문(짧은 의사코드로 시그니처가 모호할 때만 예외), XAML 마크업 자체, 오버레이 색상 팔레트 전체(컬러키 값 하나만 확정) |
| 표기 규약 | S2/S3와 동일. D-DLx = 이 문서의 결정. 측정 = 상위 측정 문서 인용. 확인 = 검증되지 않은 전제 |

---

## 0. 개정 이력

### 0.1 초판

S2 제4판·S3 제5판을 전수 스윕해 "S4로 유예" 표시가 붙은 항목과, 표시는 없으나 시그니처·속성명·상수가 실제로 비어 있는 자리를 모두 채웠다. 상위 문서가 세운 타입·인터페이스·상태기계·불변식은 하나도 바꾸지 않았다 — 이 판이 발견한 결함(§19)은 전부 "상위 문서가 결정하지 않은 자리"이지 "상위 문서가 틀리게 결정한 자리"가 아니다. 유일한 예외는 §19.1(Settings 쓰기 경로의 타입 불일치)로, 이는 S2 §8.4의 문장을 문자 그대로 따르면 **컴파일은 되지만 계약과 다른 잘못된 JSON을 낸다**는 발견이며(F1 정정 — 컴파일 실패가 아니다) 이 문서가 스스로 보강했다.

### 0.2 제2판 — 마감 지시서 반영

리뷰 3종(csharp-reviewer 프로브 빌드·silent-failure-hunter·완성도 감사)을 호출자가 판정해 통합한 마감 지시서(`_wip/s4-punchlist.md`)를 전수 반영했다 — 지적 24건 중 기각 0건. 핵심은 컴파일 차단 넷(§A, `partial` 접근 한정자 충돌 CS0262·`SettingsWriteDto` 계열 CS8618·Shell 타입 오류 셋·`InstanceSignal.TrySend`의 PID 모순)과 값이 비어 있던 상수 일곱(§C)이다. 이 판이 뒤집은 상위 결정은 없다 — §19.2(`FailureKind.ElementFault`)에 이어 §19.8(`AppConditionKind.FetchFailed`)이 이번 판이 새로 발견한 두 번째 S2 개정 요구다. 절 번호는 초판 그대로 유지했다 — 신규 내용은 각 절의 기존 하위 절 뒤에 덧붙이거나(예: §15.9/§15.10, §16.9), 표에 행을 추가하는 방식으로만 실었다.

---

## 1. 표기·규약 재확인

이 문서의 모든 시그니처는 다음을 예외 없이 따른다. 개별 시그니처마다 반복해서 적지 않는다.

| 규약 | 출처 | 적용 |
|---|---|---|
| ConfigureAwait(false) | S2 1.4 | Presentation을 제외한 PoeOverlay.Core의 모든 await(await foreach/await using 포함) |
| ConfigureAwait(false) 면제 | S2 D-C3, S3 D-SH1 | Presentation 폴더와 Shell 프로젝트 전체. Interop의 순수 I/O 지점은 pragma로 개별 재활성 |
| null!/default! 금지 | S2 1.6 | 모든 시그니처. 구조체는 default를 무해한 값으로 취급 |
| 실패는 값 | S2 1.5 | Market/Settings 공개 메서드는 예외가 아니라 MarketResult 계열/SettingsLoadResult 계열을 반환 |
| CancellationToken 위치 | 신규 D-DL1 | 항상 마지막 매개변수, 이름은 ct, 기본값 없음. 순수 함수(Pricing, Domain)는 토큰을 받지 않는다 |
| async 메서드 이름 | 신규 D-DL2 | Async 접미사. IHostedService.StartAsync/StopAsync 등 BCL 계약이 강제하는 이름은 그대로 |
| 컬렉션 반환 | S2 1.6 | IReadOnlyList/IReadOnlyDictionary. 절대 null 아님 |
| 네임스페이스 = 폴더 | S2 1.1, S3 1.1 | 아래 2절의 물리 배치와 1대1 |
| `partial` 선언 헤더 일치 | 신규 D-DL0 | 한 타입이 여러 절에 나뉘어 실리면(§2.1의 규칙) 모든 부분 선언에 `partial`을 붙이고 접근 한정자(`public`/`internal`)·`sealed`를 전부 동일하게 명시한다 — 기반 타입·인터페이스 목록은 한 곳에서만 선언하면 된다 |

---

## 2. 솔루션·프로젝트 레이아웃

### 2.1 솔루션 구조

```
PoeOverlay.sln
src/
  PoeOverlay.Core/PoeOverlay.Core.csproj                 net8.0
    Domain/
      Ids.cs                    ItemId
      CategoryRef.cs
      Enums.cs                  ExchangeCategory, DisplayCurrency, ResolvedCurrency,
                                 ChangeDirection, DisplayState, RequestPriority, PriceForm,
                                 AppConditionKind, HeightMode
      WatchlistEntry.cs
      EquatableArray.cs
      ItemPrice.cs
      SkipCounts.cs
      CategorySnapshot.cs
      CategoryStatus.cs
      DivineRate.cs
      RoundContext.cs
      Heartbeat.cs               RoundTrigger, RoundOutcome, LoopExitKind 포함
      LeagueEntry.cs             LeagueList, LeagueListStatus
      LeagueResolution.cs
      FetchedListing.cs
      ConditionState.cs
      FailureRecord.cs           FailureKind 포함
      ErrorRecord.cs
      MarketSnapshot.cs
      Ports/
        IConditionSink.cs
        IErrorSink.cs
    Diagnostics/
      LogEntry.cs
      LogLineFormatter.cs
      FileLoggerProvider.cs
      FileLogger.cs
      RollingFileSink.cs
      RecentErrorRing.cs
      SessionSuppressionRegistry.cs
      DiagnosticsStartupState.cs         부팅 초기 감지 결과 보관용, 12.2절
    Localization/
      ITemplateSource.cs
      ILocalizer.cs
      LanguageInfo.cs
      LocalizationCatalog.cs
      LocalizationJsonContext.cs
      LanguageTagValidator.cs
      Localization/en.json               EmbeddedResource + 출력 복사
    Pricing/
      PriceTemplates.cs
      NumberFormatter.cs                 Num, Pct
      StalenessPolicy.cs
      PriceDisplay.cs
      ChangeDisplay.cs
      PricingEngine.cs                   Format/Change/Relative/Resolve 정적 메서드
    Market/
      Dtos/
        NinjaOverviewDto.cs
        CoreDto.cs
        CoreItemDto.cs
        LineDto.cs
        SparklineDto.cs
        LeagueDto.cs
      NinjaJsonContext.cs
      MarketResult.cs
      NinjaGateway.cs
      IMarketClient.cs
      MarketClient.cs
    Store/
      DataTag.cs
      StoreCommand.cs                    각 명령 레코드
      ISearchSource.cs
      Store.cs                           IHostedService, IMarketSnapshotSource,
                                          IConditionSink, IErrorSink, ISearchSource
    Polling/
      PollingOptions.cs
      PollingService.cs                  BackgroundService
      PollingTrigger.cs                  RoundTrigger 트리거 채널 레코드
    Settings/
      AppSettings.cs
      WindowSettings.cs
      SettingsWriteDto.cs                신규 D-DL15, 10.6절
      SettingsJsonContext.cs
      SettingsLoadResult.cs
      WriteBlockReason.cs
      SettingsValidation.cs
      SettingsStore.cs                   IHostedLifecycleService, ISettingsSource
    Presentation/
      Fanout/
        UiPostPriority.cs
        IUiDispatcher.cs
        IUiTicker.cs
        IRefreshable.cs
        SnapshotFanout.cs
      ViewModels/
        OverlayViewModel.cs
        SettingsViewModel.cs
        TrayViewModel.cs
        Rows/
          PriceRowViewModel.cs
          BannerViewModel.cs
      Overlay/
        IOverlayModeService.cs
        IOverlayGeometryService.cs
      UiState/
        UiStateTemplates.cs
        DerivedConditions.cs             PollingStopped/RatePending 등 순수 함수

  PoeOverlay/PoeOverlay.csproj                            net8.0-windows, UseWPF+UseWindowsForms
    Composition/
      Program.cs                          STAThread Main
      HostBuilderFactory.cs
      ServiceRegistration.cs
    Interop/
      NativeMethods.cs
      Win32Constants.cs
      ExtendedStyleGate.cs
      MessageOnlyWindowFactory.cs
    Overlay/
      OverlayWindow.xaml, OverlayWindow.xaml.cs
      OverlayModeService.cs
      OverlayGeometryService.cs
      OverlayGeometryValidator.cs
      MoveModeWatchdog.cs
      DisplayChangeWatcher.cs
    Settings/
      SettingsWindow.xaml, SettingsWindow.xaml.cs
    Tray/
      TrayIconHost.cs
      UiDispatcher.cs                     IUiDispatcher 구현
      UiTicker.cs                         IUiTicker 구현
    Startup/
      SingleInstanceGuard.cs
      InstanceSignal.cs
      FirstRunGate.cs
    App.xaml.cs                            리소스 딕셔너리 코드 병합용 최소 코드비하인드, 3.2절

tests/
  PoeOverlay.Core.Tests/PoeOverlay.Core.Tests.csproj      net8.0, PoeOverlay.Core만 참조
    Pricing/, Localization/, Market/, Store/, Polling/, Settings/, Diagnostics/, Presentation/
    (파일 배치는 16절)
```

폴더 = 네임스페이스는 S2/S3의 규약 그대로다. S2 10.6은 ISearchSource를 "S3가 소비하는 경계"로 Store 절에서 소개했지만 실제 소유 모듈을 명시하지 않았다 — 이 문서가 확정한다: ISearchSource는 PoeOverlay.Core.Store 네임스페이스에 선언하고 Store가 구현한다(3.1절 6번 행, "다섯 얼굴"의 다섯 번째와 일치).

**신규 D-DL0.** `Store`·`PollingService`·`SettingsStore`·`SnapshotFanout` 넷은 이 문서 여러 절에 걸쳐 선언이 나뉜다(예: `Store`는 8.3/8.4/8.5절). C#의 `partial` 규칙상 한 부분이라도 `partial`을 빠뜨리거나 접근 한정자가 다르면 CS0262다 — 이 문서는 넷 모두를 `public sealed partial`로 통일한다(멤버 개별 접근 한정자는 각 절이 명시한 대로 `private`/`internal`일 수 있다 — 통일되는 것은 **클래스 헤더**뿐이다). 아래 각 절의 코드 블록은 이미 이 규칙을 반영했다.

### 2.2 Directory.Build.props

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <WarningsAsErrors>Nullable;CA2007;CA1031</WarningsAsErrors>
    <AnalysisLevel>latest</AnalysisLevel>
    <NoWarn>$(NoWarn);WFAC010</NoWarn>
  </PropertyGroup>
</Project>
```

신규 D-DL3. CA2007과 CA1031은 net8 기본 비활성(S2 1.4/9.5 측정)이므로 WarningsAsErrors만으로는 아무것도 강제되지 않는다 — 먼저 켜야 승격될 경고가 생긴다. 루트 .editorconfig에 다음을 둔다.

```ini
[*.cs]
dotnet_diagnostic.CA2007.severity = warning
dotnet_diagnostic.CA1031.severity = warning
```

NoWarn의 WFAC010은 net8.0(비 Windows) 프로젝트에는 의미가 없는 경고이지만, Directory.Build.props가 두 TFM에 공통 적용되므로 여기 둔다 — PoeOverlay.Core에서는 무해하게 무시된다.

신규 D-DL0-1. `src/PoeOverlay.Core/PoeOverlay.Core.csproj`에 다음을 둔다.

```xml
<ItemGroup>
  <InternalsVisibleTo Include="PoeOverlay.Core.Tests" />
</ItemGroup>
```
16절의 다수 테스트가 `internal` 와이어 DTO(7.1절)·`JsonSerializerContext`(7.2절)·설정 쓰기 DTO(10.7절)를 직접 참조하고, 16.3절(M7)이 요구하는 조인 계수 훅(15.10절 아래 §16.3 보강 참조)도 `internal`이다 — 이것 없이는 테스트 프로젝트 자체가 컴파일되지 않는다.

### 2.3 CA2007 면제 스코프 — .editorconfig 배치

| 파일 | 내용 | 근거 |
|---|---|---|
| src/PoeOverlay.Core/Presentation/.editorconfig | CA2007 severity = none, 사유 주석: async 명령은 UI 스레드 복귀를 전제한다(S2 D-C3) | S2 D-C3 |
| src/PoeOverlay/.editorconfig | CA2007 severity = none, 사유 주석: Shell의 이벤트 핸들러 전반이 UI 스레드 복귀를 전제한다(S3 D-SH1) | S3 D-SH1 |
| src/PoeOverlay/Interop 개별 파일 내부 | 순수 I/O 지점을 pragma warning restore CA2007로 개별 재활성 후 그 지점 직후 다시 disable | S3 1.4 |

확인. 구현 착수 시점까지 Interop에 실제로 "UI 스레드 재개가 불필요한 순수 I/O" 지점이 하나도 식별되지 않았다(12.2절이 부팅 초기 진단 보관을 Composition/Program.cs의 동기 코드로 확정했기 때문). 세 번째 행은 현재 빈 집합이다 — 그런 지점이 구현 중 실제로 생기면 그때 개별 재활성한다.

### 2.4 테스트 프로젝트

tests/PoeOverlay.Core.Tests(net8.0, xunit + xunit.runner.visualstudio + Microsoft.Extensions.TimeProvider.Testing의 FakeTimeProvider)만 둔다. Shell 전용 테스트 프로젝트는 만들지 않는다 — S2 11절 "S3 로직은 테스트하지 않는다. Core 전용 테스트 프로젝트가 도달할 수 없다"를 그대로 받아들인다(S3 13-31이 재확인). Presentation은 net8.0이므로 이 프로젝트가 도달 가능하고, 9.2절(파생 조건 순수 함수)과 뷰모델 로직 중 UI 비의존 부분은 여기서 검증한다(16절).


## 3. Domain — 시그니처 확정

S2 2절이 준 코드는 이미 시그니처에 가깝다. 이 절은 그것을 컴파일 가능한 최종형으로 굳히고, S2가 의사코드로 남긴 지점(TryCreate 본문 등)만 짧게 채운다. 전부 PoeOverlay.Core.Domain 네임스페이스.

### 3.1 ItemId

```
public readonly record struct ItemId
{
    public ItemId(string Value);
    public string Value { get; }
    public override string ToString();                         // Value ?? string.Empty
    public bool IsEmpty { get; }                                // string.IsNullOrWhiteSpace(Value)
    public static bool TryCreate(string? raw, out ItemId id);
}
```
TryCreate: `id = new ItemId(raw ?? string.Empty)`; 반환값은 `!id.IsEmpty`. 정규화(Trim 등)하지 않는다(S2 2.1).

### 3.2 CategoryRef

```
public readonly record struct CategoryRef(string Raw, ExchangeCategory? Known)
{
    public bool IsUnresolved { get; }                           // Known is null
}
```

### 3.3 열거형 (Enums.cs)

```
public enum ExchangeCategory : int
{
    Currency = 1, Fragment = 2, Runegraft = 3, AllflameEmber = 4, Tattoo = 5, Omen = 6,
    DjinnCoin = 7, Ducat = 8, EnshroudingCrystal = 9, DivinationCard = 10, Artifact = 11,
    Oil = 12, DeliriumOrb = 13, Scarab = 14, Astrolabe = 15, Fossil = 16, Resonator = 17,
    Essence = 18
}
public enum DisplayCurrency  { Auto, Chaos, Divine }
public enum ResolvedCurrency { Chaos, Divine }
public enum ChangeDirection  { Up, Down, Flat, Unknown }
public enum DisplayState     { Loading, Ready, Failed }
public enum RequestPriority  { Polling, UserInitiated }
public enum PriceForm
{
    ChaosOnly, ChaosWithDivine, ChaosReciprocal, DivineOnly,
    DivineReciprocal, ChaosRatePending, RatePending, Unavailable
}
public enum HeightMode { Auto, Explicit }
public enum AppConditionKind
{
    // 저장되는 것 — Store.SetCondition이 받아들인다 (6.4절)
    LeagueUnresolved, CommitRejected,
    SettingsWriteFailed, SettingsCorrupt, SettingsReadOnly, SettingsUnreadable,
    TrayUnavailable, LoggingUnavailable, ViewModelRefreshFailing,
    // 저장되지 않는 것 — 표시 시점 파생. Store는 이 멤버로 SetCondition을 받으면 거부한다
    // 【S2 제5판 반영】 FetchFailed가 저장 그룹에서 여기로 옮겨졌다(§19.8) —
    // 생산자·소비자가 없었고 실제 표시는 S2 §10.5가 CategoryStatuses에서 파생한다
    FetchFailed, RatePending, RateInherited, PollingStopped, ItemUnresolved, ItemDropped
}
```
값(정수)은 로그에 남는 결정적 순서이므로 재배열 금지(S2 2.2). AppConditionKind의 두 그룹 순서는 S2 2.11 그대로 — ViewModelRefreshFailing이 저장 그룹의 마지막(LoggingUnavailable 다음)이다(S3 13-28).

### 3.4 WatchlistEntry, EquatableArray

```
public sealed record WatchlistEntry(ItemId Id, CategoryRef Category, DisplayCurrency? DisplayCurrency);

public sealed class EquatableArray<T> : IReadOnlyList<T>, IEquatable<EquatableArray<T>>
    where T : IEquatable<T>
{
    public EquatableArray(IEnumerable<T> items);                // 생성 시 배열로 복사
    public int Count { get; }
    public T this[int index] { get; }
    public IEnumerator<T> GetEnumerator();
    public bool Equals(EquatableArray<T>? other);
    public override bool Equals(object? obj);
    public override int GetHashCode();                          // 1회 계산 후 캐시
    public static bool operator ==(EquatableArray<T>? left, EquatableArray<T>? right);
    public static bool operator !=(EquatableArray<T>? left, EquatableArray<T>? right);
}
```

### 3.5 ItemPrice / SkipCounts / CategorySnapshot / CategoryStatus

```
public sealed record ItemPrice(
    ItemId Id, string? ApiName, decimal PrimaryValue, double? VolumePrimaryValue,
    string? MaxVolumeCurrency, decimal? MaxVolumeRate, double? TotalChangePercent,
    ExchangeCategory? SelfReportedCategory);

public readonly record struct SkipCounts(int BlankId, int NonPositiveValue, int Duplicate, int ElementFault)
{
    public int Total { get; }                                   // 네 필드의 합
}

public sealed record CategorySnapshot(
    ExchangeCategory Category, IReadOnlyDictionary<ItemId, ItemPrice> Items,
    decimal MedianPrimaryValue, DateTimeOffset FetchedAt, string League, int DataEpoch,
    int RawLineCount, SkipCounts Skips, IReadOnlyList<ItemId> SkippedIds,
    bool SkippedIdsTruncated, int JoinMissCount, bool ValidationBypassed);

public sealed record CategoryStatus(
    ExchangeCategory Category, int ConsecutiveFailures, DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt, DateTimeOffset? CooldownUntil, FailureRecord? LastFailure,
    int ConsecutiveMedianJumps, DateTimeOffset? LastForcedAcceptAt, bool NeverNonEmpty);
```
`Items`는 `FrozenDictionary<ItemId, ItemPrice>`를 구체 타입으로 생성하되 필드 선언은 `IReadOnlyDictionary<ItemId, ItemPrice>`다(S2 2.6 "동결").

### 3.6 DivineRate / RoundContext / Heartbeat

```
public sealed record DivineRate(decimal ChaosPerDivine, DateTimeOffset AcquiredAt, string League, bool Inherited);

public sealed record RoundContext(string League, int DataEpoch, int RoundGeneration, int RoundNumber, DateTimeOffset StartedAt);

public enum RoundTrigger { Startup, Scheduled, Repoll, LeagueChanged }
public enum RoundOutcome { Completed, PartiallyFailed, AllFailed, LeagueUnresolved, Canceled }
public enum LoopExitKind  { Canceled, Faulted }

public sealed record Heartbeat(
    DateTimeOffset? LastRoundAttemptAt, int LastRoundNumber, DateTimeOffset? LastRoundCompletedAt,
    RoundOutcome? LastOutcome, bool LoopExited, LoopExitKind? ExitKind, DateTimeOffset? ExitedAt);
```


### 3.7 League / FetchedListing

```
public sealed record LeagueEntry(string Id, string Name);
public enum LeagueListStatus { Ok, Suspicious, Failed }
public sealed record LeagueList(
    IReadOnlyList<LeagueEntry> Entries, DateTimeOffset FetchedAt,
    LeagueListStatus Status, string? FailureCode);

public enum LeagueResolutionState { Pending, Resolved, Unresolved }
public sealed record LeagueResolution(LeagueResolutionState State, string? League, string? ReasonCode);

public sealed record FetchedListing(
    IReadOnlyDictionary<ExchangeCategory, CategorySnapshot> ByCategory, string League, int DataEpoch);
```

### 3.8 ConditionState / FailureRecord / ErrorRecord / MarketSnapshot

```
public sealed record ConditionState(bool Active, DateTimeOffset Since, string? Detail);

public enum FailureKind
{
    // 【S2 제5판 반영】 ElementFault가 제거됐다(§19.2) — 원소 단위 결함은
    // SkipCounts.ElementFault(카운터)로만 계상되고 Kind로 생산되는 자리가 없었다
    Network, Timeout, HttpStatus, RateLimited, Deserialization,
    EmptyLines, NoPricedLines, FieldMissingRatio,
    PrimaryCurrencyMismatch, DivineLineMissing, MedianJump,
    LeagueListInvalid, MappingFault
}
public sealed record FailureRecord(
    FailureKind Kind, string Code, DateTimeOffset At,
    int? HttpStatus, string? Detail, string? ExceptionType);

public sealed record ErrorRecord(
    DateTimeOffset At, string Module, string Code, string MessageKey,
    string? Detail, string? Category, string? League, int? RoundNumber, string? ExceptionType);

public sealed record MarketSnapshot(
    IReadOnlyDictionary<ExchangeCategory, CategorySnapshot> Categories,
    DivineRate? Rate, LeagueList? Leagues, FetchedListing? Listing, Heartbeat Heartbeat,
    ErrorRecord? LastError,
    IReadOnlyDictionary<ExchangeCategory, CategoryStatus> CategoryStatuses,
    LeagueResolution LeagueResolution,
    IReadOnlyDictionary<AppConditionKind, ConditionState> Conditions,
    string? DataLeague, int DataEpoch, long Version,
    int RejectedCommitCount, int ConsecutiveEmptyCommitRounds);
```
`FailureRecord.Code`/`ErrorRecord.Code`의 정확한 리터럴 카탈로그는 13절. `ErrorRecord.MessageKey`의 `ui.error.*` 카탈로그는 14.5절. `Module` 필드값은 정확히 `"Polling"`/`"Market"`/`"Settings"`/`"Store"`/`"Shell"` 다섯 리터럴 중 하나다(S2 2.12 그대로, 대소문자 고정).

### 3.9 Ports (Domain/Ports)

```
namespace PoeOverlay.Core.Domain.Ports;

public interface IConditionSink
{
    void Set(AppConditionKind kind, bool active, string? detail);
}
public interface IErrorSink
{
    void Report(ErrorRecord error);
}
```
둘 다 동기, 즉시 반환(S2 2.13) — `Task`를 반환하지 않는다. 내부적으로 `Channel<StoreCommand>.Writer.TryWrite`를 호출할 뿐이다.


## 4. Diagnostics — 시그니처 확정

네임스페이스 PoeOverlay.Core.Diagnostics. Domain을 참조하지 않으므로(S2 1.2) 전부 원시 타입.

### 4.1 LogEntry와 로그 줄 형식

```
public sealed record LogEntry(
    DateTimeOffset At, LogLevel Level, string Module, string Message,
    string? League, int? DataEpoch, int? RoundNumber, string? Category,
    string? Code, string? ExceptionType);
```

신규 D-DL4. 줄 형식을 문자 단위로 고정한다(S2 9.1 "한 항목 = 한 줄, 고정 폭 접두 + key=value 꼬리"의 정확한 모양).

```
{At:yyyy-MM-ddTHH:mm:ss.fffZ} [{LevelTag}] {Module,-10} {key=value ...}msg="{EscapedMessage}"
```
LevelTag는 LogLevel을 3자 고정폭 대문자로 사상한다: Trace=TRC, Debug=DBG, Information=INF, Warning=WRN, Error=ERR, Critical=CRT. key=value는 null이 아닌 필드만, 다음 순서로: league= dataEpoch= round= category= code= exceptionType=. 값에 공백이 있으면 큰따옴표로 감싼다. 개행(\n, \r)과 큰따옴표는 이스케이프한다(\n, \r, \\"). 스택 트레이스는 Error 이상에서만 Message 뒤에 별도 key stack="..."로 덧붙인다(S2 9.1).

```
public interface ILogLineFormatter
{
    string Format(LogEntry entry);
}
public sealed class LogLineFormatter : ILogLineFormatter
{
    public string Format(LogEntry entry);
}
```

### 4.2 파일 로거 — ILogger 위의 얇은 계층

S2 9.1의 결정대로 Microsoft.Extensions.Logging.ILogger 위에 자체 프로바이더를 얹는다. 시그니처는 표준 ILoggerProvider/ILogger 계약을 그대로 구현한다 — 신규 인터페이스를 만들지 않는다.

```
public sealed class FileLoggerProvider : ILoggerProvider
{
    public FileLoggerProvider(RollingFileSink sink, RecentErrorRing ring, TimeProvider timeProvider);
    public ILogger CreateLogger(string categoryName);
    public void Dispose();
}
public sealed class FileLogger : ILogger
{
    internal FileLogger(string categoryName, RollingFileSink sink, RecentErrorRing ring, TimeProvider timeProvider);
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull;   // Module/League/RoundNumber 스코프(S2 9.1)
    public bool IsEnabled(LogLevel logLevel);
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter);
}
```
`Log`는 LogEntry를 구성해 RollingFileSink.Enqueue로 넘기고, Level이 Warning 이상이면 RecentErrorRing.Add도 호출한다(S2 9.3 "링의 소유는 Diagnostics").

### 4.3 RollingFileSink

```
public sealed class RollingFileSink : IAsyncDisposable
{
    public RollingFileSink(string directory, ILogLineFormatter formatter, TimeProvider timeProvider);
    public void Enqueue(LogEntry entry);                        // 동기, Channel.Writer.TryWrite
    public Task StartAsync(CancellationToken ct);                // 스레드풀 소비 태스크 기동(전용 스레드 없음, S2 9.2)
    public Task FlushAsync(CancellationToken ct);                // 채널 완료 -> 배수 -> Flush(true)
    public ValueTask DisposeAsync();
}
```
버퍼 상한·롤 규칙·보존 기간은 15절 상수표. 포화 시 유실 통지는 다음 항목이 아니라 그 항목 자신으로 즉시 큐잉한다(S2 D-DG1) — Enqueue 내부에서 상한 초과를 감지하면 가장 오래된 항목을 버리고 유실 전용 LogEntry(Code="LogBufferOverflow")를 상한을 무시하고 큐에 넣는다.

**D2 — 유실 항목의 레벨과 세션 1회 조건.** 초판은 이 `LogEntry`의 `LogLevel`을 명시하지 않았다 — Warning 미만이면 §4.2의 `RecentErrorRing`(Warning 이상만 담는다)에도 실리지 않아, 로그가 가장 필요한 실패 폭풍 순간에 진단 흔적이 조각나도 배너도 트레이 색도 바뀌지 않는다(S2 §9.6 "로깅 실패는 사용자가 알아야 할 가장 중요한 것"). **결정**: 유실 통지 `LogEntry`의 `LogLevel`은 **`LogLevel.Warning`**으로 고정한다(파일 자체가 쓰이는 중이므로 `LoggingUnavailable`은 아니다 — 로거는 돌고 있고 버릴 뿐이다). 세션 중 첫 유실 시에만 `SessionSuppressionRegistry`의 `loc.*`/`market.*`/`settings.*` 채널과 같은 패턴으로 `diagnostics.bufferOverflow` 채널(14.8절에 행 추가)을 `ShouldReport`로 검사해 최근 오류 링 노출을 세션당 1회로 억제한다 — 이후의 `LogBufferOverflow` 항목은 계속 큐잉되고 파일에는 전부 남지만, `RecentErrorRing` 스냅샷을 반복 오염시키지 않는다.

### 4.4 RecentErrorRing

```
public sealed class RecentErrorRing
{
    public RecentErrorRing(int capacity = 64);                  // 15절 상수
    public void Add(LogEntry entry);                            // Warning 이상만 호출자가 필터링 후 전달
    public IReadOnlyList<LogEntry> Snapshot();                  // 복사본
}
```

### 4.5 SessionSuppressionRegistry — 세션 1회 억제 채널

```
public sealed class SessionSuppressionRegistry
{
    public SessionSuppressionRegistry(ILogger logger, int perChannelCapacity = 512);   // 15절 상수
    public bool ShouldReport(string channel, string suppressionKey);   // true면 이번이 처음이므로 기록해야 한다
    public void ReportChannelSaturated(string channel);                // 상한 도달 시 채널당 1회
    public IReadOnlyDictionary<string, int> DumpTotals();               // 종료 시 전 채널 총계
}
```
`channel` 리터럴 목록과 `ShouldReport`의 억제 키 조합은 9.4절(지역화 키 카탈로그와 별개로 §14.6에 정리)에 있다. 종료 경로(Composition/Program.cs 12-f 직전)가 `DumpTotals()`를 호출해 Info 레벨로 한 줄씩 남긴다.

### 4.6 DiagnosticsStartupState

```
public sealed class DiagnosticsStartupState
{
    public bool LoggerOpenFailed { get; init; }
    public string? SettingsFlushFailureTracePath { get; init; }   // null이면 흔적 파일 없음
}
```
Composition/Program.cs가 1번(로거 오픈)과 5번(Settings 로드) 단계에서 이 값을 지역 변수로 채우고, Store.StartAsync 완료 직후 §3.1(P5)의 공통 기제로 1회 반영한다(§12.2).


## 5. Localization — 시그니처 확정

네임스페이스 PoeOverlay.Core.Localization. Domain(ItemId)과 Diagnostics만 참조(D-C1).

### 5.1 공개 표면

```
public interface ITemplateSource
{
    bool TryGetTemplate(string key, out string template);
}
public interface ILocalizer : ITemplateSource
{
    string Ui(string key, params string[] args);
    string ItemName(ItemId id, string? apiName);
    IReadOnlyList<LanguageInfo> Languages { get; }
    string CurrentLanguage { get; }
    void SetLanguage(string tag);                 // UI 스레드 전용, Debug 단언
    event EventHandler? LanguageChanged;           // 게시 후 발생
}
public sealed record LanguageInfo(string Tag, string DisplayName);
```

### 5.2 구현과 로드 경로

```
public sealed class LocalizationCatalog : ILocalizer, IHostedLifecycleService
{
    public LocalizationCatalog(string baseDirectory, ILogger<LocalizationCatalog> logger,
        SessionSuppressionRegistry suppression);

    public Task StartingAsync(CancellationToken ct);   // D-L1: 전 사전 로드 + 3.7 자리표시자 검증
    public Task StartAsync(CancellationToken ct);      // no-op
    public Task StartedAsync(CancellationToken ct);    // no-op
    public Task StopAsync(CancellationToken ct);       // no-op
    public Task StoppingAsync(CancellationToken ct);   // no-op
    public Task StoppedAsync(CancellationToken ct);    // no-op

    public bool TryGetTemplate(string key, out string template);
    public string Ui(string key, params string[] args);
    public string ItemName(ItemId id, string? apiName);
    public IReadOnlyList<LanguageInfo> Languages { get; }
    public string CurrentLanguage { get; }
    public void SetLanguage(string tag);
    public event EventHandler? LanguageChanged;
}
```
`baseDirectory`는 `{AppContext.BaseDirectory}/Localization/`(S2 3.2)을 Composition이 조립해 주입한다 — Localization 프로젝트 자신은 AppContext를 직접 읽지 않고 문자열을 받는다(테스트에서 임의 디렉터리를 넣을 수 있어야 하므로).

### 5.3 발견·검증 헬퍼

```
internal static class LanguageTagValidator
{
    public static bool IsValid(string fileStem);   // 정규식 S2 3.2, 확장판
}
```
정규식: `^[a-z]{2,3}(-[A-Z][a-z]{3})?(-[A-Z]{2})?$`(S2 3.2, zh-Hans 등 문자 하위태그 포함하도록 확장된 판).

### 5.4 LocalizationJsonContext

사전 파일(en.json 등)은 `Dictionary<string, string>` 평면 JSON이다 — "엄격"이 아니라 "관대"(S2 1.7 표)이므로 다음 옵션으로 소스 생성한다.

```
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = false,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip)]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class LocalizationJsonContext : JsonSerializerContext { }
```
평면 `Dictionary<string,string>`이므로 속성 매핑이 필요 없다 — 키가 곧 JSON 속성명이다. 파싱 실패(JsonException)는 그 언어 파일만 탈락시키고 Warning(S2 3.2).


## 6. Pricing — 시그니처 확정

네임스페이스 PoeOverlay.Core.Pricing. 상태 없음, TimeProvider 없음, 예외 없음(S2 4절). 전부 정적 클래스.

### 6.1 PricingEngine

```
public static class PricingEngine
{
    public static ResolvedCurrency Resolve(DisplayCurrency? entryPref, DisplayCurrency globalDefault, string? token);

    public static PriceDisplay Format(
        ItemPrice price, DivineRate? rate, ResolvedCurrency display,
        DateTimeOffset now, TimeSpan rateMaxAge, ITemplateSource templates);

    public static ChangeDisplay Change(double? totalChangePercent, ITemplateSource templates);

    public static string Relative(DateTimeOffset at, DateTimeOffset now, ITemplateSource templates);
}
public sealed record PriceDisplay(PriceForm Form, string Text, DateTimeOffset EffectiveAsOf, bool RateInherited);
public sealed record ChangeDisplay(ChangeDirection Direction, string Glyph, string Text);
```
`ITemplateSource`는 `Localization`이 구현한다(D-L4). `Pricing`은 그 인터페이스만 참조하며 `Localization`의 구체 타입을 모른다(S2 1.2 허용 의존 표 그대로).

### 6.2 NumberFormatter

```
internal static class NumberFormatter
{
    public const decimal MinPrice = 1e-9m;                       // D-PR8, 15절

    public static string Num(decimal x);                         // 정의역 [1, ∞), 4.3.1 하한 이탈 시 3자리 폴백
    public static string Pct(double x);                           // Math.Round(Abs(x), 1, AwayFromZero).ToString("N1", Invariant)
}
```
`Num`의 대역표·반올림 규칙은 S2 4.3.2/4.3.3 그대로(4.3.4 InvariantCulture 고정). 시그니처만 이 절에서 확정한다 — 알고리즘은 이미 S2가 완전히 정의했다.

### 6.3 PriceTemplates / StalenessPolicy

```
internal static class PriceTemplates
{
    public const string Chaos            = "{0}c";
    public const string Divine           = "{0}d";
    public const string ChaosWithDivine  = "{0}c ({1}d)";
    public const string ChaosRatePending = "{0}c (rate pending)";
    public const string PerChaos         = "{0} per 1c";
    public const string PerDivine        = "{0} per 1d";
    public const string RatePending      = "rate pending";
    public const string Unavailable      = "\u2014";              // em dash, 14.2절과 문자 단위 일치
    public const string Change           = "{0}{1}%";
    public const string JustNow          = "just now";
    public const string SecondsAgo       = "{0}s ago";
    public const string MinutesAgo       = "{0}m ago";
    public const string HoursAgo         = "{0}h ago";
    public const string DaysAgo          = "{0}d ago";
}

public static class StalenessPolicy
{
    public static TimeSpan RateMaxAge(int refreshIntervalMinutes);         // max(30분, 3 x interval)
    public static TimeSpan RowStaleAfter(int refreshIntervalMinutes);      // 2 x interval
    public static TimeSpan HeartbeatStaleAfter(int refreshIntervalMinutes);// 2 x interval + 1분
}
```
`Tmpl`(내부 서식 헬퍼, S2 4.6.2의 세 층)은 공개 표면이 아니므로 별도 파일 없이 `PricingEngine` 내부 `private static string Tmpl(ITemplateSource templates, string key, string fallbackConst, params string[] args)`로 둔다 — 시그니처만 여기 명시하고 구현 파일 목록에는 올리지 않는다.


## 7. Market — DTO의 JSON 속성명과 시그니처 확정

네임스페이스 PoeOverlay.Core.Market(공개), PoeOverlay.Core.Market.Dtos(internal, 경계를 넘지 않는다 — S2 5.2).

### 7.1 와이어 DTO — JsonPropertyName 확정

00-api-contract.md 2절의 필드 대응표가 정본이다. 전부 프로퍼티(필드 아님, System.Text.Json이 IncludeFields 없이 필드를 무시하므로 — S2 5.2 측정).

```
internal sealed class NinjaOverviewDto
{
    [JsonPropertyName("core")]  public CoreDto? Core { get; init; }
    [JsonPropertyName("lines")] public JsonElement[]? Lines { get; init; }
}
internal sealed class CoreDto
{
    [JsonPropertyName("primary")]   public string? Primary { get; init; }
    [JsonPropertyName("secondary")] public string? Secondary { get; init; }
    [JsonPropertyName("items")]     public CoreItemDto[]? Items { get; init; }
}
internal sealed class CoreItemDto
{
    [JsonPropertyName("id")]        public string? Id { get; init; }
    [JsonPropertyName("name")]      public string? Name { get; init; }
    [JsonPropertyName("image")]     public string? Image { get; init; }
    [JsonPropertyName("category")]  public string? Category { get; init; }
    [JsonPropertyName("detailsId")] public string? DetailsId { get; init; }
}
internal sealed class LineDto
{
    [JsonPropertyName("id")]                 public string? Id { get; init; }
    [JsonPropertyName("primaryValue")]       public decimal? PrimaryValue { get; init; }
    [JsonPropertyName("volumePrimaryValue")] public double? VolumePrimaryValue { get; init; }
    [JsonPropertyName("maxVolumeCurrency")]  public string? MaxVolumeCurrency { get; init; }
    [JsonPropertyName("maxVolumeRate")]      public decimal? MaxVolumeRate { get; init; }
    [JsonPropertyName("sparkline")]          public SparklineDto? Sparkline { get; init; }
}
internal sealed class SparklineDto
{
    [JsonPropertyName("totalChange")] public double? TotalChange { get; init; }
    [JsonPropertyName("data")]        public double[]? Data { get; init; }
}
internal sealed class LeagueDto
{
    [JsonPropertyName("id")]   public string? Id { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
}
```
`core.rates`는 타입 자체에 없다(D-MK1) — CoreDto에 그 프로퍼티를 선언하지 않는다. `NinjaOverviewDto.Lines`가 JsonElement[]인 것이 D-MK2(원소별 역직렬화)의 타입 표현이다.

### 7.2 NinjaJsonContext — 소스 생성 옵션 확정

```
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = false,
    NumberHandling = JsonNumberHandling.Strict,
    AllowTrailingCommas = false,
    ReadCommentHandling = JsonCommentHandling.Disallow,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(NinjaOverviewDto))]
[JsonSerializable(typeof(LineDto))]
[JsonSerializable(typeof(LeagueDto[]))]
internal sealed partial class NinjaJsonContext : JsonSerializerContext { }
```
다섯 옵션은 S2 5.3의 표와 문자 단위로 일치해야 한다 — M22(11.7절 테스트)가 생성된 `NinjaJsonContext.Default.Options`를 리플렉션 없이 직접 단언한다(각 옵션은 JsonSerializerOptions의 공개 속성이므로 소스 생성 컨텍스트의 `Options` 인스턴스에서 바로 읽을 수 있다). `NinjaOverviewDto` 파싱 1회, `Lines`의 원소는 `JsonSerializer.Deserialize(el, NinjaJsonContext.Default.LineDto)`로 개별 파싱, `LeagueDto[]` 파싱 1회 — 셋만 소스 생성 대상이면 충분하다(CoreDto/CoreItemDto/SparklineDto는 NinjaOverviewDto/LineDto의 그래프에 포함되므로 소스 생성기가 자동으로 커버한다).

### 7.3 MarketResult

```
public abstract record MarketResult<T>
{
    private MarketResult() { }
    public sealed record Ok(T Value) : MarketResult<T>;
    public sealed record Fail(FailureRecord Why) : MarketResult<T>;
}
```

### 7.4 공개 표면 — IMarketClient / MarketClient / NinjaGateway

```
public interface IMarketClient
{
    Task<MarketResult<CategorySnapshot>> FetchCategoryAsync(
        string league, ExchangeCategory category, RequestPriority priority, CancellationToken ct);

    Task<MarketResult<LeagueList>> FetchLeaguesAsync(RequestPriority priority, CancellationToken ct);
}

public sealed class MarketClient : IMarketClient
{
    public MarketClient(IHttpClientFactory httpClientFactory, NinjaGateway gateway,
        TimeProvider timeProvider, ILogger<MarketClient> logger);

    public Task<MarketResult<CategorySnapshot>> FetchCategoryAsync(
        string league, ExchangeCategory category, RequestPriority priority, CancellationToken ct);

    public Task<MarketResult<LeagueList>> FetchLeaguesAsync(RequestPriority priority, CancellationToken ct);

    internal int JoinDictionaryBuildCount { get; private set; }   // E1, 신규 — 테스트 전용 계수 훅. 조인용 사전을 구성할 때마다 증가
}
```
`FetchCategoryAsync`/`FetchLeaguesAsync`가 D-MK4(5.10절)의 경계 catch를 소유한다 — 이 두 메서드가 곧 "카테고리·리그 진입점"이다. `FetchedAt`은 내부에서 `timeProvider.GetUtcNow()`를 매핑 완료 시점에 1회 호출한다(S2 5.1).

```
public sealed class NinjaGateway
{
    public NinjaGateway(TimeProvider timeProvider);

    public Task<HttpResponseMessage> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        RequestPriority priority, CancellationToken ct);
}
```
동시성 상한·최소 간격·우선순위 큐·기아 방지 승격은 15절 상수를 그대로 쓴다. `send`는 호출자(MarketClient)가 만든 `HttpClient.SendAsync` 델리게이트다 — `NinjaGateway`는 HTTP를 직접 모른다(S2 5.7 "입장 제어만 하고 HTTP를 부르지 않는다").

**신규 D-DL23 (§C) — 엔드포인트 URL 템플릿과 카테고리 질의 토큰.** `00-api-contract.md` §1【측정】이 실측한 그대로 두 템플릿을 확정한다(15.3절에도 상수로 싣는다).

| 호출 | URL 템플릿 |
|---|---|
| 리그 목록 | `GET https://poe.ninja/poe1/api/economy/leagues` |
| 카테고리 개요 | `GET https://poe.ninja/poe1/api/economy/exchange/current/overview?league={league}&type={category}` |

`{category}`는 `ExchangeCategory` 값의 `.ToString()`이다 — S2 §2.2 "열거 멤버 이름이 곧 `type=` 질의 문자열이다. 별도 매핑표를 두지 않는다"를 그대로 따른다(예: `ExchangeCategory.DivinationCard.ToString() == "DivinationCard"`). 초판은 이 규칙을 10.2절(설정 파일 맥락)에서만 언급해 `MarketClient`가 실제로 이 문자열을 만드는 자리(여기)에는 재기술되지 않았다 — 이 문단이 그 자리를 채운다. 새 매핑표를 만들지 않는 것 자체가 S2의 결정이므로 이 문서는 그 결정을 뒤집지 않는다.


## 8. Store — 시그니처 확정

네임스페이스 PoeOverlay.Core.Store. 허용 의존은 Domain, Diagnostics뿐(S2 6.1).

### 8.1 IMarketSnapshotSource / ISearchSource (경계 인터페이스)

```
public interface IMarketSnapshotSource
{
    MarketSnapshot Current { get; }              // 접근자 안에 Volatile.Read
    event EventHandler? SnapshotChanged;          // 신호만, 데이터 없음
}

public enum SearchOutcome { Found, NotInCache, CacheEmpty }
public enum SearchSource  { RoundCommitted, UserFetched }
public sealed record SearchHit(ItemId Id, string? ApiName, ExchangeCategory Category,
    SearchSource Source, decimal PrimaryValue, DateTimeOffset FetchedAt);
public sealed record SearchResult(IReadOnlyList<SearchHit> Hits, SearchOutcome Outcome,
    IReadOnlyList<ExchangeCategory> UnfetchedCategories, bool Truncated);
public sealed record SearchOptions(int Limit, Func<ItemId, string?, bool>? ExtraMatch);

public interface ISearchSource
{
    SearchResult Search(string query, SearchOptions options);
}
```

### 8.2 DataTag / StoreCommand

```
public readonly record struct DataTag(string League, int DataEpoch);

public abstract record StoreCommand
{
    private StoreCommand() { }

    public sealed record BeginNewLeague(string League, int NewDataEpoch) : StoreCommand;
    public sealed record CommitCategory(DataTag Tag, CategorySnapshot Snapshot) : StoreCommand;
    public sealed record RecordCategoryFailure(DataTag Tag, ExchangeCategory Category, FailureRecord Failure) : StoreCommand;
    public sealed record CommitRate(DataTag Tag, DivineRate? Rate) : StoreCommand;
    public sealed record SetFetchedListing(DataTag Tag, ExchangeCategory Category, CategorySnapshot Snapshot) : StoreCommand;
    public sealed record SetLeagueList(LeagueList List) : StoreCommand;
    public sealed record SetLeagueUnresolved(string ReasonCode) : StoreCommand;
    public sealed record RecordHeartbeatAttempt(int RoundNumber) : StoreCommand;
    public sealed record RecordHeartbeatOutcome(RoundOutcome Outcome) : StoreCommand;
    public sealed record RecordLoopExit(LoopExitKind Kind) : StoreCommand;
    public sealed record SetLastErrorCmd(ErrorRecord Error) : StoreCommand;
    public sealed record SetConditionCmd(AppConditionKind Kind, bool Active, string? Detail) : StoreCommand;
}
```
신규 D-DL5. `SetLastErrorCmd`/`SetConditionCmd`로 이름 지은 이유는 `IErrorSink.Report`/`IConditionSink.Set`과 이름이 겹치면 같은 파일 안에서 오버로드처럼 읽혀 실수를 부르기 때문이다 — 명령 레코드와 포트 메서드는 다른 것이다.

### 8.3 Store

```
public sealed partial class Store : IHostedService, IMarketSnapshotSource, IConditionSink, IErrorSink, ISearchSource
{
    public Store(TimeProvider timeProvider, ILogger<Store> logger);

    public MarketSnapshot Current { get; }
    public event EventHandler? SnapshotChanged;

    public void Set(AppConditionKind kind, bool active, string? detail);      // IConditionSink
    public void Report(ErrorRecord error);                                    // IErrorSink
    public SearchResult Search(string query, SearchOptions options);          // ISearchSource

    public void Post(StoreCommand command);                                   // 동기, TryWrite 반환값 검사(6.2절)

    public Task StartAsync(CancellationToken ct);                             // 소비 루프 기동
    public Task StopAsync(CancellationToken ct);                              // Writer.Complete() -> 취소 없이 완료 대기 -> ct는 하드 타임아웃 전용
}
```
`Post`가 공개 메서드다 — `IConditionSink.Set`/`IErrorSink.Report`/각 모듈의 직접 호출(예: `Polling`의 `BeginNewLeague`)은 전부 내부적으로 `Post(new StoreCommand.XXX(...))`를 호출하는 얇은 오버로드로 노출한다. `Polling`이 실제로 호출하는 것은 `Post`가 아니라 아래 8.4의 타입 안전 오버로드 집합이다 — `StoreCommand`를 직접 만들어 넘기게 하면 `Polling`이 `Store`의 내부 명령 표현을 알게 되어 D-ST1의 "DataTag만 싣는다"는 캡슐화가 새어 나간다.

### 8.4 Polling 전용 커밋 오버로드

```
public sealed partial class Store
{
    public void BeginNewLeague(string league, int newDataEpoch);
    public void CommitCategory(DataTag tag, CategorySnapshot snapshot);
    public void RecordCategoryFailure(DataTag tag, ExchangeCategory category, FailureRecord failure);
    public void CommitRate(DataTag tag, DivineRate? rate);
    public void SetLeagueList(LeagueList list);
    public void SetLeagueUnresolved(string reasonCode);
    public void RecordHeartbeatAttempt(int roundNumber);
    public void RecordHeartbeatOutcome(RoundOutcome outcome);
    public void RecordLoopExit(LoopExitKind kind);
}
```
`SettingsViewModel`은 이 표의 `SetFetchedListing`에 해당하는 오버로드 하나만 쓴다: `void SetFetchedListing(DataTag tag, ExchangeCategory category, CategorySnapshot snapshot)`. 전부 동기, 내부에서 `StoreCommand`를 만들어 `Post`로 큐잉한다 — S2 6.2 표의 "생산자" 열이 이 오버로드들의 호출자 목록과 정확히 대응한다.

### 8.5 내부 시그니처 — 소비 루프, 검증, 적용

```
public sealed partial class Store
{
    private async Task ConsumeAsync(CancellationToken lifetimeToken);         // 8.1절 의사코드(S2 6.3) 그대로
    private void Apply(StoreCommand command);
    private static bool Validate(StoreCommand command, MarketSnapshot current, out string? rejectCode);
    private MarketSnapshot Publish(MarketSnapshot next);                       // Volatile.Write + Version 증가 + SnapshotChanged 발신
}
```
`Validate`는 `DataTag`를 지닌 명령에만 적용한다(S2 6.4). 거부 코드 리터럴은 13.3절.


## 9. Polling — 시그니처 확정

네임스페이스 PoeOverlay.Core.Polling. 허용 의존: Domain, Market, Store, Settings, Diagnostics, Pricing.StalenessPolicy(D-C2).

### 9.1 PollingService

```
public enum PollingTriggerKind { Scheduled, Repoll }

public sealed partial class PollingService : BackgroundService
{
    public PollingService(
        IMarketClient market, Store store, ISettingsSource settings,
        TimeProvider timeProvider, ILogger<PollingService> logger);

    protected override Task ExecuteAsync(CancellationToken stoppingToken);
}
```
`ExecuteAsync`가 최외곽 `finally`(D20)와 라운드별 `try/catch`(§9.5 허용 목록 1번)를 함께 갖는다 — 시그니처는 `BackgroundService`가 강제하는 형태뿐이며 본문은 S2 7.2/7.9의 의사코드 그대로다. **B4 — 첫 라운드 기동.** `ExecuteAsync`는 트리거 채널을 소비하는 루프(§7.2, S2)에 들어가기 전에 `await RunRoundAsync(RoundTrigger.Startup, stoppingToken)`을 정확히 1회 호출한다 — 그러지 않으면 첫 라운드는 `PeriodicTimer`의 첫 틱(5~60분)까지 시작되지 않는데, HLD §4.1("기동 → 리그 확정 → 첫 라운드 → 렌더")과 "`Loading`은 흡수 상태가 아니다"(첫 라운드가 성공·실패와 무관하게 반드시 전이시킨다)는 즉시 첫 라운드를 전제한다. S2 §7.2의 채널 스캐폴드 자체는 바꾸지 않는다 — 이 호출은 루프 밖에서 한 번 더 있을 뿐이다.

### 9.2 내부 시그니처 — 라운드 알고리즘

```
public sealed partial class PollingService
{
    private readonly Channel<PollingTriggerKind> triggers;      // 7.2절, D-PL2. 전송 값은 Scheduled/Repoll 둘뿐 — S2 그대로
    private volatile bool pendingLeagueChangeTrigger;           // B4, 신규 — OnSettingsChanged가 세운다(§9.3)

    private async Task RunRoundAsync(RoundTrigger trigger, CancellationToken ct);
    private async Task<RoundOutcome> ExecuteRoundStepsAsync(RoundContext ctx, AppSettings settings, CancellationToken ct);

    private static IReadOnlyList<ExchangeCategory> ResolveCategorySet(
        EquatableArray<WatchlistEntry> watchlist,
        IReadOnlyDictionary<ExchangeCategory, CategoryStatus> statuses, DateTimeOffset now);

    private (LeagueResolutionState State, string? League, string? ReasonCode) ResolveLeague(
        string? settingsLeague, LeagueList leagues);

    private static bool IsMedianJumpAcceptable(
        decimal newMedian, decimal? previousMedian, int consecutiveMedianJumps);

    private DivineRate? InheritOrExtractRate(
        MarketResult<CategorySnapshot> currencyResult, DivineRate? previous, RoundContext ctx, TimeSpan rateMaxAge);

    private static TimeSpan ComputeCooldown(int consecutiveFailures, int refreshIntervalMinutes);
}
```
각 메서드는 S2 7.3~7.8의 표·의사코드를 그대로 구현한다 — 이 절은 시그니처만 확정한다. `ResolveCategorySet`이 반환하는 목록의 정렬은 `ExchangeCategory` 숫자 순(§7.4, `Currency=1`이 항상 맨 앞).

### 9.3 재폴링 디바운스

```
public sealed partial class PollingService
{
    private void OnSettingsChanged(AppSettings oldSettings, AppSettings newSettings);
    private static bool RequiresImmediateRepoll(AppSettings oldSettings, AppSettings newSettings,
        IReadOnlyDictionary<ExchangeCategory, CategorySnapshot> currentCategories);
}
```
**B4 — `RoundTrigger`(Domain, `Startup`·`Scheduled`·`Repoll`·`LeagueChanged`)와 `PollingTriggerKind`(§9.1, `Scheduled`·`Repoll`뿐)가 사상 없이 공존하고 `RoundTrigger`에 소비자가 없다는 지적을 채널을 늘리지 않고 닫는다.** `triggers` 채널은 S2 §7.2가 정의한 그대로 `PollingTriggerKind` 두 값만 나른다 — 전송 계층은 바꾸지 않는다. `RunRoundAsync`의 매개변수 타입만 `RoundTrigger`(상위 개념)로 바꾸고, `PollingService`가 채널에서 값을 뽑을 때 다음 규칙으로 승격한다: `Scheduled → RoundTrigger.Scheduled`. `Repoll`이면 `pendingLeagueChangeTrigger`를 확인-후-소비(atomic)한다 — 서 있으면 `RoundTrigger.LeagueChanged`, 아니면 `RoundTrigger.Repoll`. 첫 라운드는 §9.1이 채널 밖에서 `RoundTrigger.Startup`으로 직접 호출한다. `OnSettingsChanged`는 `oldSettings.League?.Trim() != newSettings.League?.Trim()`이면(S2 §7.3의 `Trim()` 정규화와 동일 비교) `pendingLeagueChangeTrigger = true`로 세운 뒤 `Repoll`을 채널에 쓴다. `RunRoundAsync`는 받은 `trigger`를 라운드 시작 Information 로그 줄의 `Message`(자유 텍스트, §4.1)에 싣는다 — `LogEntry`의 스키마는 바꾸지 않는다. 이로써 `RoundTrigger`의 네 멤버 모두 실제로 읽히고 갈라진다.
디바운스 창·최소 간격 상수는 15절.


## 10. Settings — 시그니처 확정과 JSON 스키마 키

네임스페이스 PoeOverlay.Core.Settings. 허용 의존: Domain(포트 포함), Diagnostics.

### 10.1 AppSettings / WindowSettings

```
public sealed record AppSettings(
    int SchemaVersion, string? League, int RefreshIntervalMinutes,
    string Language, DisplayCurrency DefaultDisplayCurrency,
    WindowSettings Window, EquatableArray<WatchlistEntry> Watchlist,
    bool FirstRunAcknowledged)
{
    public static AppSettings Default { get; }        // 15절 기본값 표와 일치
}
public sealed record WindowSettings(
    double X, double Y, double Width, double Height, HeightMode HeightMode, double Opacity);
```

### 10.2 JSON 스키마 키 — 정본

신규 D-DL6. HLD 7절 스키마 표가 이미 사실상 확정한 키 이름을 이 문서가 formal화한다.

| 키 | 타입 | camelCase JSON 이름 |
|---|---|---|
| SchemaVersion | int | schemaVersion |
| League | string? | league |
| RefreshIntervalMinutes | int | refreshIntervalMinutes |
| Language | string | language |
| DefaultDisplayCurrency | enum | defaultDisplayCurrency, 값은 소문자 auto/chaos/divine |
| Window.X | double | window.x |
| Window.Y | double | window.y |
| Window.Width | double | window.width |
| Window.Height | double | window.height |
| Window.HeightMode | enum | window.heightMode, 값은 소문자 auto/explicit |
| Window.Opacity | double | window.opacity |
| Watchlist[].Id | string | watchlist[].id |
| Watchlist[].Category | string | watchlist[].category, 18종 열거 멤버 이름 문자열, 미지 값은 원문 보존 |
| Watchlist[].DisplayCurrency | enum? | watchlist[].displayCurrency, 생략 가능 |
| FirstRunAcknowledged | bool | firstRunAcknowledged |

firstRunAcknowledged가 정확한 최종 키 이름이다(S3 6.5절/13-11이 유예했던 자리) — HLD 7절 스키마 표와 S2 8.1이 이미 이 이름을 작업명으로 일관되게 써 왔고, 다른 후보가 검토된 적이 없으므로 이 문서는 기존 표기를 그대로 확정한다. 다른 키와 이름 규약(캐멀케이스, 축약 없음)도 일치한다.

### 10.3 ISettingsSource

```
public delegate void SettingsChangedHandler(AppSettings oldSettings, AppSettings newSettings);

public interface ISettingsSource
{
    AppSettings Current { get; }
    event SettingsChangedHandler? Changed;
    void Update(AppSettings next);
    Task FlushAsync(CancellationToken ct);
    void Acknowledge();
    WriteBlockReason BlockReason { get; }
}
public enum WriteBlockReason { None, Corrupt, Unreadable, FutureSchema }
```

### 10.4 SettingsLoadResult

```
public abstract record SettingsLoadResult
{
    private SettingsLoadResult() { }
    public sealed record Loaded(AppSettings Settings, IReadOnlyList<string> Corrections) : SettingsLoadResult;
    public sealed record Defaulted(string ReasonCode) : SettingsLoadResult;
    public sealed record IoFailed(string Path, string ExceptionType) : SettingsLoadResult;
    public sealed record Corrupt(string QuarantinePath) : SettingsLoadResult;
    public sealed record ReadOnly(AppSettings Settings) : SettingsLoadResult;
}
```
Defaulted.ReasonCode의 리터럴은 정상 경로 하나뿐이다: NoFile (파일이 아직 없음).

### 10.5 SettingsStore

```
public sealed partial class SettingsStore : ISettingsSource, IHostedLifecycleService
{
    public SettingsStore(string directory, TimeProvider timeProvider,
        IConditionSink conditionSink, IErrorSink errorSink, ILogger<SettingsStore> logger);

    public AppSettings Current { get; }
    public event SettingsChangedHandler? Changed;
    public void Update(AppSettings next);
    public Task FlushAsync(CancellationToken ct);
    public void Acknowledge();
    public WriteBlockReason BlockReason { get; }

    public Task StartingAsync(CancellationToken ct);
    public Task StartAsync(CancellationToken ct);
    public Task StartedAsync(CancellationToken ct);
    public Task StopAsync(CancellationToken ct);
    public Task StoppingAsync(CancellationToken ct);
    public Task StoppedAsync(CancellationToken ct);
}
```
directory는 APPDATA PoeOverlay 경로를 Composition이 조립해 주입한다(테스트에서 임시 디렉터리를 넣기 위함). conditionSink/errorSink는 Store가 구현하는 포트를 그대로 받는다 — SettingsStore는 Store의 구체 타입을 모른다(D-C5).

### 10.6 읽기 경로 — SettingsLoadResult 판독

```
public sealed partial class SettingsStore
{
    private static SettingsLoadResult Load(string path);
    private static AppSettings ParseAndValidate(JsonDocument doc, out IReadOnlyList<string> corrections);
    private static WatchlistEntry? ParseWatchlistEntry(JsonElement element, out string? discardReason);
}
```
Load는 S2 8.4의 6단계 판독을 순서대로 수행하는 JsonDocument 수동 판독이다(직렬화기를 쓰지 않음, 근거는 S2 8.4의 다섯 가지). ParseAndValidate의 6번째 단계가 firstRunAcknowledged를 읽는다 — 키 부재 또는 불리언이 아닌 값은 false로 취급하고(10.2절 표 그대로) corrections에 항목을 추가하지 않는다.

### 10.7 쓰기 경로 — SettingsWriteDto, 신규 D-DL15

S2 8.4의 문장 "쓰기는 소스 생성 직렬화기를 그대로 쓴다"는 AppSettings를 직접 직렬화한다는 뜻으로 읽히지만, 그대로는 컴파일되지 않는다는 것이 이 문서의 발견이다(19.1절). WatchlistEntry.Id는 ItemId, Category는 CategoryRef, DisplayCurrency는 Domain.DisplayCurrency 널러블이다. System.Text.Json이 이 값 타입들을 커스텀 컨버터 없이 직렬화하면 각 필드가 중첩 객체로 나와 10.2절의 평평한 스키마와 어긋난다. 이 문서가 전용 쓰기 DTO로 보강한다.

```
internal sealed class SettingsWriteDto
{
    public int SchemaVersion { get; init; }
    public string? League { get; init; }
    public int RefreshIntervalMinutes { get; init; }
    public required string Language { get; init; }
    public required string DefaultDisplayCurrency { get; init; }
    public required WindowWriteDto Window { get; init; }
    public required WatchlistEntryWriteDto[] Watchlist { get; init; }
    public bool FirstRunAcknowledged { get; init; }
}
internal sealed class WindowWriteDto
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public required string HeightMode { get; init; }
    public double Opacity { get; init; }
}
internal sealed class WatchlistEntryWriteDto
{
    public required string Id { get; init; }
    public required string Category { get; init; }
    public string? DisplayCurrency { get; init; }
}

internal static class SettingsWriteDtoMapper
{
    public static SettingsWriteDto ToWriteDto(AppSettings settings);
}
```
각 프로퍼티에는 10.2절 표의 JSON 이름을 JsonPropertyName 특성으로 붙인다(위 목록에서는 시그니처만 보이려 생략했다). ToWriteDto의 enum 문자열화 규칙: DisplayCurrency와 HeightMode는 소문자(auto/chaos/divine, auto/explicit). WatchlistEntry.Category는 CategoryRef.Raw를 그대로 쓴다(정규화하지 않는다, S2 2.2) — Known이 null이어도 Raw가 원문 문자열이므로 미지 카테고리도 그대로 왕복한다.

**A2 — CS8618 수정과 동형 결함 스윕.** `Language`·`DefaultDisplayCurrency`·`Window`·`Watchlist`·`WindowWriteDto.HeightMode`·`WatchlistEntryWriteDto.Id`·`.Category` 일곱 곳에 `required`를 붙였다(생성자를 두지 않은 것은 그대로 유지 — 매퍼가 객체 이니셜라이저로만 생성하므로 `required`가 더 짧다). 이 문서가 선언한 `{ get; init; }` 전체를 다시 훑었다 — `DiagnosticsStartupState`(4.6절, `bool`/`string?`뿐)와 7.1절의 와이어 DTO 여섯 개(`NinjaOverviewDto` 등, 전 필드가 `?`)는 전부 널러블이거나 값 타입이라 같은 결함이 없다. 이 셋(`SettingsWriteDto`/`WindowWriteDto`/`WatchlistEntryWriteDto`) 밖에서 CS8618을 낼 수 있는 비널 참조형 `{ get; init; }`은 이 문서에 더 없다.

### 10.8 SettingsJsonContext

```
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SettingsWriteDto))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext { }
```
쓰기 전용(S2 1.7 표). 들여쓰기는 S2 8.5 "사람이 열어보는 파일이다"를 그대로 반영한다.

### 10.9 원자적 쓰기

```
public sealed partial class SettingsStore
{
    private async Task WriteAtomicAsync(AppSettings settings, CancellationToken ct);
    private async Task<bool> TryWriteOnceAsync(string tmpPath, string finalPath, string backupPath, SettingsWriteDto dto, CancellationToken ct);
}
```
재시도 횟수와 간격은 15절. await using을 쓰는 파일 스트림 지점은 ConfigureAwait(false)를 명시한다(S2 8.5). **F2 정정 — 근거가 거꾸로였다.** 초판은 "CA2007이 `await using`을 잡지 않으므로"라 적었으나 【측정】 `await using`은 CA2007이 실제로 잡는다 — 분석기가 잡지 **않는** 것은 `await foreach` 하나뿐이다. 결론(수동 부착)은 유지한다 — `await using`은 분석기가 강제하므로 손으로 붙이지 않으면 애초에 빌드가 경고/오류로 막는다. 다만 `await foreach`의 `ConfigureAwait(false)` 준수는 분석기가 강제하지 않으므로 **리뷰에서 사람이 잡아야 한다** — 이 설계에서 그 형태를 쓰는 자리는 정확히 둘, `Store.ConsumeAsync`(§8.5의 소비 루프)와 `RollingFileSink`의 채널 소비 루프(§4.3)다.


## 11. Presentation — 시그니처 확정

네임스페이스 PoeOverlay.Core.Presentation.*. TargetFramework net8.0 — WindowsBase 타입(Dispatcher, DispatcherPriority)을 참조하지 않는다(S2 10.8, S3 D-PS1).

### 11.1 IUiDispatcher / IUiTicker

```
public enum UiPostPriority { Normal, Background, Render }

public interface IUiDispatcher
{
    bool CheckAccess();
    void Post(Action action, UiPostPriority priority = UiPostPriority.Normal);
    bool HasShutdownStarted { get; }
}

public interface IUiTicker
{
    event EventHandler? Tick;
    void Start(TimeSpan period);
    void Stop();
}
```
Shell의 구현체(UiDispatcher, UiTicker)는 12절.

### 11.2 SnapshotFanout

```
public interface IRefreshable
{
    void Refresh(MarketSnapshot snapshot, DateTimeOffset now);
}

public sealed partial class SnapshotFanout
{
    public SnapshotFanout(
        IMarketSnapshotSource snapshotSource, IUiDispatcher uiDispatcher, IUiTicker uiTicker,
        IConditionSink conditionSink, IErrorSink errorSink, TimeProvider timeProvider,
        ILogger<SnapshotFanout> logger);

    public void Attach(IRefreshable subscriber);       // UI 스레드 전용
    public void Detach(IRefreshable subscriber);       // UI 스레드 전용
}
```
내부적으로 postPending 플래그(Interlocked), 구독자 목록(패스 시작 시점 스냅샷 순회), deferred 버퍼(§8.1/§8.4)를 갖는다. 시그니처는 공개 표면뿐이다 — Republish/OnSnapshotChanged는 private.

```
public sealed partial class SnapshotFanout
{
    private void OnSnapshotChanged(object? sender, EventArgs e);
    private void OnTick(object? sender, EventArgs e);
    private void Republish();
    private void RunDeferred(IReadOnlyList<Action> deferred);
}
```
OnSnapshotChanged와 OnTick은 둘 다 같은 Republish 경로로 합류한다(D-PS3) — 병합 플래그를 공유하므로 시그니처가 사실상 동일한 트리거 어댑터다.

### 11.3 뷰모델 공통 — Rows

```
public sealed record PriceRowViewModel(
    ItemId Id, string DisplayName, PriceDisplay Price, ChangeDisplay Change,
    string RelativeTime, bool IsRateInherited, bool IsStale, RowKind Kind);

public enum RowKind { Normal, Loading, FetchFailed, ItemUnresolved, ItemDropped }

public sealed record BannerViewModel(AppConditionKind Kind, string Text, TimeSpan Duration, BannerSeverity Severity);
public enum BannerSeverity { Info, Warning, Error }
```

### 11.4 OverlayViewModel

```
public sealed partial class OverlayViewModel : ObservableObject, IRefreshable
{
    public OverlayViewModel(ILocalizer localizer, TimeProvider timeProvider, ILogger<OverlayViewModel> logger);

    public DisplayState State { get; }
    public IReadOnlyList<PriceRowViewModel> Rows { get; }
    public IReadOnlyList<BannerViewModel> Banners { get; }
    public string FooterAttribution { get; }
    public string FooterRelativeTime { get; }
    public int FailedCategoryCount { get; }
    public int HiddenRowCount { get; }               // 외 n개 더

    public void Refresh(MarketSnapshot snapshot, DateTimeOffset now);
}
```
ObservableObject는 CommunityToolkit.Mvvm(REQUIREMENTS 7절 지정). CA2007 면제 대상 폴더(Presentation)이므로 내부 async 커맨드가 있어도 ConfigureAwait 강제가 없다 — 다만 OverlayViewModel 자체는 사용자 커맨드가 없으므로(오버레이는 클릭 통과) async 멤버가 없다.

### 11.5 SettingsViewModel

```
public delegate void FetchedListingSink(string league, int dataEpoch, ExchangeCategory category, CategorySnapshot snapshot);   // B2, 신규

public sealed partial class SettingsViewModel : ObservableObject, IRefreshable, IDisposable
{
    public SettingsViewModel(
        ISearchSource searchSource, IMarketClient marketClient, ISettingsSource settingsSource,
        ILocalizer localizer, IOverlayModeService moveMode, IOverlayGeometryService geometry,
        RecentErrorRing errorRing, TimeProvider timeProvider, CancellationToken windowScopeToken,
        FetchedListingSink setFetchedListing, Func<CancellationToken, Task<bool>> retryTrayRegistration,
        ILogger<SettingsViewModel> logger);

    public string SearchQuery { get; set; }
    public IReadOnlyList<SearchHit> SearchResults { get; }
    public SearchOutcome SearchOutcome { get; }
    public IReadOnlyList<ExchangeCategory> UnfetchedCategories { get; }
    public IReadOnlyList<WatchlistEntry> Watchlist { get; }
    public IReadOnlyList<LeagueEntry> Leagues { get; }
    public LeagueListStatus LeaguesStatus { get; }
    public IReadOnlyList<BannerViewModel> Banners { get; }
    public bool WritesBlocked { get; }
    public bool IsMoveModeActive { get; set; }         // IOverlayModeService로의 통과 속성(HLD D4-b)
    public bool ShowFirstRunBanner { get; }

    public IAsyncRelayCommand AddToWatchlistCommand { get; }
    public IRelayCommand<ItemId> RemoveFromWatchlistCommand { get; }
    public IAsyncRelayCommand FetchCategoryCommand { get; }        // 캐시에 없을 때 카테고리 1회 조회
    public IAsyncRelayCommand ReloadLeaguesCommand { get; }
    public IRelayCommand RetryNowCommand { get; }                  // FetchFailed 쿨다운 무시 재시도
    public IRelayCommand AcknowledgeCorruptionCommand { get; }
    public IRelayCommand ResetPlacementCommand { get; }
    public IRelayCommand RevertHeightCommand { get; }
    public IRelayCommand DismissFirstRunBannerCommand { get; }
    public IRelayCommand OpenLogFolderCommand { get; }
    public IRelayCommand RetryTrayRegistrationCommand { get; }     // D-SH12, TrayIconHost로 위임

    public void Refresh(MarketSnapshot snapshot, DateTimeOffset now);
    public void Dispose();                                        // detach + 자원 정리(S3 5.3)
}
```
Refresh 내부 순서: 배너 계산(먼저, 독립 구간, S3 7.6) → 검색 결과 재계산 → 관심목록 행 갱신 → 리그 목록 갱신.

**B2 — 세 가지 결함을 함께 닫는다.** ① `windowScopeToken`을 공급할 팩터리가 없었다 — §12.4가 `SettingsWindowFactory`를 그 공급자로 확정했다. ② §19.4가 산문으로만 적었던 `retryTrayRegistration` 매개변수가 실제 생성자 목록에 없었다 — 위 시그니처에 추가했다. `RetryTrayRegistrationCommand`는 이 델리게이트를 그대로 호출한다: Composition의 `ServiceRegistration`이 `sp => sp.GetRequiredService<TrayIconHost>().TryReregisterAsync`를 바인딩한다(§19.4, D-SH12). ③ §8.4가 "SettingsViewModel이 `Store.SetFetchedListing`을 부른다"고 적었지만 생성자에 `Store`도 그것을 노출하는 인터페이스도 없었다 — `Store`에 6번째 얼굴을 추가하는 대신(S3 §3.1이 다섯 얼굴로 동결했으므로 늘리면 S3 개정이 필요해진다), `retryTrayRegistration`과 같은 델리게이트 패턴을 재사용한다: `FetchedListingSink`(위)를 새로 선언하고, Composition이 `sp => (league, epoch, category, snapshot) => sp.GetRequiredService<Store>().SetFetchedListing(new DataTag(league, epoch), category, snapshot)`를 바인딩한다. `SettingsViewModel`은 `Store`도 `DataTag`도 모른 채(D-C5와 동형의 격리) §8.4의 오버로드를 호출할 수 있다.

### 11.6 TrayViewModel

```
public sealed partial class TrayViewModel : ObservableObject, IRefreshable
{
    public TrayViewModel(ILocalizer localizer, IOverlayModeService moveMode, TimeProvider timeProvider,
        ILogger<TrayViewModel> logger);

    public TrayIconVariant IconVariant { get; }        // Normal, Warning, Error (D21)
    public string TooltipText { get; }
    public bool ShowMoveModeOffMenuItem { get; }

    public void Refresh(MarketSnapshot snapshot, DateTimeOffset now);
}
public enum TrayIconVariant { Normal, Warning, Error }
```


### 11.7 IOverlayModeService / IOverlayGeometryService

```
public enum MoveModeExitReason { SettingsToggleOff, TrayMenu, WatchdogTimeout }

public interface IOverlayModeService
{
    bool IsActive { get; }
    void EnterMoveMode();
    void ExitMoveMode(MoveModeExitReason reason);
    event EventHandler? StateChanged;
}

public interface IOverlayGeometryService
{
    void ResetPlacement();
    void RevertHeightToAuto();
}
```
둘 다 인터페이스 선언만 Presentation, 구현은 Shell(§12.3). UI 스레드 친화 선언(HLD D4-b) — Debug 빌드에서 호출 스레드를 단언한다.

### 11.8 UiState — 파생 조건과 상수 폴백

```
internal static class UiStateTemplates
{
    public const string RatePendingWithDuration = "rate pending for {0}";       // D1 정정 — {0}은 이미 서식된 기간 문자열(예: "3m")
    public const string PollingStoppedStale = "updates are delayed. last attempt {0}";  // D1 정정 — {0}은 이미 서식된 상대 시각(예: "3m ago")
    public const string PollingStoppedExited = "updates have stopped. restart the app"; // D1 정정
    public const string CommitRejectedBanner = "prices are not updating. check the league setting";
    public const string RateInheritedFooter = "rate carried over";
    public const string ItemDroppedRow = "price unavailable \u2014 item still exists";
    public const string ItemUnresolvedRow = "item not found";
    public const string TrayTooltipMore = "(+{0} more)";
}

internal static class DerivedConditions
{
    public static bool IsPollingStopped(Heartbeat heartbeat, DateTimeOffset now, int refreshIntervalMinutes);
    public static bool IsRatePending(DivineRate? rate, DateTimeOffset now, int refreshIntervalMinutes);
    public static bool IsRowStale(DateTimeOffset fetchedAt, DateTimeOffset now, int refreshIntervalMinutes);
    public static RowKind ClassifyRow(bool hasSnapshotEntry, bool consecutiveFailuresPositive, bool isInSkippedIds);
}

internal static class UiStateFormat
{
    public static string Ui(ITemplateSource templates, string key, string fallbackConst, params string[] args);
}
```
DerivedConditions의 네 함수는 전부 순수 함수다(now가 인자, TimeProvider 없음 — S2 1.3의 Pricing 패턴을 그대로 상속). S3 9.2가 요구하는 "한 Republish 패스 안에서 now를 두 번 얻지 않는다"를 시그니처가 강제한다. UiStateTemplates의 정확한 영문·인자 개수는 14.3절 지역화 키 카탈로그와 문자 단위로 일치해야 한다(S3 9.3, S2 4.6.3과 동형의 테스트로 강제 — 16.2절).

**G1 — `Ui(key, fallbackConst, args)` 3층 헬퍼(S3 §9.3 discharge).** 초판은 S2 §4.6.2의 3층 폴백(① `TryGetTemplate` 실패 시 상수 폴백 ② 센티널 검증 ③ `FormatException` 시 상수 폴백 재시도)을 Presentation이 상속한다고 §9.3(S3)에서 결정만 하고, 그 헬퍼의 실제 시그니처를 이 문서에 옮기지 않았다 — `PricingEngine.Tmpl`(§6.3, private, Pricing 전용)만 있었다. `UiStateFormat.Ui`가 그 짝이다 — `PricingEngine.Tmpl`과 정확히 동형이되 Presentation(`OverlayViewModel`/`TrayViewModel`/`SettingsViewModel`)이 부를 수 있도록 `internal static`으로 둔다(`ITemplateSource`는 `ILocalizer`가 구현하므로 그 인터페이스만 참조 — D-L4와 같은 경계).

**D1 정정.** 초판은 §11.8과 §14.3에 같은 세 키에 대해 서로 다른 영문·인자 모양을 실었다 — 특히 `PollingStoppedStale`은 인자 모양까지 달라서, §18.4가 실제로 넘기는 `Pricing.Relative(...)`의 완성된 구절("3m ago")을 §11.8의 옛 상수("...last attempt {0}m ago")에 넣으면 **"last attempt 3m ago ago"**가 렌더된다 — §16.2의 문자 단위 일치 테스트는 문서에 답이 둘이므로 어느 쪽을 옮겨 적어도 통과해, 이 결함을 잡지 못한다. **§14.3을 정본으로 §11.8을 위와 같이 맞췄다** — 호출부(§18.4)의 인자 모양이 §14.3과 일치하기 때문이다. `RatePendingWithDuration`도 같은 이유로 "이미 서식된 기간 문자열"을 받는 §14.3 모양으로 맞췄다. `PollingStoppedExited`는 텍스트만 §14.4(자리표시자 없는 키 카탈로그, 정본)와 맞췄다 — 아래 14절 머리말의 규칙 정정도 함께 참조.


## 12. Shell — 시그니처 확정

네임스페이스 PoeOverlay.*(프로젝트 자체가 루트). net8.0-windows.

### 12.1 Composition

```
internal static class Program
{
    [STAThread]
    private static int Main(string[] args);

    private static void RegisterFatalExceptionHandlers(Application app);   // B6 — AppDomain.UnhandledException + Application.DispatcherUnhandledException
    private static void RunShutdownSequence(IHost host, TrayIconHost trayHost, SingleInstanceGuard guard,
        MessageOnlyWindowHandle signalWindow, RollingFileSink logSink);    // B6 — S3 §3.3 a~f. 정상 종료·치명적 예외 종료가 이 메서드 하나를 공유한다
}

internal static class HostBuilderFactory
{
    public static IHost Build(string[] args);
}

internal static class ServiceRegistration
{
    public static IServiceCollection AddPoeOverlayCore(this IServiceCollection services);   // Domain 이하 8개 모듈
    public static IServiceCollection AddPoeOverlayShell(this IServiceCollection services);  // Shell 구현체
}
```
Main의 본문은 HLD 3.5의 1~12단계 그대로다(S4는 알고리즘을 바꾸지 않는다) — 이 절은 그 단계들이 호출하는 협력 타입들의 시그니처만 아래에서 확정한다. 등록 순서는 S3 3.1의 표(다섯 얼굴 Store 포함) 그대로.

**B6 — 종료 순서·치명적 예외 핸들러의 소유 타입.** 초판은 S3 §3.3의 a~f 단계와 §10.2의 `DispatcherUnhandledException` 허용 목록을 수행할 타입을 지정하지 않았다(`App.xaml.cs`는 "리소스 딕셔너리 병합용 최소 코드비하인드"로만 기술돼 있었다). HLD §3.2가 `OnStartup`이 아니라 명시적 `[STAThread] Main`을 요구한 이상(디스패처 컨텍스트에서 `host.StartAsync`를 부르면 폴링이 UI 스레드로 올라오는 문제, HLD §3.2), 그 대칭으로 종료·치명적 예외도 `App.xaml.cs`가 아니라 `Program`이 소유한다 — `App.xaml.cs`는 계속 XAML 리소스 병합 전용으로 남는다. `Main`은 `app.Run()` 호출 전에 `RegisterFatalExceptionHandlers(app)`로 `AppDomain.CurrentDomain.UnhandledException`과 `app.DispatcherUnhandledException`을 함께 구독한다 — 둘의 폐기 경로(트레이 아이콘 폐기 포함)는 반드시 `RunShutdownSequence`의 c단계 하나로 합류해야 한다(S3 §3.3 "하나의 멱등 가드를 공유"). 이 문서는 그 멱등성을 새 가드 타입으로 만들지 않는다 — `TrayIconHost.Dispose()`(§12.4) 자체가 `Interlocked.Exchange`로 재진입에 안전한 표준 `IDisposable` 관용구를 따르는 것으로 충분하며, `RunShutdownSequence`가 정상 종료 경로와 두 예외 핸들러 경로 모두에서 **같은 인스턴스의 같은 메서드**를 호출하기만 하면 이중 호출도 안전하다.

### 12.2 부팅 초기 진단 보관 — DiagnosticsStartupState의 소비

```
internal static class Program
{
    private static DiagnosticsStartupState CollectBootDiagnostics(string logDirectory);
    private static void ReconcileBootDiagnostics(DiagnosticsStartupState state, IConditionSink conditionSink, IErrorSink errorSink);
}
```
CollectBootDiagnostics는 1번(로거 오픈)과 5번(Settings 로드) 단계 사이 지역 변수로 결과를 들고 있다가, ReconcileBootDiagnostics가 Store.StartAsync 완료 직후 정확히 1회 호출된다(S3 3.1 P5, 3.2 M10). LoggingUnavailable은 errorSink.Report + conditionSink.Set(LoggingUnavailable, true, ...)로, flush 흔적 파일은 SettingsWriteFailed 재사용(errorSink.Report + conditionSink.Set(SettingsWriteFailed, true, "종료 시 flush 실패"))으로 반영한다. 흔적 파일은 이 호출이 실제로 나간 뒤에만 삭제한다(S3 3.2 M2 정정).

**B3 — `SnapshotFanout.Attach`의 실제 호출부.** 초판은 §11.2/§8.0(S3)에서 attach 계약만 정의하고 §12 어디에서도 부르지 않았다. 9번(오버레이 창 생성) 직후, `Main`이 `fanout.Attach(overlayViewModel)`과 `fanout.Attach(trayViewModel)`을 정확히 1회씩 호출한다(S3 §3.1 9′행) — 대칭 해제는 §3.3-a③이 담당한다(이미 명시돼 있었다). `SettingsViewModel`의 attach/detach는 창별이므로 여기서 하지 않는다 — §12.4 `SettingsWindowFactory.GetOrCreate()`가 새 인스턴스를 만들 때 `fanout.Attach(viewModel)`을 호출하고, §5.3(S3) 5단계가 `Dispose()` 직전에 detach한다.

```
internal static class BootFailureGuard
{
    public static void ShowFatalMessageBox(DiagnosticsStartupState? state, Exception? exception);
}
```
5번(호스트 초기 구성)부터 6번(Store 등록) 완료까지 구간 전체를 감싸는 최상위 try/catch(S3 D-SH19)가 예외를 잡으면 이 메서드를 호출한다 — Diagnostics가 열려 있으면 같은 내용을 파일에도 기록하되, MessageBox 표시는 무조건 경로다.

### 12.3 Overlay — Interop, 모드 서비스, 기하 서비스

```
internal static class Win32Constants
{
    public const int  GWL_EXSTYLE        = -20;                  // 신규, SDK 표준값
    public const uint WS_EX_LAYERED      = 0x00080000;
    public const uint WS_EX_TRANSPARENT  = 0x00000020;
    public const uint WS_EX_NOACTIVATE   = 0x08000000;           // 측정 — 00-shell-measurements.md, GWL_EXSTYLE=0x08080028의 성분(LAYERED|TRANSPARENT|NOACTIVATE|TOPMOST)
    public const uint LWA_COLORKEY       = 0x00000001;
    public const uint LWA_ALPHA          = 0x00000002;
    public static readonly IntPtr HWND_MESSAGE = new IntPtr(-3); // 신규, SDK 표준값
    public const uint SMTO_ABORTIFHUNG   = 0x00000002;           // 신규, S3 §3.2 SendMessageTimeout
}

[Flags]
public enum ExtendedStyleBits : uint
{
    None        = 0,
    Layered     = Win32Constants.WS_EX_LAYERED,
    Transparent = Win32Constants.WS_EX_TRANSPARENT,
    NoActivate  = Win32Constants.WS_EX_NOACTIVATE,
}

public sealed class ExtendedStyleGate
{
    public delegate ExtendedStyleGate Factory(IntPtr hwnd);      // A3 — public. OverlayWindow의 public 생성자가 이 델리게이트를 받으므로 internal이면 CS0051

    public ExtendedStyleGate(IntPtr hwnd);
    public ExtendedStyleBits Read();
    public void ApplyOr(ExtendedStyleBits mask);
    public void ApplyAndNot(ExtendedStyleBits mask);
    public void SetLayered(uint colorKeyRgb, byte alpha, LwaFlags flags);
}
[Flags]
public enum LwaFlags { ColorKey = 1, Alpha = 2 }
```
Shell 안 어디에서도 SetWindowLong/SetLayeredWindowAttributes를 직접 부르지 않는다(S3 4.1) — 이 게이트가 유일한 경로다.

**A3 정정.** 초판은 `Win32Constants`·`ExtendedStyleBits`를 "15절 상수표 참조"로 본문을 비워 뒀는데 15절에는 Win32 수치가 하나도 없었다. 위 값은 이 문서가 지금 채운다(15.9절에도 동일 표를 둔다). `ExtendedStyleGate`·`ExtendedStyleBits`·`LwaFlags`·`Factory`를 `internal`에서 `public`으로 올린 이유는 아래 `OverlayWindow`의 public 생성자가 `Factory`를 매개변수로 받기 때문이다 — internal 델리게이트를 public 생성자 매개변수로 쓰면 CS0051이고(A3), 반대로 생성자를 internal로 내리면 기본 제공 DI 컨테이너가 non-public 생성자를 해석하지 못한다(Store/PollingService와 같은 이유로 public이 필요하다). 캡슐화 규칙("Shell 어디서도 SetWindowLong을 직접 부르지 않는다")은 C# 접근 한정자가 아니라 "이 게이트를 거치지 않은 P/Invoke 호출을 두지 않는다"는 절차 규약이므로 타입 자체의 공개 여부와 무관하다.

```
internal sealed class OverlayModeService : IOverlayModeService
{
    public OverlayModeService(OverlayWindow window, ExtendedStyleGate gate, IUiDispatcher dispatcher,
        TimeProvider timeProvider, IConditionSink conditionSink, ILogger<OverlayModeService> logger);

    public bool IsActive { get; }
    public void EnterMoveMode();
    public void ExitMoveMode(MoveModeExitReason reason);
    public event EventHandler? StateChanged;

    // B1 — OverlayWindow의 마우스 핸들러가 부르는 내부 훅. 캡처 개념은 Shell(View)만 알므로 Presentation의
    // IOverlayModeService 표면에는 넣지 않는다(internal로 충분).
    internal void NotifyDragActivity();       // 드래그·리사이즈 시작·진행마다 호출 — 내부 MoveModeWatchdog.Kick()으로 전달
    internal void NotifyCaptureReleased();    // LostMouseCapture 시 호출 — "만료됨" 플래그가 서 있으면 그제서야 Exiting을 개시(§4.6.1)
}
```
`OverlayModeService`는 생성자에서 자신이 소유하는 `MoveModeWatchdog` 인스턴스 하나를 내부에 구성한다 — `onIdleTimeout` 콜백은 이미 `IUiDispatcher.Post`를 거쳐 UI 스레드에서 실행되며(워치독 자신의 계약), 그 안에서 캡처 중이 아니면 즉시 `ExitMoveMode(MoveModeExitReason.WatchdogTimeout)`을 호출하고 캡처 중이면 private `bool expiredWhileCaptured` 플래그만 세운다(S3 §4.6.1). `NotifyCaptureReleased()`는 그 플래그를 소비해(세워져 있으면 지우고 `Exiting`을 개시) `LostMouseCapture`가 요구하는 지연 처리를 구현한다. `NotifyDragActivity()`는 드래그·리사이즈가 진행되는 동안 워치독의 `Kick()`을 그대로 전달할 뿐이다.

```
internal sealed class OverlayGeometryService : IOverlayGeometryService
{
    public OverlayGeometryService(OverlayWindow window, ISettingsSource settings);
    public void ResetPlacement();
    public void RevertHeightToAuto();
}

internal static class OverlayGeometryValidator
{
    public static bool HasMinimumVisibleArea(Rect windowBounds, IReadOnlyList<Rect> workAreas, Size footerSize);
    public static (double X, double Y) ClampToDefault();
}

internal sealed class MoveModeWatchdog : IDisposable
{
    public MoveModeWatchdog(IUiDispatcher dispatcher, TimeProvider timeProvider, Action onIdleTimeout);
    public void Kick();         // B1 — 활동 시 유휴 타이머 리셋(드래그·리사이즈 시작·진행 중 호출, §4.6.1)
    public void Dispose();      // TimeProvider.CreateTimer 인스턴스 폐기 (S3 3.3-a5)
}

internal sealed class DisplayChangeWatcher : IDisposable
{
    public DisplayChangeWatcher(IUiDispatcher dispatcher, IOverlayModeService modeService);
    public void Dispose();
}
```
**B1 — MoveModeWatchdog의 재사용 경로.** 초판은 생성자와 `Dispose()`뿐이라 활동에 의한 리셋도, LostMouseCapture가 나중에 소비할 "만료됨" 플래그를 세울 방법도 없었다. `Kick()`을 추가하고, 소유·배선은 `OverlayModeService`가 맡는다(아래) — 워치독은 DI로 주입하지 않는다. 수명이 `OverlayModeService` 자신과 완전히 같고 다른 소비자가 없기 때문이다.

**A3 — `OverlayGeometryValidator`는 정적 클래스이므로 매개변수 타입이 될 수 없다**(CS0723, "정적 타입의 변수를 선언할 수 없다"). `HasMinimumVisibleArea`/`ClampToDefault`가 상태 없는 순수 함수이므로 `DisplayChangeWatcher`는 그 정적 메서드를 직접 호출한다 — 주입할 인스턴스 자체가 없다.

MoveModeWatchdog의 비활동 임계값은 15절 상수. DisplayChangeWatcher는 SystemEvents.DisplaySettingsChanged를 구독해 IUiDispatcher.Post로 마샬링한 뒤 재검증 루틴(S3 4.7, `OverlayGeometryValidator.HasMinimumVisibleArea`/`ClampToDefault`를 정적으로 직접 호출)을 실행한다 — Active 상태 중 트리거가 오면 보류 플래그만 세운다(S3 4.7 N8-1).


### 12.4 Tray

```
internal sealed class UiDispatcher : IUiDispatcher
{
    public UiDispatcher(Dispatcher dispatcher);
    public bool CheckAccess();
    public void Post(Action action, UiPostPriority priority = UiPostPriority.Normal);
    public bool HasShutdownStarted { get; }
}

internal sealed class UiTicker : IUiTicker
{
    public UiTicker(Dispatcher dispatcher);
    public event EventHandler? Tick;
    public void Start(TimeSpan period);
    public void Stop();
}
```
UiDispatcher.Post의 사상표(S3 7.2): Normal to DispatcherPriority.Normal, Background to DispatcherPriority.Background, Render to DispatcherPriority.Render.

```
internal sealed class TrayIconHost : IDisposable
{
    public TrayIconHost(TrayViewModel viewModel, IUiDispatcher dispatcher, IConditionSink conditionSink,
        SettingsWindowFactory settingsWindowFactory, ILogger<TrayIconHost> logger);

    public bool TryRegister();                          // NIM_ADD, 동기 Thread.Sleep 백오프(D-SH5)
    public Task<bool> TryReregisterAsync(CancellationToken ct);   // 펌프 도는 시점 전용 재시도(D-SH12)
    public void Dispose();                               // 확정적 폐기(S3 3.3-c)
}
internal sealed class SettingsWindowFactory
{
    public SettingsWindowFactory(IServiceProvider serviceProvider, OverlayWindow overlayWindow, SnapshotFanout fanout);
    public SettingsWindow GetOrCreate();                 // 이미 열려 있으면 Activate()만
}
```
TryRegister의 재시도 횟수·간격, TryReregisterAsync의 재시도 정책은 15절 상수. TrayIconHost가 클릭 라우팅(좌클릭/더블클릭 = 설정 창, 우클릭 = 메뉴)을 갖는다 — 클릭 핸들러는 이미 WPF UI 스레드에서 돈다(측정 4 D2)므로 Dispatcher.Invoke가 없다. `TrayIconHost.Dispose()`는 멱등이다(B6) — 정상 종료와 두 치명적 예외 핸들러(§12.1) 경로가 같은 인스턴스를 향해 두 번 이상 부를 수 있으므로, 내부적으로 `Interlocked.Exchange`로 재진입을 막는다.

**B2 — `windowScopeToken`의 공급자.** `SettingsWindowFactory.GetOrCreate()`가 새 `SettingsWindow`/`SettingsViewModel`을 만들 때마다(이미 열려 있으면 재사용하므로 이 경로를 타지 않는다) 새 `CancellationTokenSource`를 만들어 `.Token`을 `SettingsViewModel` 생성자에 주입하고, 그 `Cts` 인스턴스는 factory가 창 인스턴스와 함께 들고 있는다. §5.3(S3) 1단계 직후(정확히는 2단계) 그 `Cts.Cancel()`이 불리고, 5단계에서 `SettingsViewModel.Dispose()` 직후 factory가 `Cts.Dispose()`한다 — 창이 다시 열리면 새 `Cts`를 새로 만든다(재사용하지 않는다).

### 12.5 Startup — 단일 인스턴스, 신호 채널

```
internal sealed class SingleInstanceGuard : IDisposable
{
    public SingleInstanceGuard(string mutexName);
    public bool TryAcquire();
    public void Release();
    public void Dispose();
}

internal sealed class MessageOnlyWindowFactory
{
    public MessageOnlyWindowFactory();
    public MessageOnlyWindowHandle Create(string className, string windowTitle, Func<uint, IntPtr, IntPtr, IntPtr?> wndProc);
    // wndProc: (msg, wParam, lParam) -> 처리했으면 반환할 IntPtr, 아니면 null(DefWindowProc으로 위임)
}
internal sealed class MessageOnlyWindowHandle : IDisposable
{
    public IntPtr Hwnd { get; }
    public void Dispose();      // DestroyWindow + UnregisterClass
}

internal sealed class InstanceSignal : IDisposable
{
    // 수신부 — 메시지 전용 창. MessageOnlyWindowFactory로 만든다(B5)
    public InstanceSignal(IUiDispatcher dispatcher, MessageOnlyWindowFactory windowFactory, Action onSignalReceived);
    public void StartReceiving();
    public void StopReceiving();
    public void Dispose();

    // 발신부 — 정적 메서드, 두 번째 프로세스가 첫 프로세스를 향해 호출. PID가 아니라 클래스 이름으로 찾는다(A4)
    public static InstanceSignalSendResult TrySend(string className, TimeSpan perAttemptTimeout, int maxAttempts);
}
internal enum InstanceSignalSendResult { Acknowledged, NoResponse, WindowNotFound }

internal static class FirstRunGate
{
    public static bool ShouldAutoShowSettings(AppSettings settings);   // B5 — !settings.FirstRunAcknowledged
}
```
**A4 정정.** 초판의 `TrySend(int firstProcessId, ...)`는 S3 §3.2와 모순됐다 — S3가 발견을 `FindWindowEx(HWND_MESSAGE, IntPtr.Zero, className, null)`(클래스 이름 기반)으로 확정했는데, 두 번째 인스턴스가 첫 인스턴스의 PID를 알아낼 방법 자체가 어디에도 없다. `firstProcessId` 매개변수를 없애고 `className`(15.6절 상수)으로 정렬했다.

**B5 — `MessageOnlyWindowFactory`/`FirstRunGate` 시그니처.** 초판은 두 파일을 §2.1 배치에만 올려 두고 시그니처가 없었다. `MessageOnlyWindowFactory`는 `CreateWindowEx`/`RegisterClassEx`/`DefWindowProc` P/Invoke를 감싸는 `Interop/` 전용 헬퍼로, `ExtendedStyleGate`와 같은 패턴이다(Win32 메커니즘을 이 한 곳에만 둔다) — `InstanceSignal`이 그 위에서 메시지 라우팅만 담당한다. `FirstRunGate`는 순수 판정 함수 하나뿐이다 — 실제로 설정 창을 여는 동작(주체)은 §12.1의 `Program`이 부팅 순서 10번(트레이 아이콘) 직후, 11번(app.Run()) 직전에 `if (FirstRunGate.ShouldAutoShowSettings(settingsSource.Current)) settingsWindowFactory.GetOrCreate().Show();`를 호출하는 것으로 확정한다 — FR-08-6("첫 실행 시 설정 창 자동 표시")이 그동안 `SettingsViewModel.ShowFirstRunBanner`/`DismissFirstRunBannerCommand`(수동적 배너 표시)만 갖고 실제로 창을 여는 주체가 없었다.

클래스 이름 문자열, 창 제목, RegisterWindowMessage 이름, 센티널 값은 15절 상수(신규 D-DL7~D-DL9). StartReceiving이 만드는 창은 HWND_MESSAGE 부모의 메시지 전용 창이며 오버레이 HWND와 별개다(D-SH4). 수신 핸들러는 처리 후 반환값에 센티널을 심고, TrySend는 SendMessageTimeout의 원시 성공 반환과 lpdwResult의 센티널 일치를 함께 검사해야만 Acknowledged를 반환한다(D-SH18).

### 12.6 OverlayWindow / SettingsWindow — 코드비하인드 표면

```
public sealed partial class OverlayWindow : Window
{
    public OverlayWindow(OverlayViewModel viewModel, ExtendedStyleGate.Factory gateFactory, ISettingsSource settings);
    // SourceInitialized, Closing 등 이벤트 핸들러는 private, Interop 게이트를 통해서만 Win32를 만진다(S3 1.1)
}
public sealed partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel, Window owner);
    // Closing 핸들러가 S3 5.3의 5단계 닫기 처리를 수행한다
}
```
두 창의 XAML 마크업 자체는 이 문서의 범위 밖이다(구역 확정은 S3 5.4) — 코드비하인드 생성자 시그니처만 확정한다. ExtendedStyleGate.Factory는 `internal delegate ExtendedStyleGate Factory(IntPtr hwnd);` — SourceInitialized 시점에야 HWND가 존재하므로 생성자 주입이 아니라 팩터리로 지연시킨다.


## 13. 오류 코드 문자열 카탈로그

신규 D-DL10. FailureRecord.Code/ErrorRecord.Code는 같은 문자열 공간이다(S2 2.12). 전부 PascalCase, 공백 없음, 서수 비교 대상이므로 대소문자까지 고정한다.

### 13.1 FailureKind별 기본 Code

| FailureKind | Code 리터럴 | 생산자 |
|---|---|---|
| Network | Network | Market(HTTP 예외) |
| Timeout | Timeout | Market(요청 타임아웃) |
| HttpStatus | HttpStatus | Market(재시도 불가 4xx) |
| RateLimited | RateLimited | Market(429 소진) |
| Deserialization | Deserialization | Market(골격 역직렬화 실패, 2단계/2프라임 단계) |
| EmptyLines | EmptyLines | Market(lines 0건) |
| NoPricedLines | NoPricedLines | Market(mapped == 0) |
| PrimaryCurrencyMismatch | PrimaryCurrencyMismatch | Market(core.primary != chaos) |
| DivineLineMissing | DivineLineMissing | Polling(D8-c, Currency에 divine 라인 없음) |
| MedianJump | MedianJump | Polling(D8-e, 강제 수용되지 않은 급변) |
| LeagueListInvalid | 13.3절의 하위 코드 중 하나를 그대로 옮겨 씀 | Polling(리그 목록 실패를 ErrorRecord로 승격할 때) |
| MappingFault | MappingFault | Market(D-MK4 경계 catch, 예상 밖 예외) |
| FieldMissingRatio | 13.2절의 사유 분화 표 | Market(20% 임계 초과) |
| ElementFault | ElementFault | **미사용 — 19.2절 발견 사항 참조** |

ElementFault는 FailureKind 열거에는 있으나 이 문서가 S2 5.5.3/5.5.4의 판정 순서를 전수 추적한 결과 FailureRecord.Kind로 실제 생산되는 자리가 없다(19.2절). Code 리터럴은 혹시 모를 호출을 위해 정의해 두되, 구현 중 이 자리가 실제로 쓰이면 그 자체가 §5.5.3 순서 이해가 잘못됐다는 신호이므로 반드시 재검토한다.

### 13.2 FieldMissingRatio 사유 분화 — 지배 원인 판정 알고리즘

신규 D-DL11. S2 5.5.4는 "지배적 사유가 X면 코드 Y"라고만 적고 동률 처리를 정의하지 않았다 — 이 문서가 채운다.

```
DetermineFieldMissingCode(skips: SkipCounts) -> string:
    candidates = { NonPositiveValue: skips.NonPositiveValue,
                   ElementFault:     skips.ElementFault,
                   BlankId:          skips.BlankId }
                 // Duplicate는 후보에서 제외한다 — 전용 코드가 없다(아래 표 참조)
    max = candidates.Values.Max()
    if max == 0: return "FieldMissingRatio"                 // 전부 Duplicate이거나 표본이 0
    winners = candidates에서 값이 max와 같은 키들
    if winners.Count > 1: return "FieldMissingRatio"         // 동률은 분화하지 않는다
    return winners.Single() switch:
        NonPositiveValue -> "AllNonPositive"
        ElementFault      -> "ElementFaultRatio"
        BlankId           -> "MissingIdRatio"
```

| 지배 원인 | Code 리터럴 |
|---|---|
| NonPositiveValue 단독 최댓값 | AllNonPositive |
| ElementFault 단독 최댓값 | ElementFaultRatio |
| BlankId 단독 최댓값 | MissingIdRatio |
| Duplicate가 최댓값이거나 동률이거나 전부 0 | FieldMissingRatio (기본값) |

FailureRecord.Kind는 이 네 갈래 전부 FieldMissingRatio로 고정한다 — Code만 갈린다(S2 5.5.4 "지배적 사유가 ~ 코드 ~"의 문면이 이미 이 구조를 함의한다). Detail 필드에는 항상 `blank={n} nonpos={n} dup={n} fault={n}` 형식으로 네 카운트를 싣는다(S2 5.5.4).

### 13.3 리그 목록 판정 코드 — LeagueList.FailureCode

| 상황 | FailureCode 리터럴 |
|---|---|
| 배열이 비었음 | EmptyLeagueList |
| HTTP/역직렬화 실패 | 13.1절의 Network/Timeout/HttpStatus/RateLimited/Deserialization 코드를 그대로 재사용 |

Polling이 리그 목록 실패를 ErrorRecord로 승격할 때 Kind=LeagueListInvalid, Code=위 표에서 실제로 발생한 리터럴을 그대로 옮긴다(13.1절 LeagueListInvalid 행).

### 13.4 Store 검증 거부 코드 — DataTag 계열

S2 6.4/6.2가 이미 리터럴을 문자로 제시했다 — 이 문서는 그대로 확정하고 한곳에 모은다.

| 상황 | Code |
|---|---|
| current.DataLeague가 null | NoBaseline |
| cmd.Tag.League가 null 또는 빈 문자열 (default(DataTag) 방어 포함) | DefaultTag |
| cmd.Tag.DataEpoch != current.DataEpoch | EpochMismatch |
| cmd.Tag.League != current.DataLeague | LeagueMismatch |
| CommitCategory의 스냅샷에 빈 ItemId 키 포함 | EmptyItemId |

이 코드들은 FailureRecord.Code가 아니라 Warning 로그 항목(§4.1 log line의 code= 필드)에 실린다 — RejectedCommitCount를 올린 명령 자체는 ErrorRecord를 만들지 않는다(S2 6.4 "lastError는 건드리지 않는다"). CommitRejected 조건의 Detail에는 "마지막 거부 코드"로 위 다섯 중 하나가 실린다(S2 6.4 D-ST4).

### 13.5 Store 소비 루프 예외 — ApplyFault

Apply가 예외를 던지면 `SetLastErrorCmd(new ErrorRecord(now, "Store", "ApplyFault", "ui.error.applyFault", ex.Message, null, null, null, ex.GetType().Name))`를 같은 catch 안에서 큐잉한다(S2 6.3 의사코드 그대로, Code="ApplyFault"는 리터럴로 고정).


### 13.6 MessageKey 카탈로그 — ui.error.*

신규 D-DL12. ErrorRecord.MessageKey는 Localization이 표시 시점에 푸는 키다(S2 2.12). Kind 하나당 키 하나로 사상한다 — Store/Settings 출처 오류(Kind가 없는 자리)는 전용 키를 둔다.

| MessageKey | 대응 FailureKind / 출처 | 인자 개수 | 영문 값 |
|---|---|---|---|
| ui.error.network | Network | 0 | Could not reach poe.ninja |
| ui.error.timeout | Timeout | 0 | Request to poe.ninja timed out |
| ui.error.httpStatus | HttpStatus | 1 (HTTP 상태 코드) | poe.ninja returned HTTP {0} |
| ui.error.rateLimited | RateLimited | 0 | poe.ninja is rate-limiting requests |
| ui.error.deserialization | Deserialization | 0 | poe.ninja response could not be parsed |
| ui.error.emptyLines | EmptyLines | 1 (카테고리) | No listings returned for {0} |
| ui.error.noPricedLines | NoPricedLines | 1 (카테고리) | No priced listings for {0} |
| ui.error.primaryCurrencyMismatch | PrimaryCurrencyMismatch | 0 | Unexpected base currency in response |
| ui.error.divineLineMissing | DivineLineMissing | 0 | Divine Orb price missing from response |
| ui.error.medianJump | MedianJump | 1 (카테고리) | Prices for {0} changed unexpectedly |
| ui.error.leagueListInvalid | LeagueListInvalid | 0 | Could not load the league list |
| ui.error.mappingFault | MappingFault | 0 | Unexpected error while reading prices |
| ui.error.fieldMissingRatio | FieldMissingRatio(전 사유 분화 포함) | 1 (카테고리) | Too many invalid entries for {0} |
| ui.error.applyFault | Store.ApplyFault | 0 | Internal state update failed |
| ui.error.settingsWriteFailed | Settings 쓰기 실패 | 0 | Could not save settings |
| ui.error.settingsCorrupt | Settings 파손 | 1 (격리 경로) | Settings file was corrupted, backed up to {0} |
| ui.error.generic | 위 표에 없는 모든 자리(방어적 폴백) | 0 | An unexpected error occurred |

각 키는 14절 지역화 카탈로그에도 동일하게 등재한다(단일 소스는 14절 — 이 표는 13절 독자를 위한 교차 참조다).


## 14. 지역화 키 카탈로그 — 단일 권위 목록

신규 D-DL13. 이 절이 ui.* 키의 유일한 정본이다. S2 3.6/4.6.2/4.6.3(ui.price.*, ui.time.*)과 S3 9.3(ui.state.*, ui.tray.*)이 요구한 "문자 단위 일치"를 이 절 하나로 만족시킨다 — PriceTemplates(6.3절)와 UiStateTemplates(11.8절)의 값은 아래 표와 정확히 같아야 하며, 16.6절의 등가 테스트가 그것을 강제한다. 값은 전부 내장 en.json의 정본이다(1차 릴리스는 영문만 채운다, FR-07-3).

**D1 정정 — 상수 보유 규칙.** 초판은 "자리표시자가 있는 키만 컴파일 시점 상수를 갖는다"고 적어 놓고(S2 4.6.4의 범위 확정 결정), 실제로는 자리표시자가 없는 다섯 키(`PollingStoppedExited`·`CommitRejectedBanner`·`RateInheritedFooter`·`ItemDroppedRow`·`ItemUnresolvedRow`, §11.8·§18.3)에도 상수를 뒀다 — 문서가 스스로 세운 규칙을 어겼다. **정정된 규칙**: 값에 자리표시자가 있는 키는 예외 없이 컴파일 시점 상수를 갖는다(서식 인자 개수 불일치는 체인 5층 — 키 문자열 그대로 — 만으로는 잡히지 않으므로 §16.2의 문자 단위 테스트가 반드시 필요하다). 자리표시자가 없는 키 중에서도 **사용자가 반드시 봐야 하는 상태 배너·행 텍스트**(위 다섯 키가 정확히 이것이다 — 폴링 정지·거부·승계·미해결·드롭 표시)는 상수를 둔다. 트레이 메뉴 항목처럼 부차적인 나머지 무-자리표시자 키(14.6절 `ui.tray.*` 등)는 상수를 두지 않고 체인 5층으로 충분하다 — 이 문서는 규칙을 상수 목록에 맞춰 고쳤다(상수를 빼는 대신).

### 14.1 ui.price.*

| 키 | 인자 | 영문 값 |
|---|---|---|
| ui.price.chaos | 1 | {0}c |
| ui.price.divine | 1 | {0}d |
| ui.price.chaosWithDivine | 2 | {0}c ({1}d) |
| ui.price.chaosRatePending | 1 | {0}c (rate pending) |
| ui.price.perChaos | 1 | {0} per 1c |
| ui.price.perDivine | 1 | {0} per 1d |
| ui.price.ratePending | 0 | rate pending |
| ui.price.unavailable | 0 | (em dash, U+2014) |
| ui.price.change | 2 | {0}{1}% |

### 14.2 ui.time.*

| 키 | 인자 | 영문 값 |
|---|---|---|
| ui.time.justNow | 0 | just now |
| ui.time.secondsAgo | 1 | {0}s ago |
| ui.time.minutesAgo | 1 | {0}m ago |
| ui.time.hoursAgo | 1 | {0}h ago |
| ui.time.daysAgo | 1 | {0}d ago |

### 14.3 ui.state.* — 자리표시자 있는 키 (상수 폴백 필수)

| 키 | 인자 | 영문 값 | 대응 UiStateTemplates 상수 |
|---|---|---|---|
| ui.state.ratePendingDuration | 1 (기간 문자열, 예: 3m) | rate pending for {0} | RatePendingWithDuration |
| ui.state.pollingStoppedStale | 1 (상대 시각 문자열) | updates are delayed. last attempt {0} | PollingStoppedStale |
| ui.state.fetchFailedRow | 1 (상대 시각 문자열) | update failed {0} | (신규, 11.8절 목록에 추가 필요 — 19.3절 갭) |
| ui.state.fetchFailedBadge | 1 (개수) | {0} categories failed to update | (신규, 19.3절 갭) |
| ui.state.loggingUnavailable | 1 (경로) | log file unavailable \u2014 path: {0} | (신규, 19.3절 갭) |
| ui.tray.tooltipMore | 1 (개수) | (+{0} more) | TrayTooltipMore |

### 14.4 ui.state.* — 자리표시자 없는 키 (상수 폴백 불필요, 체인 5층으로 충분)

| 키 | 영문 값 | 대응 AppConditionKind |
|---|---|---|
| ui.state.pollingStoppedExited | updates have stopped. restart the app | PollingStopped (LoopExited 갈래) |
| ui.state.leagueUnresolved | could not determine the league | LeagueUnresolved |
| ui.state.commitRejected | prices are not updating. check the league setting | CommitRejected |
| ui.state.settingsWriteFailed | could not save settings | SettingsWriteFailed |
| ui.state.settingsCorrupt | settings file was corrupted | SettingsCorrupt |
| ui.state.settingsReadOnly | settings file is from a newer version. editing is disabled | SettingsReadOnly |
| ui.state.settingsUnreadable | could not read the settings file | SettingsUnreadable |
| ui.state.trayUnavailable | tray icon could not be registered | TrayUnavailable |
| ui.state.viewModelRefreshFailing | display is not updating | ViewModelRefreshFailing |
| ui.state.rateInherited | rate carried over | RateInherited(파생) |
| ui.state.itemUnresolved | item not found | ItemUnresolved(파생) |
| ui.state.itemDropped | price unavailable \u2014 item still exists | ItemDropped(파생) |

### 14.5 ui.error.* — 13.6절과 동일 표(교차 참조 아님, 정본은 이 절)

13.6절의 표를 그대로 옮긴다. 값·인자 개수는 13.6절 참조 — 중복 나열하지 않는다.

### 14.6 ui.tray.* — 메뉴·툴팁

| 키 | 인자 | 영문 값 |
|---|---|---|
| ui.tray.openSettings | 0 | Open settings |
| ui.tray.movePositionOff | 0 | Turn off move mode |
| ui.tray.exit | 0 | Exit |
| ui.tray.appName | 0 | PoE Market Price Tracker |

### 14.7 ui.footer.*

| 키 | 인자 | 영문 값 |
|---|---|---|
| ui.footer.attribution | 0 | Data from poe.ninja \u2014 a community site, not affiliated with GGG |

### 14.8 억제 채널 이름 — SessionSuppressionRegistry.channel 리터럴

신규 D-DL14. 4.5절 SessionSuppressionRegistry가 받는 channel 문자열을 여기서 고정한다(S2 9.4 표의 일곱 채널 + 3.7절의 템플릿 검증 채널).

| channel 리터럴 | 대응 S2 9.4 행 |
|---|---|
| loc.unresolvedKey | 미해결 i18n 키 |
| loc.itemNameFallback | 아이템명 API 폴백 |
| loc.templatePlaceholder | 사전 템플릿 자리표시자 위반 |
| market.unknownMaxVolumeCurrency | 미지 maxVolumeCurrency |
| market.leagueOrderAnomaly | 리그 순서 이상 |
| market.categoryMismatch | core.items[].category 불일치 |
| settings.writeBlocked | 쓰기 차단 상태의 갱신 시도 |
| store.extraMatchFault | ExtraMatch 예외 |
| diagnostics.bufferOverflow | `RollingFileSink` 유실 통지의 `RecentErrorRing` 노출 억제(D2, 신규) |


## 15. 구체 상수 목록

신규 D-DL16(총괄). 이미 상위 문서가 값을 고정한 것은 출처만 재확인하고, →S4로 유예된 값은 이 문서가 처음 확정한다(표의 "출처" 열에 "신규"로 표시).

### 15.1 창·컬러키

| 상수 | 값 | 출처 |
|---|---|---|
| 컬러키 RGB | R=255,G=0,B=255 (클래식 마젠타) | 신규, **잠정으로 표기(F3)** — 팔레트가 시세 표시에 쓰는 흰색/회색/상승녹색/하락적색 계열과 절대 겹치지 않는 값으로 선택(S3 4.0.1 함정 2). **F3 정정**: 이 비충돌 주장은 §19.5가 팔레트 전체를 미루는 것과 같은 문서 안에서 성립할 수 없다 — 존재하지 않는 팔레트에 대해 비충돌은 논증되지 않는다. 마젠타는 통상적인 시세 팔레트(흰/회/녹/적)와 충돌할 가능성이 낮다는 상식적 근거로 잠정 채택하며, §19.5의 팔레트 확정 시 이 값도 함께 재검증한다 |
| 컬러키 COLORREF 인코딩 | 0x00FF00FF (0x00BBGGRR 순서, R=B=255라 대칭) | 신규, Win32 SetLayeredWindowAttributes 규약 |
| LWA_ALPHA 계산 | byte alpha = (byte)Math.Round(settings.Window.Opacity * 255) | 신규, S3 4.0 |
| window.x/y 기본값 | 100 / 100 | HLD 7절 |
| window.width/height 기본값 | 420 / 500 | HLD 7절 |
| window.width/height 클램프 | [240, 4000] | S2 8.2 |
| window.opacity 기본값/클램프 | 0.87 / [0.2, 1.0] | HLD 7절 |
| 최소 가시 면적 | 어느 한 작업영역과의 교집합이 (푸터 폭 x 푸터 높이) 이상 | S3 4.5 D-SH8 |
| 푸터 폰트 크기(잠정) | 12px | 신규, 잠정 — `00-shell-measurements.md` §8이 측정한 10/11/12/14px 중 중간값. `HasMinimumVisibleArea(..., Size footerSize)`의 실입력이 지금 필요하므로 임시로 확정한다. **결정 주체·시점(G3)**: 구현 담당자가 실물 1차 사용성 확인(S3 §14 항목12가 요구하는 체감 판독성 검증) 직후 교체 — 팔레트 확정과 같은 실험에 묶는다, 별도 절차를 새로 만들지 않는다 |
| 오버레이 색 팔레트(컬러키 제외) | 잠정 — 흰색 주 텍스트/회색 보조/상승 녹색/하락 적색(시스템 기본 계열, 정확한 헥스값 미정) | 신규, 잠정 — 팔레트 값 자체는 여전히 §19.5가 의도적으로 열어 둔 자리다. 이 행은 "값이 아예 없다"는 지적(G3)에 대해 실험 전까지 쓸 수 있는 자리표시자를 준 것이지, §19.5의 유예를 철회한 것이 아니다 |

### 15.2 폴링·주기

| 상수 | 값 | 출처 |
|---|---|---|
| refreshIntervalMinutes 기본값/클램프 | 5 / [5, 60] | HLD 7절, D11 |
| RateMaxAge | max(30분, 3 x interval) | S2 4.5.3 |
| RowStaleAfter | 2 x interval | S2 4.5.3 |
| HeartbeatStaleAfter | 2 x interval + 1분 | S2 4.5.3 |
| UiTicker 주기 | 30초 | HLD D20, S3 3.2 (오버레이 표시 직후, app.Run 직전 Start 호출) |
| 재폴링 디바운스 창 | 2초 | S2 7.7 |
| 재폴링 최소 간격(직전 라운드 완료 후) | 60초 | S2 7.7, 확인 |
| 카테고리 쿨다운 | interval x min(2^(연속실패-1), 8) | S2 7.7 |
| 중앙값 급변 임계 | ratio > 5 | S2 7.5 D8-e |
| 중앙값 강제 수용 임계 | 연속 2회 거부 후 3회째 수용 | S2 7.5 |
| FieldMissingRatio 임계 | skips.Total / RawLineCount > 0.20 | S2 5.5.4 |
| 소표본 예외 | RawLineCount < 5 | S2 5.5.4 |

### 15.3 HTTP·게이트웨이

| 상수 | 값 | 출처 |
|---|---|---|
| NinjaGateway 동시성 상한 | 2 | S2 5.7 D13 |
| NinjaGateway 최소 발행 간격 | 250ms | S2 5.7 |
| 사용자 개시 요청 기아 방지 임계 | 10초 대기 시 다음 슬롯 강제 획득 | S2 5.7 D-MK3 |
| 논리 요청 총 타임아웃 | 90초, 확인 | S2 5.7 |
| 시도별 HTTP 타임아웃 | 10초 | S2 5.8 D13 |
| 재시도 횟수/백오프 기준 | 3회, 지수 2초 기준 + 지터 | S2 5.8 |
| Retry-After 클램프 | [0, 60초] | S2 5.8 |
| MinPrice | 1e-9m | S2 4.2 D-PR8 |
| User-Agent | `PoeOverlayPriceTracker/1.0` | 신규(§C) — S2 §5.8이 "식별 가능한 고정 문자열"만 요구했다. `IHttpClientFactory`의 명명 클라이언트 구성(Composition)에서 1회 설정 |
| 리그 목록 엔드포인트 | `GET https://poe.ninja/poe1/api/economy/leagues` | 측정 — `00-api-contract.md` §1 |
| 카테고리 개요 엔드포인트 | `GET https://poe.ninja/poe1/api/economy/exchange/current/overview?league={league}&type={category}` | 측정 — `00-api-contract.md` §1. `{category}` = `ExchangeCategory.ToString()`(7.4절 D-DL23, S2 §2.2) |

### 15.4 Diagnostics

| 상수 | 값 | 출처 |
|---|---|---|
| 롤링 파일 크기 상한 | 10MB | S2 9.2 |
| 로그 보존 기간 | 14일 | S2 9.2 |
| 로그 버퍼 상한 | 10,000건 | S2 9.2 |
| RecentErrorRing 크기 | 64 | S2 9.3 |
| 억제 채널 상한 | 512건/채널 | S2 9.4 |
| 파손 격리 파일 보관 개수 | 10개 | S2 8.7 |

### 15.5 Settings 쓰기

| 상수 | 값 | 출처 |
|---|---|---|
| 디바운스 창 | 1초, 확인 | S2 8.6 |
| 원자적 쓰기 재시도 횟수 | 3회 | S2 8.5 |
| 재시도 백오프 간격 | 50ms, 100ms, 200ms(누적 지연) | 신규 — S2가 "짧은 백오프"로만 남긴 값을 확정 |
| 종료 flush 실패 흔적 파일 이름 | `settings.flush-failure.trace` (로그 디렉터리, 4.6절 `DiagnosticsStartupState.SettingsFlushFailureTracePath`가 가리키는 파일) | 신규(§C) — S2 §8.6은 "종료 시 쓰고 기동 시 읽는다"고만 하고 경로 프로퍼티만 명명했다. 내용은 실패 시각(UTC ISO 8601) 한 줄 |

### 15.6 단일 인스턴스 신호 — 신규 확정

| 상수 | 값 | 출처 |
|---|---|---|
| 명명 뮤텍스 이름 | PoeOverlay.SingleInstance.Mutex | 신규 |
| RegisterWindowMessage 이름 | PoeOverlay.SingleInstance.Signal.v1 | 신규 |
| 메시지 전용 창 클래스 이름 | PoeOverlay.MessageOnlyWindow | 신규, S3 3.2 "클래스 이름 문자열 자체 S4로 유예"를 확정 |
| ack 센티널 값 | 0x3039 (12345) | 측정 — 00-shell-measurements.md 10.1절이 이미 이 값으로 프로브를 구동해 참 핸들러 실행을 판별했다 |
| FindWindowEx 재시도 | 최대 3회, 100ms 간격 | 신규 |
| SendMessageTimeout 타임아웃/재시도 | 2,000ms x 최대 3회(SMTO_ABORTIFHUNG) | 신규 — S3 3.2 "총 2~3회, 각 수 초 이내"를 확정. 최악 6초가 8~11단계(펌프 시작 전 구간)를 안전하게 덮는다(정상 기동은 수백 ms) |
| 무응답 판정 | 위 재시도를 모두 소진해도 센티널 불일치 | S3 D-SH18 |

### 15.7 트레이

| 상수 | 값 | 출처 |
|---|---|---|
| NIM_ADD 초기 재시도 | 최대 3회, Thread.Sleep(500ms) 간격(동기, D-SH5) | 신규 — S3 D-SH5 "예: 3회, 총 대기 시간이 초 단위"를 확정 |
| 펌프 도는 시점 재등록(D-SH12) | 최대 3회, Task.Delay(1000ms, TimeProvider) 간격(비동기) | 신규 |
| NotifyIcon.Text 최대 길이 | 63자(과거 WinForms 한계, HLD D21 인용) | 신규 — S3 14절 항목 3을 확정. 조립기가 초과분을 자른다 |
| 이동 모드 워치독 비활동 임계 | 5분 | 신규 — HLD 3.3절 "비활동 임계(S4)"를 확정. 드래그·리사이즈 조작이 5분간 없으면 자동 OFF |

### 15.8 뷰모델 임계

| 상수 | 값 | 출처 |
|---|---|---|
| ViewModelRefreshFailing 연속 실패 임계 N | 5 | 신규 — S3 10.1절 "N회(S4, 예 5회)"를 확정 |
| SettingsViewModel 검색 디바운스 창 | 250ms, 확인 | S3 7.4 |
| 트레이 표시 경로 반복 실패 승격 임계 | 3회 연속 실패 시 네이티브 MessageBox | S3 10.1(HLD D12) |
| `SearchOptions.Limit` 상한 | 200 | S2 §6.7 |

### 15.9 Win32 P/Invoke 상수 (A3, §C)

12.3절 `Win32Constants`/`ExtendedStyleBits`/`LwaFlags`와 문자 단위로 일치한다 — 초판은 이 자리를 "15절 상수표 참조"로 비워 둔 채 15절에 값을 싣지 않았다(A3).

| 상수 | 값 | 출처 |
|---|---|---|
| GWL_EXSTYLE | -20 | 신규, SDK 표준값 |
| WS_EX_LAYERED | 0x00080000 | 측정 — `00-shell-measurements.md`, `GWL_EXSTYLE`=`0x08080028`의 성분 |
| WS_EX_TRANSPARENT | 0x00000020 | 측정, 위와 동일 근거 |
| WS_EX_NOACTIVATE | 0x08000000 | 측정, 위와 동일 근거 |
| LWA_COLORKEY | 0x00000001 | 신규, SDK 표준값 |
| LWA_ALPHA | 0x00000002 | 신규, SDK 표준값 |
| HWND_MESSAGE | `new IntPtr(-3)` | 신규, SDK 표준값 |
| SMTO_ABORTIFHUNG | 0x00000002 | 신규, SDK 표준값 — S3 §3.2가 요구한 플래그 |

### 15.10 부팅 감시

| 상수 | 값 | 출처 |
|---|---|---|
| BootWatchdog 타임아웃 | 15초 | 신규(§C) — §18.1이 본문에서 이미 확정한 값인데 §18.1 스스로 "15절에 추가"라 적어 놓고 추가하지 않았다. 이 행이 그 약속을 이행한다 |



## 16. 테스트 계획과 배치

신규 D-DL17. S2 11절/11.8절이 나열한 표의 각 행이 어느 파일의 어느 메서드로 가는지 확정한다. 명명 규칙: {모듈}Tests.cs 파일 안에 [Theory]/[Fact] 메서드 `{표기ID}_{짧은설명}`. ID가 소문자 프라임(예: S2′)을 쓰면 파일명·메서드명에서는 Prime으로 옮긴다(예: S2Prime).

### 16.1 Pricing

| 파일 | S2 절 | 커버 ID |
|---|---|---|
| Pricing/PricingEngineFormatTests.cs | 11.1 | P1~P14 (FR-04-4 다섯 행 결정 절차, Theory로 묶는다) |
| Pricing/NumberFormatterTests.cs | 11.2 | 대역표 7행(Theory, 이름 없는 행은 입력값을 메서드명에 새긴다. 예: Num_ZeroPointFive_FormatsAsZeroThreeDigits) |
| Pricing/ResolveCurrencyTests.cs | 11.3 | R1~R7 |
| Pricing/ChangeDisplayTests.cs | 11.4 | 8행(0.05 경계 포함, 1e300 포함) |
| Pricing/RelativeTimeTests.cs | 11.5 | 5행 |

### 16.2 Localization

| 파일 | S2 절 | 커버 ID |
|---|---|---|
| Localization/FallbackChainTests.cs | 11.6 | L1~L10 |
| Localization/PriceTemplateFallbackTests.cs | 11.11 | C1~C7, 14절 카탈로그와 PriceTemplates 상수의 문자 단위 일치(C1)를 리플렉션으로 순회 단언 |
| Presentation/UiStateTemplateFallbackTests.cs | S3 9.3, 신규 | UiStateTemplates 상수와 14.3절 표의 일치를 C1과 동형으로 단언 |

### 16.3 Market

| 파일 | S2 절 | 커버 ID |
|---|---|---|
| Market/CategoryFetchTests.cs | 11.7 | M1~M6, M9~M11, **M10Prime**(전 행이 문자열 값 -> ElementFaultRatio, E2) (구조 검사 순서, 원소별 역직렬화) |
| Market/JoinTests.cs | 11.7 | M6, M7(사전 1회 구축 계수 단언 포함 — E1, 아래 참조) |
| Market/DeserializationBoundaryTests.cs | 11.7 | **M8**(core 키 자체 없음 -> Deserialization, E2), **M12(신규 개별 파일 아님, 이 파일의 한 메서드) — {"core":null,...}가 Deserialization으로 귀결함을 단언. 2프라임 골격 널 검사 회귀**, **M12Prime**({"core":{...},"lines":null} -> Deserialization), **M12DoublePrime**(본문이 null 리터럴 -> Deserialization) |
| Market/LeagueListTests.cs | 11.7 | M13~M15 |
| Market/RetryAfterTests.cs | 11.7 | M16~M18 |
| Market/NinjaGatewayTests.cs | 11.7 | M19~M21, M20Prime |
| Market/JsonContextOptionsTests.cs | 11.7 | **M22 — NinjaJsonContext.Default.Options의 다섯 값(7.2절)을 직접 단언, JsonSerializerDefaults.Web 혼입 회귀** |
| Market/BoundaryCatchTests.cs | 11.7 | M23, D-MK4 |

### 16.4 Store

| 파일 | S2 절 | 커버 ID |
|---|---|---|
| Store/CommitValidationTests.cs | 11.8 | **S0 — 기동 후 BeginNewLeague -> CommitCategory 커밋이 착지함을 단언, B1 회귀**. S1, S2, S2Prime, S2DoublePrime, **S2TriplePrime**(`Items`에 `default(ItemId)` 키 -> `EmptyItemId` 거부, Release에서도 — §13.4가 코드를 정의한 자리, E2), S3, S4, S4Prime |
| Store/ConcurrencyTests.cs | 11.8 | S5 |
| Store/ApplyFaultTests.cs | 11.8 | S6, S7, S7Prime, S7DoublePrime |
| Store/CommitRejectedConditionTests.cs | 11.8 | S8, S8Prime |
| Store/SearchTests.cs | 11.8 | S9~S15 |
| Store/SnapshotChangedInvariantTests.cs | 11.8 | **S16 — 임의 명령 적용 후 SnapshotChanged가 정확히 1회 발신됨을 단언(AP->EV, S3 13-41)** |
| Store/ConditionStorageGroupTests.cs | 11.8 | **S17 — SetCondition(ViewModelRefreshFailing, ...)이 거부되지 않고 적용됨을 단언(S3 P4/B3 전제 회귀)** |

### 16.5 Polling

| 파일 | S2 절 | 커버 ID |
|---|---|---|
| Polling/HeartbeatTests.cs | 11.9 | **PL0 — 기동 직후 30초 경과해도 PollingStopped == false, default(DateTimeOffset) 회귀**. PL1~PL4 |
| Polling/ContextValidationTests.cs | 11.9 | PL5~PL9, PL11, PL12 |
| Polling/CooldownTests.cs | 11.9 | PL13, PL14 |
| Polling/RepollDebounceTests.cs | 11.9 | PL15~PL17 |
| Polling/PeriodChangeTests.cs | 11.9 | PL18 |
| Polling/LeagueTransitionTests.cs | 11.9 | PL19, PL24 |
| Polling/TriggerChannelTests.cs | 11.9 | **PL20 — 틱이 이긴 라운드 직후 재폴링 요청이 유실 없이 실행됨을 단언, B7 회귀** |
| Polling/CancellationTests.cs | 11.9 | PL21~PL23, PL25 |
| Polling/ForcedAcceptanceTests.cs | 11.9 | PL8, PL8Prime |
| Polling/RateInheritanceTests.cs | 11.9 | PL9, PL10 |

### 16.6 Settings

| 파일 | S2 절 | 커버 ID |
|---|---|---|
| Settings/LoadValidationTests.cs | 11.10 | SE1, SE2, SE4~SE10 |
| Settings/AcknowledgeTests.cs | 11.10 | SE3, **SE3Prime — Acknowledge()가 Unreadable에서 거부됨을 단언**, SE3DoublePrime |
| Settings/EquatableArrayTests.cs | 11.10 | **SE13, SE13Prime, SE13DoublePrime — HashSet 중복 제거, object.Equals, 생성 시 복사 회귀** |
| Settings/ChangeNotificationTests.cs | 11.10 | SE11, SE12 |
| Settings/AtomicWriteTests.cs | 11.10 | SE14~SE18 |
| Settings/SettingsWriteDtoMapperTests.cs | 신규(19.1절 발견에 대한 회귀) | ToWriteDto가 EquatableArray/ItemId/CategoryRef/DisplayCurrency?를 평평한 JSON으로 정확히 왕복시킴을 단언 — 10.2절 키 표와 문자 단위 일치 |

### 16.7 Presentation (net8.0이므로 Core.Tests가 도달 가능)

| 파일 | 대응 절 | 내용 |
|---|---|---|
| Presentation/SnapshotFanoutMergeTests.cs | S3 8.2, 측정 R2 | 병합 규약 — 소규모 스트레스(생산자 여러 스레드 x 다회, 유실 0건). 80만 회 규모의 원측정은 재현하지 않되(CI 시간), 유실 0건 단언은 유지 |
| Presentation/SnapshotFanoutReentrancyTests.cs | S3 8.4, 측정 §10.3 | 경계 트리거+래치 구현이 반복 실패에서 유한 패스 후 수렴함을 단언 — **상한 패스 수 N=7**(E4, `00-shell-measurements.md` §10.3 실측값)로 고정 assert. `>=N` 레벨 구현으로 회귀하면 실패하도록 정확히 7을 assert하며, "N 이하"가 아니라 "정확히 7 이하로 수렴"임을 단언한다 |
| Presentation/DerivedConditionsTests.cs | S3 9.2 | PollingStopped/RatePending/RowStale/ClassifyRow 네 순수 함수 |
| Presentation/ViewModelRefreshFailingLatchTests.cs | S3 10.1, B3 | 경계(false->true/true->false)에서만 Set이 호출됨을 단언 — N-1회 실패 시 미호출, N회째 정확히 1회 호출 |

### 16.9 Diagnostics (E3, 신규)

§2.1 배치에 `tests/PoeOverlay.Core.Tests/Diagnostics/` 폴더가 있었는데 이 절이 초판에 없어 §4.1의 로그 와이어 형식과 D-DG1(포화 시 유실)의 동작이 무검증이었다.

| 파일 | 대응 절 | 내용 |
|---|---|---|
| Diagnostics/LogLineFormatterTests.cs | 4.1절 | 로그 줄 형식(고정폭 접두 + key=value 꼬리, 이스케이프 규칙)의 문자 단위 단언 |
| Diagnostics/RollingFileSinkOverflowTests.cs | 4.3절, D-DG1, D2(신규) | 상한 초과 시 최고참 항목 폐기 + `LogBufferOverflow` 유실 통지가 상한 무시하고 큐잉됨을 단언. **유실 통지의 LogLevel이 Warning임을 단언**(D2 회귀) |
| Diagnostics/RecentErrorRingTests.cs | 4.4절 | Warning 이상만 담김, 용량 64에서 최고참 폐기 |
| Diagnostics/SessionSuppressionRegistryTests.cs | 4.5절, 14.8절 | `ShouldReport`의 채널별 1회 억제, `DumpTotals` |

### 16.8 공통 규약

FakeTimeProvider(Microsoft.Extensions.TimeProvider.Testing)로 전부 구동한다. Task.Delay/Thread.Sleep 직접 호출 금지(S2 11절). Market 테스트는 HttpMessageHandler 스텁 + 00-api-contract.md 실측 본문을 고정 자산(tests/PoeOverlay.Core.Tests/Market/Fixtures/*.json)으로 둔다.

**E1 — M7이 공허하게 통과할 수 있었던 자리를 닫는다.** 조인은 `MarketClient`의 private 경로 안이고 `CategorySnapshot`은 `JoinMissCount`만 갖는다 — "사전을 1회만 구축한다"(선형 탐색 금지)를 검사할 수 있는 표면이 없었다. `MarketClient`에 테스트 전용 `internal` 계수 훅을 둔다: `internal int JoinDictionaryBuildCount { get; private set; }`(7.4절에 필드로 추가, 조인 사전을 구성할 때마다 증가) — `InternalsVisibleTo`(2.2절, D-DL0-1)로 `Market/JoinTests.cs`가 직접 읽는다. `Ok` 카테고리를 여러 항목으로 1회 조회한 뒤 `JoinDictionaryBuildCount == 1`을 단언하면 선형 탐색(항목마다 순회)으로 퇴행해도 값이 늘지 않아 테스트가 계속 공허하게 통과하는 일은 없다 — 오히려 사전이 항목 수만큼 재구축되면 값이 커지므로 그 실패 모드를 직접 잡는다.


## 17. 추적표 — S2/S3의 모든 "S4로 유예" 표시가 이 문서 어디에서 닫히는지

신규 D-DL18. 두 문서의 헤더가 선언한 포괄 유예("메서드 시그니처·JSON 속성명·오류 코드 문자열·테스트 프로젝트 배치·XAML 마크업")는 3~16절 전체가 나눠서 닫으므로 표에 넣지 않는다. 개별 지점만 나열한다.

| 출처 | 위치 | 유예 내용 | 이 문서의 discharge |
|---|---|---|---|
| S2 | 3.6절 | Pricing이 쓰는 키 전체 카탈로그 | 14.1, 14.2, 14.3절 |
| S2 | 5.2절 | JsonPropertyName과 컨텍스트 배치 | 7.1, 7.2절 |
| S2 | 8.1절 | firstRunAcknowledged 키 이름 | 10.2절 |
| S2 | 11절 | 프로젝트 배치·명명 | 2절, 16절 |
| S2 | 11.7절 M7 | 선형 탐색 금지의 계수 단언 | 16.3절(Market/JoinTests.cs) |
| S3 | 2.2절 | PollingStopped 두 갈래 구별 문구·판정 | 14.4절(ui.state.pollingStoppedStale/Exited), 18.4절(판정 조건) |
| S3 | 3.2절 D-SH4 | 메시지 전용 창 클래스 이름 문자열 | 15.6절 |
| S3 | 3.2절 D-SH18 | ack 센티널 값 | 15.6절(측정 §10.1의 0x3039를 그대로 채택) |
| S3 | 3.2절 M6 | 무응답/지연 구별 대화상자 문구 | 18.2절(NativeDialogText) |
| S3 | 3.2절 | SendMessageTimeout 재시도 횟수·간격 | 15.6절 |
| S3 | 3.1절 D-SH19 | 부팅 정지 감시 방식 | 18.1절(BootWatchdog) |
| S3 | 4.0.1절 함정 2 | 컬러키 정확한 값 | 15.1절 |
| S3 | 4.4.2절 D-SH16 | "외 n개 더" 두 변형 문구·조건 판정 | 18.3절 |
| S3 | 6.5절 D-SH6 | firstRunAcknowledged 키 이름(S3측 인용) | 10.2절 |
| S3 | 9.3절 | ui.state.*/ui.tray.* 이중화 테스트 프로젝트 배치 | 16.2절 |
| S3 | 10.1절 D-PS10 | 연속 실패 임계 N | 15.8절(N=5) |
| S3 | 13-30행 (HLD 3.3절 인용) | 이동 모드 워치독 비활동 임계 | 15.7절(5분) |
| S3 | 13-38행 (HLD 6.4절 인용) | CommitRejected 정확한 문구 | 14.4절 |
| S3 | 14절 항목 3 | NotifyIcon.Text 길이 상한 | 15.7절(63자) |
| S3 | 14절 항목 6 | firstRunAcknowledged 스키마 키·위치 | 10.2절 |
| HLD | 3.3절 | 이동 모드 워치독 비활동 임계(S3와 동일 지점) | 15.7절 |
| HLD | 7절 | firstRunAcknowledged 키 이름(HLD측 인용) | 10.2절 |
| S3 | 14절 항목 13 | 렌더링 품질 검증 도구·자동화 배치 → S4 | 2.4절(Shell 전용 테스트 프로젝트를 두지 않으므로 화면 캡처 기반 렌더링 검증은 자동화 대상 밖 — RTB 무효 경고만 유지, F5 정정 — 초판 §17에 이 행이 없었다) |

**의도적으로 열어 둔 자리 하나** — S3 14절 항목 12("색 팔레트·폰트 크기 선택 → S4")는 이 문서의 범위 선언(헤더 "범위 밖: 오버레이 색상 팔레트 전체")과 충돌하지 않는다. 컬러키 한 값만 확정하고(15.1절) 나머지 팔레트는 XAML 마크업과 함께 다음 단계로 넘긴다 — 실사용 판독성 실험(항목 12 자체가 요구하는 선행 조건)이 아직 없으므로 지금 팔레트를 확정하면 근거 없는 색이 된다.


## 18. 보강 — 앞 절이 인용하는 신규 확정 사항

이 절의 항목들은 3~16절이 이미 써 버린 자리(§18.x 인용)를 뒤에서 채운다 — 부록이 아니라 본문의 연속이다. 추가 방식으로만 작성된 문서 특성상 여기 모았다.

### 18.1 부팅 정지 감시 — BootWatchdog (D-SH19 discharge)

신규 D-DL19. host.Start()가 예외 없이도 끝내 반환하지 않고 멈추는 경우(교착, 무한 대기)를 감시한다.

```
internal sealed class BootWatchdog : IDisposable
{
    public BootWatchdog(TimeProvider timeProvider, Action onTimeout);
    public void Arm();          // 5번 단계(host.Start() 직전) 호출
    public void Disarm();       // Store.StartAsync 완료 직후 호출 — 정상 경로
    public void Dispose();
}
```
타임아웃 15초(상수, 15절에 추가). Arm 이후 15초 안에 Disarm이 불리지 않으면 onTimeout이 BootFailureGuard.ShowFatalMessageBox(state: 그 시점까지 모은 DiagnosticsStartupState, exception: null)를 호출한다 — 예외 경로(§12.2)와 같은 최종 표시 메서드를 공유한다. 근거: 15초는 정상 기동(수백 ms~수 초, **S3 §3.2** "정상 기동은 8~11단계를 밀리초~수백 밀리초 안에 통과" — F4 정정, 초판은 이 문장을 HLD §3.5로 잘못 귀속시켰다)의 수십 배 여유이며, 사용자가 "exe가 아무것도 하지 않는다"고 느끼기 시작하는 체감 임계(수 초~10여 초)보다 살짝 길게 잡아 오탐(정상인데 느린 디스크에서 격리 파일 10개를 순회하는 경우 등)을 피한다.

### 18.2 무응답/지연 구별 대화상자 문구 — NativeDialogText (M6 discharge)

신규 D-DL20. 이 문구를 보여주는 프로세스(신호를 보내는 두 번째 인스턴스)는 신호 전송 실패 시 즉시 종료하는 경로이므로 Localization을 로드하지 않는다 — ILocalizer가 아직 존재하지 않는다. 따라서 이 문구는 지역화 대상이 아니라 영문 고정 리터럴이다(1차 릴리스가 영문만 채우는 것과 같은 이유가 아니라, 애초에 사전에 접근할 수 없는 프로세스 단계이기 때문).

```
internal static class NativeDialogText
{
    public const string InstanceUnreachable =
        "PoE Market Price Tracker did not respond in time. If it's already running, it should appear shortly. " +
        "If the problem continues, check the log folder:\n{0}";
    public const string BootFailed =
        "PoE Market Price Tracker failed to start.\n{0}\nCheck the log folder if it exists:\n{1}";
}
```
`{0}`/추가 인자는 `string.Format`으로 채운다(로그 폴더 경로, 예외 메시지) — Pricing의 D-PR4와 같은 이유로 인자는 항상 이미 서식된 문자열이다. InstanceUnreachable이 §3.2 M6이 요구한 "도달 불가를 단정하지 않는" 완화된 문구다.

### 18.3 "외 n개 더" 두 변형 — 오버레이 지역화 키 (D-SH16 discharge)

신규 D-DL21. 14절 카탈로그가 등재하지 못한 두 키를 여기서 보강한다 — 14.3절 표에 이 두 행이 추가되는 것으로 간주한다.

| 키 | 인자 | 영문 값 | 조건 |
|---|---|---|---|
| ui.overlay.moreRows | 1 (숨겨진 행 수) | +{0} more | heightMode == Auto (화면 여백 부족으로 잘림) |
| ui.overlay.moreRowsExplicit | 1 (숨겨진 행 수) | +{0} more \u2014 adjust height in settings | heightMode == Explicit && hiddenCount > 0 |

11.8절 UiStateTemplates에 다음 상수를 추가한다(원 목록에 없었다 — 이 절이 보강):
```
internal static class UiStateTemplates
{
    // ...11.8절의 기존 여덟 상수에 이어서...
    public const string MoreRows         = "+{0} more";
    public const string MoreRowsExplicit = "+{0} more \u2014 adjust height in settings";
    public const string FetchFailedRow   = "update failed {0}";
    public const string FetchFailedBadge = "{0} categories failed to update";
    public const string LoggingUnavailableWithPath = "log file unavailable \u2014 path: {0}";
}
```
뒤 세 상수(FetchFailedRow/FetchFailedBadge/LoggingUnavailableWithPath)는 14.3절 표가 이미 요구했으나 11.8절 원 목록이 빠뜨렸던 것이다(19.3절 발견 사항).

### 18.4 PollingStopped 갈래 판정 — 정확한 조건 (S3 2.2절 discharge)

신규 D-DL22.
```
PollingStoppedBranch(heartbeat: Heartbeat, now: DateTimeOffset, refreshIntervalMinutes: int) -> (bool IsStopped, bool IsExited):
    if heartbeat.LoopExited: return (true, true)                                            // Exited 갈래
    if heartbeat.LastRoundAttemptAt is null: return (false, false)                            // 정체 아님, PL0 회귀
    if now - heartbeat.LastRoundAttemptAt > StalenessPolicy.HeartbeatStaleAfter(refreshIntervalMinutes):
        return (true, false)                                                                  // Stale 갈래
    return (false, false)
```
`IsExited`면 `ui.state.pollingStoppedExited`(인자 없음), 아니면(`IsStopped && !IsExited`) `ui.state.pollingStoppedStale`(인자: `Pricing.Relative(heartbeat.LastRoundAttemptAt.Value, now, templates)`)를 쓴다. `DerivedConditions.IsPollingStopped`(11.8절)는 이 함수의 `IsStopped`만 반환하는 얇은 래퍼다 — 갈래 구별이 필요한 설정 창·오버레이 배너 조립부만 `PollingStoppedBranch` 전체를 부른다.


## 19. 발견 사항과 잔여 갭

S2 §12·S3 §13과 같은 형식이다. 이 문서가 상위 결정을 뒤집은 항목은 없다 — 전부 "결정되지 않았던 자리"다.

| # | 항목 | 성격 | 처분 |
|---|---|---|---|
| 19.1 | **S2 8.4절의 "쓰기는 소스 생성 직렬화기를 그대로 쓴다"는 AppSettings를 직접 직렬화한다는 뜻으로 읽히지만, 그러면 계약과 다른 JSON이 나온다**(F1 정정 — "컴파일되지 않는다"가 아니라 **컴파일은 되고 잘못된 JSON을 낸다**: `{"id":{"value":"divine"},"category":{"raw":"Currency","known":1}}`처럼 값 타입이 중첩 객체로, enum이 숫자로 나온다) — WatchlistEntry.Id(ItemId)/Category(CategoryRef)/DisplayCurrency(Domain 열거형?)가 System.Text.Json 기본 처리로는 10.2절의 평평한 스키마를 만들지 못한다 | **구현 불가능 지점(가장 가치 있는 발견)** | 10.7절에서 SettingsWriteDto/WatchlistEntryWriteDto와 SettingsWriteDtoMapper.ToWriteDto를 신설해 닫았다(A2가 그 DTO 자신의 CS8618을 마저 고쳤다). S2를 개정하지 않는다 — 이 문서가 S2가 남긴 구현 세부(직렬화기를 "그대로" 쓴다는 표현의 정확한 의미)를 채우는 것으로 충분하다 |
| 19.2 | **FailureKind.ElementFault 열거 멤버가 죽은 코드다** — S2 5.5.3/5.5.4의 판정 순서를 전수 추적하면, 원소 단위 결함은 항상 SkipCounts.ElementFault(카운터)로만 계상되고 카테고리 단위 FailureRecord.Kind로는 FieldMissingRatio(Code=ElementFaultRatio, 20% 초과 시) 또는 아무 실패도 아닌 것(소표본 예외, M10 회귀)으로만 귀결된다. Kind=ElementFault 자체를 만드는 생산자가 없다 | 상위 문서의 사소한 미비 | 13.1절에 Code 리터럴은 정의해 두되(혹시 모를 호출 대비) 미사용으로 명시했다. **개정 권고 — S2 2.12절의 FailureKind 열거에서 ElementFault를 제거하거나, 의도했던 생산 지점을 S2가 명시할 것.** 이 문서는 열거를 바꾸지 않았다(그럴 권한이 없다) |
| 19.3 | **11.8절 UiStateTemplates 원 목록이 14.3절 카탈로그가 요구하는 세 상수(FetchFailedRow, FetchFailedBadge, LoggingUnavailableWithPath)를 빠뜨렸다** | 이 문서 자신의 작성 중 발견 | 18.3절에서 다섯 상수(위 셋 + MoreRows + MoreRowsExplicit)를 추가로 확정해 닫았다. 구현 시 11.8절 코드 블록과 18.3절 코드 블록을 합쳐 하나의 UiStateTemplates로 만든다 |
| 19.4 | **11.5절 SettingsViewModel.RetryTrayRegistrationCommand의 주입 형태가 "위임"이라고만 적혀 시그니처가 비어 있었다** | 이 문서 자신의 작성 중 발견 | 12.4절이 이미 TrayIconHost.TryReregisterAsync(CancellationToken) -> Task<bool>을 확정했으므로, SettingsViewModel 생성자는 `Func<CancellationToken, Task<bool>> retryTrayRegistration` 매개변수를 받아 그 델리게이트를 커맨드 본문에서 호출한다. Composition의 ServiceRegistration이 `sp => sp.GetRequiredService<TrayIconHost>().TryReregisterAsync`를 바인딩한다 |
| 19.5 | 오버레이 색상 팔레트 전체(컬러키 제외) | 의도적으로 열어 둠 — 이 문서의 범위 선언 | S3 14절 항목 12가 스스로 "체감 판독성 검증 후 반영"을 전제 조건으로 달았으므로, 그 실험이 없는 지금 팔레트를 고정하면 근거 없는 색이 된다. XAML 마크업과 함께 다음 단계(구현 중 또는 S5)로 넘긴다. **상위 문서가 예약한 결정이므로 이 문서가 대신 발명하지 않는다** |
| 19.6 | FieldMissingRatio 사유 분화에서 Duplicate가 지배 원인일 때(또는 동률일 때) 전부 기본 코드로 뭉개진다 — S2 5.5.4는 이 경우를 언급하지 않았다 | 이 문서가 명시적으로 결정(13.2절 D-DL11) | 재론이 필요하면 실사용 로그에서 Duplicate 지배 사례가 실제로 관측된 뒤 판단한다(§15.2의 다른 임계값들과 같은 성격 — "실사용 후 조정") |
| 19.7 | Interop/의 CA2007 개별 재활성 지점(2.3절 세 번째 행)이 현재 빈 집합 | 확인 사항, 결함 아님 | 구현 중 실제로 그런 지점이 생기면 그때 표를 채운다. 지금 억지로 만들 필요가 없다 |
| 19.8 | **AppConditionKind.FetchFailed가 고아다**(D3, 제2판이 새로 발견) — 저장 그룹으로 선언됐으나 생산자도 소비자도 없다. 실제 실패 목록 표시는 `CategoryStatuses`에서 `DerivedConditions.ClassifyRow(...)`로 파생되며 `Conditions`를 건드리지 않는다 — `snapshot.Conditions[FetchFailed]`는 영원히 부재한다. 19.2가 `FailureKind.ElementFault`에 대해 스스로 수행한 추적을 이 문서 자신에게는 적용하지 못했던 자리다 | 상위 문서의 사소한 미비, 19.2와 동형 | 3.3절 열거 주석에 고아임을 표시해 두되(값 재배열 금지이므로 자리는 그대로 둔다) 열거 자체를 바꾸지 않는다. **개정 권고 — S2 §2.11에서 `FetchFailed`를 파생 그룹으로 옮기거나, 생산자·소비자와 회귀 테스트를 새로 명시할 것.** 어느 쪽이든 열거 멤버의 소속 그룹(저장/파생 경계, 값 순서에 영향)이 바뀌므로 이 문서가 대신 결정하지 않는다 |

**요약**: 42/42 요구사항 충족은 이 문서가 재논증하지 않는다(S2/S3가 이미 확정) — 이 문서는 그 결정들이 실제로 컴파일되는 형태를 갖추게 했을 뿐이다. 19.1이 유일하게 "상위 문서의 문장이 문자 그대로는 성립하지 않는" 자리였고, 나머지는 전부 상위 문서가 의도적으로 남겨 둔 빈칸이었다.

**S2 개정 요구 2건(제2판 확정)** — ① `FailureKind.ElementFault` 제거 또는 생산 지점 명시(§19.2) ② `AppConditionKind.FetchFailed`의 그룹 재배치 또는 생산자 신설(§19.8, D3). 둘 다 열거 멤버의 소속·순서에 관한 결정이라 S2의 권한이며, 이 문서는 어느 쪽도 대신 정하지 않았다.

