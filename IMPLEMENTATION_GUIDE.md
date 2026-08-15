# PoE Market Price Tracker Overlay - 구현 가이드

## 프로젝트 구조

```
src/PoeOverlay/
├── Models/                         # 공유 데이터 모델
│   ├── PriceDataPoint.cs           # 가격 데이터 포인트
│   ├── ItemInfo.cs                 # 아이템 정보
│   └── TradeResult.cs              # 거래 검색 결과
│
├── Services/                       # 서비스 레이어
│   ├── IPoeTradeService.cs         # [인터페이스] 거래소 API 통신
│   ├── IPriceHistoryService.cs     # [인터페이스] 가격 히스토리 저장/캐싱
│   ├── SamplePoeTradeService.cs    # [샘플] 하드코딩 데이터 반환
│   └── SamplePriceHistoryService.cs# [샘플] 인메모리 저장소
│
├── ViewModels/                     # UI 데이터 바인딩
│   └── MainViewModel.cs            # [UI 로직] 메인 화면 ViewModel
│
├── App.xaml                        # 앱 설정
├── App.xaml.cs                     # 서비스 등록 & 앱 시작
├── MainWindow.xaml                 # [UI 로직] 메인 화면 레이아웃
└── MainWindow.xaml.cs              # [UI 로직] 순수 UI 이벤트 핸들러
```

---

## 서비스 로직 vs UI 로직 구분

### 서비스 로직 (사용자가 구현)

당신이 직접 구현해야 하는 부분입니다. `Services/` 폴더의 **인터페이스**를 구현하세요.

| 인터페이스 | 역할 | 구현 시 참고 |
|---|---|---|
| `IPoeTradeService` | PoE 거래소 API 호출, 가격 조회, 아이템 정보 조회 | PoE Trade API, poe.ninja 등 |
| `IPriceHistoryService` | 가격 데이터 로컬 저장/로드/갱신 | JSON 파일, SQLite, 메모리 캐시 등 |

서비스 로직이 담당하는 것:
- **API 통신**: HTTP 요청, 응답 파싱, 에러 핸들링
- **데이터 저장**: 파일/DB 읽기쓰기, 캐싱 전략
- **비즈니스 규칙**: 가격 계산, 필터링, 정렬 로직

### UI 로직 (이미 구현됨)

이미 구현되어 있는 부분입니다. 수정할 필요 없이 바로 사용 가능합니다.

| 파일 | 역할 |
|---|---|
| `MainWindow.xaml` | 오버레이 레이아웃 (검색바, 차트, 거래목록, 투명도 조절) |
| `MainWindow.xaml.cs` | 윈도우 드래그, 투명도, 키보드 이벤트 등 순수 UI |
| `MainViewModel.cs` | 서비스 데이터를 받아 차트/목록 등 UI 요소에 바인딩 |

UI 로직이 담당하는 것:
- **데이터 바인딩**: ViewModel → XAML 양방향 바인딩
- **차트 렌더링**: OxyPlot 모델 구성 (축, 시리즈, 스타일)
- **윈도우 동작**: 드래그, 투명도 조절, 닫기
- **사용자 입력**: 검색 버튼, Enter 키, 새로고침

---

## 시작하기

### 1. 빌드 & 실행 (샘플 데이터로 동작 확인)

```bash
cd src/PoeOverlay
dotnet restore
dotnet build
dotnet run
```

샘플 서비스가 하드코딩 데이터를 반환하므로, 실제 API 없이도 UI가 동작합니다.

### 2. 실제 서비스 구현

`IPoeTradeService`를 구현하는 새 클래스를 만드세요.

```csharp
// Services/PoeNinjaTradeService.cs (예시)
using System.Net.Http;
using System.Text.Json;
using PoeOverlay.Models;

namespace PoeOverlay.Services;

public class PoeNinjaTradeService : IPoeTradeService
{
    private static readonly HttpClient Http = new();

    public async Task<IReadOnlyList<TradeResult>> SearchItemPricesAsync(
        string itemName, CancellationToken ct = default)
    {
        // TODO: poe.ninja 또는 PoE Trade API에서 가격 데이터 가져오기
        // 예시:
        // var json = await Http.GetStringAsync(
        //     $"https://poe.ninja/api/data/currencyoverview?league=Standard&type=Currency", ct);
        // var data = JsonSerializer.Deserialize<PoeNinjaResponse>(json);
        // return data.Lines.Select(l => new TradeResult(...)).ToList();

        throw new NotImplementedException();
    }

    public async Task<IReadOnlyList<PriceDataPoint>> GetPriceHistoryAsync(
        string itemName, int days = 10, CancellationToken ct = default)
    {
        // TODO: 가격 히스토리 API 호출
        throw new NotImplementedException();
    }

    public async Task<ItemInfo?> GetItemInfoAsync(
        string itemName, CancellationToken ct = default)
    {
        // TODO: 아이템 정보 조회
        throw new NotImplementedException();
    }
}
```

