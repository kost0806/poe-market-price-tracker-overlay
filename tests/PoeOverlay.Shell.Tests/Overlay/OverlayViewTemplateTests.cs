using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using PoeOverlay.Core.Domain;
using PoeOverlay.Core.Localization;
using PoeOverlay.Core.Presentation.ViewModels;
using PoeOverlay.Core.Presentation.ViewModels.Rows;
using PoeOverlay.Core.Pricing;
using PoeOverlay.Core.Settings;
using PoeOverlay.Overlay;
using Xunit;
using Image = System.Windows.Controls.Image;
using Size = System.Windows.Size;

namespace PoeOverlay.Shell.Tests.Overlay;

/// <summary>
/// The price row's template loads (FR-04-6 / S3 4.10).
/// </summary>
/// <remarks>
/// This is the one test that runs the overlay's real BAML rather than a piece of it. It exists
/// because the first shipped build of the icon column crashed on its first layout pass and no test
/// noticed: every part had been checked on its own — the manifest, the source, the panel — and the
/// defect was in how they were joined, which only the template can show.
/// </remarks>
public sealed class OverlayViewTemplateTests : IDisposable
{
    private readonly string _iconDirectory =
        Path.Combine(Path.GetTempPath(), "PoeOverlay.ViewIcons." + Guid.NewGuid().ToString("N"));

    public OverlayViewTemplateTests() => Directory.CreateDirectory(_iconDirectory);

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_iconDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A stale temp folder is not a test failure.
        }
    }

    [Fact]
    public void APriceRowIsMeasuredWithItsIconCell()
        => Sta(() =>
        {
            var view = Realize(BuildView());

            // The Image is the observable proof that the row template was instantiated: it is the
            // element the template's converter reference sits on, and a template that fails to load
            // throws out of the measure pass rather than producing a smaller tree.
            var image = FirstVisual<Image>(view);
            Assert.NotNull(image);
            Assert.Equal(16d, image!.Width);

            // No icon file exists here, and that is the normal answer for roughly 40% of the
            // catalogue. The cell keeps its width so the names below it stay aligned (S3 4.10.3).
            Assert.Null(image.Source);
        });

    [Fact]
    public void TheRowNameIsDrawnBesideTheIcon()
        => Sta(() =>
        {
            var view = Realize(BuildView());

            var name = FirstVisual<TextBlock>(view, t => t.Text == "Chaos Orb");
            Assert.NotNull(name);
        });

    private OverlayView BuildView()
    {
        var viewModel = new OverlayViewModel(
            new StubLocalizer(),
            new StubSettings(),
            TimeProvider.System,
            NullLogger<OverlayViewModel>.Instance)
        {
            Rows =
            [
                new PriceRowViewModel(
                    new ItemId("chaos"),
                    "Chaos Orb",
                    new PriceDisplay(PriceForm.ChaosOnly, "1.0c", DateTimeOffset.UnixEpoch, false),
                    new ChangeDisplay(ChangeDirection.Up, "▲", "▲2.0%"),
                    "3m ago",
                    false,
                    false,
                    RowKind.Normal),
            ],
        };

        return new OverlayView(viewModel, new ItemIconSource(_iconDirectory, NullLogger<ItemIconSource>.Instance));
    }

    /// <summary>
    /// Lays the view out until its rows exist.
    /// </summary>
    /// <remarks>
    /// One Measure is not enough. An <c>ItemsControl</c> builds its containers through the
    /// generator, which posts to the dispatcher, and off a window there is nothing pumping it —
    /// the first pass produced a panel with no children at all, which would have made this test
    /// pass over an empty tree whatever the template did.
    /// </remarks>
    private static OverlayView Realize(OverlayView view)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            view.Measure(new Size(420, 600));
            view.Arrange(new Rect(0, 0, 420, 600));
            view.UpdateLayout();
            DrainDispatcher();
        }

        return view;
    }

    private static void DrainDispatcher()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static T? FirstVisual<T>(DependencyObject root, Func<T, bool>? where = null)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T hit && (where is null || where(hit)))
            {
                return hit;
            }

            if (FirstVisual(child, where) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }

    private static void Sta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }

    private sealed class StubLocalizer : ILocalizer
    {
        public IReadOnlyList<LanguageInfo> Languages => [];

        public string CurrentLanguage => "en";

        public event EventHandler? LanguageChanged
        {
            add { }
            remove { }
        }

        public string Ui(string key, params string[] args) => key;

        public string ItemName(ItemId id, string? apiName) => apiName ?? id.Value;

        public bool TryGetTemplate(string key, out string template)
        {
            template = key;
            return false;
        }

        public void SetLanguage(string tag)
        {
        }
    }

    private sealed class StubSettings : ISettingsSource
    {
        public AppSettings Current => AppSettings.Default;

        public WriteBlockReason BlockReason => WriteBlockReason.None;

        public event SettingsChangedHandler? Changed
        {
            add { }
            remove { }
        }

        public void Update(AppSettings next)
        {
        }

        public Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

        public void Acknowledge()
        {
        }
    }
}
