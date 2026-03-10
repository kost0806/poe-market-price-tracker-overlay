namespace PoeOverlay.Models;

/// <summary>
/// 특정 시점의 아이템 가격 데이터를 나타냅니다.
/// </summary>
public record PriceDataPoint(
    DateTime Timestamp,
    double Price,
    string CurrencyType = "chaos"
);
