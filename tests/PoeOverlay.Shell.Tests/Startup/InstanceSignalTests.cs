using System.Windows.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using PoeOverlay.Composition;
using PoeOverlay.Interop;
using PoeOverlay.Startup;
using PoeOverlay.Tray;
using Xunit;

namespace PoeOverlay.Shell.Tests.Startup;

/// <summary>
/// Live round trips over the real message-only window (S3 3.2 D-SH18).
/// </summary>
/// <remarks>
/// These are not unit tests in the usual sense — they stand up a genuine <c>HWND_MESSAGE</c> window
/// on a pumping STA thread and send it a real cross-thread <c>SendMessageTimeout</c>. That is the
/// point: the measurement that produced D-SH18 is about what the Win32 call returns in situations no
/// mock reproduces, so the only honest test of the sentinel rule is one that performs it.
/// <para>
/// The receiver's window class name is process-wide, so this collection must not run in parallel
/// with itself.
/// </para>
/// </remarks>
[Collection(nameof(MessageOnlyWindowCollection))]
public sealed class InstanceSignalTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1500);

    [Fact]
    public void TrySend_WithNoReceiver_ReportsWindowNotFound()
    {
        var result = InstanceSignal.TrySend("PoeOverlay.Tests.NoSuchWindowClass", Timeout, maxAttempts: 1);
        Assert.Equal(InstanceSignalSendResult.WindowNotFound, result);
    }

    [Fact]
    public void TrySend_ToALivePumpingReceiver_IsAcknowledgedAndRunsTheHandler()
    {
        using var receiver = ReceiverThread.Start();

        var result = InstanceSignal.TrySend(ShellConstants.SignalWindowClassName, Timeout, maxAttempts: 3);

        Assert.Equal(InstanceSignalSendResult.Acknowledged, result);
        Assert.True(receiver.WaitForHandler(TimeSpan.FromSeconds(2)), "The handler did not run.");
    }

    [Fact]
    public void TrySend_AfterStopReceiving_IsTreatedAsNoResponse()
    {
        // The teardown window: reception is off but the HWND still exists. The raw return value is
        // a success — this is precisely the case the measurement caught, where DestroyWindow
        // released a pending send with return 0x1 and GetLastError 0 while the handler never ran
        // (00-shell-measurements.md 10.1). Only the sentinel separates the two.
        using var receiver = ReceiverThread.Start();
        receiver.StopReceiving();

        var result = InstanceSignal.TrySend(ShellConstants.SignalWindowClassName, Timeout, maxAttempts: 2);

        Assert.Equal(InstanceSignalSendResult.NoResponse, result);
        Assert.False(receiver.WaitForHandler(TimeSpan.FromMilliseconds(300)), "The handler ran after StopReceiving.");
    }

    /// <summary>An STA thread running a dispatcher with a live signal receiver on it.</summary>
    private sealed class ReceiverThread : IDisposable
    {
        private readonly ManualResetEventSlim _handlerRan = new(false);
        private readonly ManualResetEventSlim _ready = new(false);
        private readonly Thread _thread;
        private Dispatcher? _dispatcher;
        private InstanceSignal? _signal;

        private ReceiverThread()
        {
            _thread = new Thread(Pump) { IsBackground = true, Name = "signal-receiver" };
            _thread.SetApartmentState(ApartmentState.STA);
        }

        internal static ReceiverThread Start()
        {
            var receiver = new ReceiverThread();
            receiver._thread.Start();
            Assert.True(receiver._ready.Wait(TimeSpan.FromSeconds(5)), "The receiver thread never came up.");
            return receiver;
        }

        internal bool WaitForHandler(TimeSpan timeout) => _handlerRan.Wait(timeout);

        internal void StopReceiving() => _dispatcher!.Invoke(() => _signal!.StopReceiving());

        public void Dispose()
        {
            _dispatcher?.Invoke(() => _signal?.Dispose());
            _dispatcher?.InvokeShutdown();
            _ = _thread.Join(TimeSpan.FromSeconds(5));
            _handlerRan.Dispose();
            _ready.Dispose();
        }

        private void Pump()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _signal = new InstanceSignal(
                new UiDispatcher(_dispatcher),
                new MessageOnlyWindowFactory(),
                () => _handlerRan.Set(),
                NullLogger<InstanceSignal>.Instance);
            _signal.StartReceiving();
            _ready.Set();
            Dispatcher.Run();
        }
    }
}

/// <summary>Serialises everything that registers the process-wide receiver window class.</summary>
[CollectionDefinition(nameof(MessageOnlyWindowCollection), DisableParallelization = true)]
public sealed class MessageOnlyWindowCollection
{
}
