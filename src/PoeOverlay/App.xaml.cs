using System.Windows;
using PoeOverlay.ViewModels;

namespace PoeOverlay;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── ViewModel 생성 ───────────────────────────────────
        // TODO: 설계문서 확정 후 poe.ninja 시세 서비스와 폴링 호스트를 여기서 구성합니다.
        var viewModel = new MainViewModel();

        // ── Window 생성 및 표시 ──────────────────────────────
        var mainWindow = new MainWindow(viewModel);
        mainWindow.Show();
    }
}
