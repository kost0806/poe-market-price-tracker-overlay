using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PoeOverlay.ViewModels;

/// <summary>
/// [UI 로직] MainWindow에 대한 ViewModel입니다.
/// 서비스 인터페이스를 통해 데이터를 받아 UI에 바인딩합니다.
///
/// 관심 목록 폴링·시세 조회 서비스는 설계문서 확정 후 주입됩니다.
/// 현재는 창 껍데기만 동작하는 상태입니다. (docs/REQUIREMENTS.md 참고)
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    // ── Bindable Properties ──────────────────────────────────

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

    // ── INotifyPropertyChanged ───────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