### 3. 서비스 교체 (App.xaml.cs)

`App.xaml.cs`에서 Sample 구현을 실제 구현으로 교체합니다:

```csharp
// 변경 전 (샘플)
IPoeTradeService tradeService = new SamplePoeTradeService();
IPriceHistoryService historyService = new SamplePriceHistoryService(tradeService);

// 변경 후 (실제 구현)
IPoeTradeService tradeService = new PoeNinjaTradeService();
IPriceHistoryService historyService = new FilePriceHistoryService(tradeService);
```

이것만으로 UI는 자동으로 실제 데이터를 표시합니다.

---

## 데이터 흐름

```
[사용자 입력: "Exalted Orb"]
        │
        ▼
  MainWindow.xaml.cs          ← UI 이벤트 (SearchButton_Click)
        │
        ▼
  MainViewModel.SearchAsync() ← UI 로직 (로딩 상태, 에러 표시)
        │
        ├─→ IPoeTradeService.GetItemInfoAsync()      ← 서비스 로직
        ├─→ IPoeTradeService.GetPriceHistoryAsync()   ← 서비스 로직
        └─→ IPoeTradeService.SearchItemPricesAsync()  ← 서비스 로직
                    │
                    ▼
          MainViewModel (바인딩 프로퍼티 갱신)
                    │
                    ▼
          MainWindow.xaml (자동 UI 갱신)
```

---

## 인터페이스 명세

### IPoeTradeService

```csharp
// 아이템 이름으로 거래소 가격 목록을 검색
Task<IReadOnlyList<TradeResult>> SearchItemPricesAsync(string itemName, CancellationToken ct);

// 특정 아이템의 가격 히스토리 (최근 N일)
Task<IReadOnlyList<PriceDataPoint>> GetPriceHistoryAsync(string itemName, int days, CancellationToken ct);

// 아이템 기본 정보 (이름, 타입, 아이콘 URL)
Task<ItemInfo?> GetItemInfoAsync(string itemName, CancellationToken ct);
```

### IPriceHistoryService

```csharp
// 로컬 저장소에서 히스토리 로드
Task<IReadOnlyList<PriceDataPoint>> LoadHistoryAsync(string itemName, CancellationToken ct);

// 새 데이터 포인트 저장
Task SaveDataPointAsync(string itemName, PriceDataPoint dataPoint, CancellationToken ct);

// API에서 최신 데이터를 가져와 로컬에 저장
Task RefreshHistoryAsync(string itemName, CancellationToken ct);
```

---

## 모델 구조

```csharp
// 가격 데이터 포인트
record PriceDataPoint(DateTime Timestamp, double Price, string CurrencyType = "chaos");

// 아이템 정보
record ItemInfo(string Id, string Name, string Type, string? IconUrl = null);

// 거래 검색 결과
record TradeResult(ItemInfo Item, double Price, string CurrencyType, string SellerAccount, DateTime ListedAt);
```

---

## 구현 체크리스트

- [ ] `IPoeTradeService` 구현 클래스 작성
  - [ ] `SearchItemPricesAsync` - 거래소 가격 목록 검색
  - [ ] `GetPriceHistoryAsync` - 가격 히스토리 조회
  - [ ] `GetItemInfoAsync` - 아이템 정보 조회
- [ ] `IPriceHistoryService` 구현 클래스 작성
  - [ ] `LoadHistoryAsync` - 로컬 데이터 로드
  - [ ] `SaveDataPointAsync` - 데이터 저장
  - [ ] `RefreshHistoryAsync` - API → 로컬 갱신
- [ ] `App.xaml.cs`에서 Sample → 실제 구현으로 교체
- [ ] (선택) AngleSharp 패키지 활성화 (HTML 파싱이 필요한 경우 csproj에서 주석 해제)

---

## 패키지 의존성

| 패키지 | 용도 | 관리 주체 |
|---|---|---|
| `OxyPlot.Wpf` | 차트 렌더링 | UI (이미 설정됨) |
| `AngleSharp` | HTML 파싱 | 서비스 (csproj에 주석 처리됨, 필요 시 활성화) |
| `System.Text.Json` | JSON 파싱 | 서비스 (기본 내장) |
| `System.Net.Http` | HTTP 통신 | 서비스 (기본 내장) |
