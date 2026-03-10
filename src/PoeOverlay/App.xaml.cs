using System.Windows;
using PoeOverlay.Services;
using PoeOverlay.ViewModels;

namespace PoeOverlay;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── 서비스 등록 ──────────────────────────────────────
        // TODO: Sample 구현을 실제 구현으로 교체하세요.
        //   예) IPoeTradeService  → PoeNinjaPriceService (직접 구현)
        //       IPriceHistoryService → SqlitePriceHistoryService (직접 구현)
        IPoeTradeService tradeService = new SamplePoeTradeService();
        IPriceHistoryService historyService = new SamplePriceHistoryService(tradeService);

        // ── ViewModel 생성 ───────────────────────────────────
        var viewModel = new MainViewModel(tradeService, historyService);

        // ── Window 생성 및 표시 ──────────────────────────────
        var mainWindow = new MainWindow(viewModel);
        mainWindow.Show();
    }
}
