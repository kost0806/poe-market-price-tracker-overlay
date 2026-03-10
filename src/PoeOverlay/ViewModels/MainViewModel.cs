using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using PoeOverlay.Models;
using PoeOverlay.Services;

namespace PoeOverlay.ViewModels;

/// <summary>
/// [UI 로직] MainWindow에 대한 ViewModel입니다.
/// 서비스 인터페이스를 통해 데이터를 받아 UI에 바인딩합니다.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    private readonly IPoeTradeService _tradeService;
    private readonly IPriceHistoryService _historyService;

    public MainViewModel(IPoeTradeService tradeService, IPriceHistoryService historyService)
    {
        _tradeService = tradeService;
        _historyService = historyService;
    }

    // ── Bindable Properties ──────────────────────────────────

    private string _searchItemName = "Exalted Orb";
    public string SearchItemName
    {
        get => _searchItemName;
        set { _searchItemName = value; OnPropertyChanged(); }
    }

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    private PlotModel? _chartModel;
    public PlotModel? ChartModel
    {
        get => _chartModel;
        set { _chartModel = value; OnPropertyChanged(); }
    }

    private BitmapImage? _itemImage;
    public BitmapImage? ItemImage
    {
        get => _itemImage;
        set { _itemImage = value; OnPropertyChanged(); }
    }

    private string _itemDisplayName = "";
    public string ItemDisplayName
    {
        get => _itemDisplayName;
        set { _itemDisplayName = value; OnPropertyChanged(); }
    }

    private string _currentPrice = "";
    public string CurrentPrice
    {
        get => _currentPrice;
        set { _currentPrice = value; OnPropertyChanged(); }
    }

    public ObservableCollection<TradeResult> TradeResults { get; } = new();

    // ── Commands / Actions ───────────────────────────────────

    /// <summary>
    /// 아이템 검색을 수행하고 UI를 갱신합니다.
    /// </summary>
    public async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchItemName)) return;

        IsLoading = true;
        StatusText = "Searching...";

        try
        {
            // 서비스에서 데이터 가져오기 (서비스 로직은 인터페이스 뒤에 숨겨져 있음)
            var itemInfo = await _tradeService.GetItemInfoAsync(SearchItemName);
            var priceHistory = await _tradeService.GetPriceHistoryAsync(SearchItemName);
            var trades = await _tradeService.SearchItemPricesAsync(SearchItemName);

            // UI 갱신 (아래는 모두 UI 로직)
            if (itemInfo != null)
            {
                ItemDisplayName = itemInfo.Name;
                LoadItemImage(itemInfo.IconUrl);
            }

            UpdateChart(priceHistory);
            UpdateTradeResults(trades);

            if (priceHistory.Count > 0)
            {
                var latest = priceHistory[^1];
                CurrentPrice = $"{latest.Price:N0} {latest.CurrencyType}";
            }

            StatusText = $"Found {trades.Count} listings";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 가격 히스토리를 새로고침합니다.
    /// </summary>
    public async Task RefreshAsync()
    {
        IsLoading = true;
        StatusText = "Refreshing...";

        try
        {
            await _historyService.RefreshHistoryAsync(SearchItemName);
            var history = await _historyService.LoadHistoryAsync(SearchItemName);
            UpdateChart(history);
            StatusText = "Refreshed";
        }
        catch (Exception ex)
        {
            StatusText = $"Refresh error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── UI Helper Methods (차트/이미지 구성) ─────────────────

    private void UpdateChart(IReadOnlyList<PriceDataPoint> history)
    {
        var model = new PlotModel
        {
            Title = "Price History",
            TitleColor = OxyColors.LightGray,
            PlotAreaBorderColor = OxyColors.Gray,
            Background = OxyColors.Transparent,
        };

        model.Axes.Add(new DateTimeAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Date",
            TitleColor = OxyColors.LightGray,
            TextColor = OxyColors.LightGray,
            TicklineColor = OxyColors.Gray,
            StringFormat = "MM/dd",
        });

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Price (chaos)",
            TitleColor = OxyColors.LightGray,
            TextColor = OxyColors.LightGray,
            TicklineColor = OxyColors.Gray,
        });

        var series = new LineSeries
        {
            Title = SearchItemName,
            Color = OxyColor.FromRgb(255, 200, 50),
            StrokeThickness = 2,
        };

        foreach (var point in history)
        {
            series.Points.Add(new DataPoint(
                DateTimeAxis.ToDouble(point.Timestamp),
                point.Price));
        }

        model.Series.Add(series);
        model.LegendTextColor = OxyColors.LightGray;

        ChartModel = model;
    }

    private void UpdateTradeResults(IReadOnlyList<TradeResult> trades)
    {
        TradeResults.Clear();
        foreach (var trade in trades)
        {
            TradeResults.Add(trade);
        }
    }

    private void LoadItemImage(string? iconUrl)
    {
        if (string.IsNullOrEmpty(iconUrl)) return;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(iconUrl);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            ItemImage = bitmap;
        }
        catch
        {
            // 이미지 로드 실패는 무시
        }
    }

    // ── INotifyPropertyChanged ───────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
