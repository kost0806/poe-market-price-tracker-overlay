using PoeOverlay.Models;

namespace PoeOverlay.Services;

/// <summary>
/// 가격 히스토리 저장/캐싱을 담당하는 서비스 인터페이스입니다.
/// [서비스 로직] 사용자가 직접 구현해야 합니다.
///
/// 구현 시 참고:
/// - 로컬 파일(JSON/SQLite) 또는 메모리 캐시 활용
/// - 주기적 갱신 로직 포함 가능
/// </summary>
public interface IPriceHistoryService
{
    /// <summary>
    /// 저장된 가격 히스토리를 로드합니다.
    /// </summary>
    Task<IReadOnlyList<PriceDataPoint>> LoadHistoryAsync(string itemName, CancellationToken ct = default);

    /// <summary>
    /// 새 가격 데이터를 히스토리에 추가합니다.
    /// </summary>
    Task SaveDataPointAsync(string itemName, PriceDataPoint dataPoint, CancellationToken ct = default);

    /// <summary>
    /// 전체 히스토리를 갱신합니다 (API → 로컬 저장).
    /// </summary>
    Task RefreshHistoryAsync(string itemName, CancellationToken ct = default);
}
