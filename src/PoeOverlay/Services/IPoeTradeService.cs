using PoeOverlay.Models;

namespace PoeOverlay.Services;

/// <summary>
/// PoE 거래소 API와 통신하는 서비스 인터페이스입니다.
/// [서비스 로직] 사용자가 직접 구현해야 합니다.
///
/// 구현 시 참고:
/// - Path of Exile Trade API (https://www.pathofexile.com/developer/docs/reference)
/// - poe.ninja API 등 서드파티 가격 데이터 소스
/// </summary>
public interface IPoeTradeService
{
    /// <summary>
    /// 아이템 이름으로 현재 거래소 가격 목록을 검색합니다.
    /// </summary>
    Task<IReadOnlyList<TradeResult>> SearchItemPricesAsync(string itemName, CancellationToken ct = default);

    /// <summary>
    /// 특정 아이템의 가격 히스토리를 가져옵니다.
    /// </summary>
    Task<IReadOnlyList<PriceDataPoint>> GetPriceHistoryAsync(string itemName, int days = 10, CancellationToken ct = default);

    /// <summary>
    /// 아이템 정보(아이콘 URL 포함)를 가져옵니다.
    /// </summary>
    Task<ItemInfo?> GetItemInfoAsync(string itemName, CancellationToken ct = default);
}
