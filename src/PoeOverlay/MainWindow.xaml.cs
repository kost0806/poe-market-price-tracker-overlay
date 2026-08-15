using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PoeOverlay.ViewModels;

namespace PoeOverlay;

/// <summary>
/// [UI 로직] MainWindow의 코드비하인드입니다.
/// ViewModel에 위임하지 않는 순수 UI 동작(드래그, 투명도, 키보드 이벤트)만 처리합니다.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.SearchAsync();
    }

    // ── 순수 UI 이벤트 핸들러 ────────────────────────────────

    private void DragGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Content is Border border)
        {
            var alpha = (byte)(e.NewValue * 255);
            var current = ((SolidColorBrush)border.Background).Color;
            border.Background = new SolidColorBrush(Color.FromArgb(alpha, current.R, current.G, current.B));
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SearchAsync();
    }

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await _viewModel.SearchAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshAsync();
    }
}
