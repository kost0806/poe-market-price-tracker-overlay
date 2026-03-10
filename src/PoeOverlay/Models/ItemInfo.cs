namespace PoeOverlay.Models;

/// <summary>
/// PoE 아이템의 기본 정보를 나타냅니다.
/// </summary>
public record ItemInfo(
    string Id,
    string Name,
    string Type,
    string? IconUrl = null
);
