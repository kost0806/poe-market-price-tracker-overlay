using PoeOverlay.Models;

namespace PoeOverlay.Services;

/// <summary>
/// IPoeTradeService의 샘플 구현입니다.
/// 실제 API 호출 없이 하드코딩된 데이터를 반환합니다.
///
/// TODO: 이 클래스를 실제 PoE Trade API를 호출하는 구현으로 교체하세요.
/// </summary>
public class SamplePoeTradeService : IPoeTradeService
{
    public Task<IReadOnlyList<TradeResult>> SearchItemPricesAsync(string itemName, CancellationToken ct = default)
    {
        var results = new List<TradeResult>
        {
            new(new ItemInfo("1", "Exalted Orb", "Currency", "https://web.poecdn.com/gen/image/WzI1LDE0LHsiZiI6IjJESXRlbXMvQ3VycmVuY3kvQ3VycmVuY3lBZGRNb2RUb1JhcmUiLCJ3IjoxLCJoIjoxLCJzY2FsZSI6MX1d/fc05f25452/CurrencyAddModToRare.png"),
                170, "chaos", "SamplePlayer1", DateTime.Now.AddMinutes(-5)),
            new(new ItemInfo("1", "Exalted Orb", "Currency"),
                168, "chaos", "SamplePlayer2", DateTime.Now.AddMinutes(-12)),
            new(new ItemInfo("1", "Exalted Orb", "Currency"),
                175, "chaos", "SamplePlayer3", DateTime.Now.AddMinutes(-30)),
        };

        return Task.FromResult<IReadOnlyList<TradeResult>>(results);
    }

    public Task<IReadOnlyList<PriceDataPoint>> GetPriceHistoryAsync(string itemName, int days = 10, CancellationToken ct = default)
    {
        double[] prices = [150, 148, 155, 160, 158, 165, 170, 168, 175, 180];
        var now = DateTime.Now;

        var history = prices.Select((price, i) =>
            new PriceDataPoint(now.AddDays(-(days - 1 - i)), price)
        ).ToList();

        return Task.FromResult<IReadOnlyList<PriceDataPoint>>(history);
    }

    public Task<ItemInfo?> GetItemInfoAsync(string itemName, CancellationToken ct = default)
    {
        var item = new ItemInfo(
            "1",
            "Exalted Orb",
            "Currency",
            "https://web.poecdn.com/gen/image/WzI1LDE0LHsiZiI6IjJESXRlbXMvQ3VycmVuY3kvQ3VycmVuY3lBZGRNb2RUb1JhcmUiLCJ3IjoxLCJoIjoxLCJzY2FsZSI6MX1d/fc05f25452/CurrencyAddModToRare.png"
        );

        return Task.FromResult<ItemInfo?>(item);
    }
}
