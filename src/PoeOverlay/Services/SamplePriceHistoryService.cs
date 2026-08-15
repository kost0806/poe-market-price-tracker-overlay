using PoeOverlay.Models;

namespace PoeOverlay.Services;

/// <summary>
/// IPriceHistoryService의 샘플 구현입니다.
/// 인메모리 저장소를 사용합니다.
///
/// TODO: 이 클래스를 파일 기반(JSON/SQLite) 또는 실제 저장소를 사용하는 구현으로 교체하세요.
/// </summary>
public class SamplePriceHistoryService : IPriceHistoryService
{
    private readonly Dictionary<string, List<PriceDataPoint>> _store = new();
    private readonly IPoeTradeService _tradeService;

    public SamplePriceHistoryService(IPoeTradeService tradeService)
    {
        _tradeService = tradeService;
    }

    public Task<IReadOnlyList<PriceDataPoint>> LoadHistoryAsync(string itemName, CancellationToken ct = default)
    {
        if (_store.TryGetValue(itemName, out var list))
            return Task.FromResult<IReadOnlyList<PriceDataPoint>>(list);

        return Task.FromResult<IReadOnlyList<PriceDataPoint>>(Array.Empty<PriceDataPoint>());
    }

    public Task SaveDataPointAsync(string itemName, PriceDataPoint dataPoint, CancellationToken ct = default)
    {
        if (!_store.ContainsKey(itemName))
            _store[itemName] = [];

        _store[itemName].Add(dataPoint);
        return Task.CompletedTask;
    }

    public async Task RefreshHistoryAsync(string itemName, CancellationToken ct = default)
    {
        var history = await _tradeService.GetPriceHistoryAsync(itemName, ct: ct);
        _store[itemName] = history.ToList();
    }
}
