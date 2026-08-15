namespace PoeOverlay.Models;

/// <summary>
/// 거래소 검색 결과를 나타냅니다.
/// </summary>
public record TradeResult(
    ItemInfo Item,
    double Price,
    string CurrencyType,
    string SellerAccount,
    DateTime ListedAt
);
