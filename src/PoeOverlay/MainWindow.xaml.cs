using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AngleSharp;
using AngleSharp.Dom;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace PoeOverlay;

public partial class MainWindow : Window
{
    private static readonly HttpClient HttpClient = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SetupChart();
        LoadSampleImage();
        await FetchAndParseHtmlAsync();
    }

    // --- Drag only from the grip handle ---
    private void DragGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    // --- Opacity slider ---
    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Content is Border border)
        {
            var alpha = (byte)(e.NewValue * 255);
            var current = ((SolidColorBrush)border.Background).Color;
            border.Background = new SolidColorBrush(Color.FromArgb(alpha, current.R, current.G, current.B));
        }
    }

    // --- Close button ---
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // --- Sample line chart using OxyPlot ---
    private void SetupChart()
    {
        var model = new PlotModel
        {
            Title = "Price History",
            TitleColor = OxyColors.LightGray,
            PlotAreaBorderColor = OxyColors.Gray,
            Background = OxyColors.Transparent,
        };

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Day",
            TitleColor = OxyColors.LightGray,
            TextColor = OxyColors.LightGray,
            TicklineColor = OxyColors.Gray,
        });

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Price (chaos)",
            TitleColor = OxyColors.LightGray,
            TextColor = OxyColors.LightGray,
            TicklineColor = OxyColors.Gray,
        });

        var series = new LineSeries
        {
            Title = "Exalted Orb",
            Color = OxyColor.FromRgb(255, 200, 50),
            StrokeThickness = 2,
        };

        // Sample data points
        double[] prices = [150, 148, 155, 160, 158, 165, 170, 168, 175, 180];
        for (int i = 0; i < prices.Length; i++)
        {
            series.Points.Add(new DataPoint(i + 1, prices[i]));
        }

        model.Series.Add(series);
        model.LegendTextColor = OxyColors.LightGray;

        ChartView.Model = model;
    }

    // --- Sample image ---
    private void LoadSampleImage()
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri("https://web.poecdn.com/gen/image/WzI1LDE0LHsiZiI6IjJESXRlbXMvQ3VycmVuY3kvQ3VycmVuY3lBZGRNb2RUb1JhcmUiLCJ3IjoxLCJoIjoxLCJzY2FsZSI6MX1d/fc05f25452/CurrencyAddModToRare.png");
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            SampleImage.Source = bitmap;
        }
        catch
        {
            // Image load failure is non-critical for a demo
        }
    }

    // --- HTML fetch + parse with AngleSharp, JSON demo with System.Text.Json ---
    private async Task FetchAndParseHtmlAsync()
    {
        try
        {
            // Fetch a lightweight public page
            var html = await HttpClient.GetStringAsync("https://httpbin.org/html");

            // Parse with AngleSharp
            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(req => req.Content(html));

            var heading = document.QuerySelector("h1")?.TextContent ?? "(no heading found)";

            // Also demonstrate System.Text.Json parsing
            var jsonString = await HttpClient.GetStringAsync("https://httpbin.org/json");
            using var jsonDoc = JsonDocument.Parse(jsonString);
            var title = jsonDoc.RootElement
                .GetProperty("slideshow")
                .GetProperty("title")
                .GetString() ?? "(no title)";

            FetchedTextLabel.Text = $"HTML <h1>: {heading}\nJSON title: {title}";
        }
        catch (Exception ex)
        {
            FetchedTextLabel.Text = $"Fetch error: {ex.Message}";
        }
    }
}
