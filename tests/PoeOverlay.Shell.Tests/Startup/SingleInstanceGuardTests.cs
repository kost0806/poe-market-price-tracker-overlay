using PoeOverlay.Startup;
using Xunit;

namespace PoeOverlay.Shell.Tests.Startup;

/// <summary>The single-instance mutex (HLD 3.5 step 2, teardown step d).</summary>
public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void FirstAcquirerWins()
    {
        var name = UniqueName();
        using var first = new SingleInstanceGuard(name);

        Assert.True(first.TryAcquire());
        Assert.True(first.IsHeld);
    }

    [Fact]
    public void SecondAcquirerFailsWhileTheFirstHolds()
    {
        var name = UniqueName();
        using var first = new SingleInstanceGuard(name);
        Assert.True(first.TryAcquire());

        // A different thread, because a mutex is reentrant for its owning thread — testing this on
        // one thread would pass for the wrong reason.
        Assert.False(TryAcquireOnAnotherThread(name));
    }

    [Fact]
    public void ReleaseLetsARelaunchIn()
    {
        // Teardown releases the mutex before StopAsync, which can take the full five seconds. A
        // relaunch in that window must be able to take it rather than fall through to the signal
        // channel, where nothing is left alive to answer.
        var name = UniqueName();
        using var first = new SingleInstanceGuard(name);
        Assert.True(first.TryAcquire());

        first.Release();

        Assert.False(first.IsHeld);
        Assert.True(TryAcquireOnAnotherThread(name));
    }

    [Fact]
    public void ReleaseIsIdempotent()
    {
        var name = UniqueName();
        using var guard = new SingleInstanceGuard(name);
        Assert.True(guard.TryAcquire());

        guard.Release();
        guard.Release();
    }

    private static string UniqueName() => $"PoeOverlay.Tests.{Guid.NewGuid():N}";

    private static bool TryAcquireOnAnotherThread(string name)
    {
        var acquired = false;
        var thread = new Thread(() =>
        {
            using var guard = new SingleInstanceGuard(name);
            acquired = guard.TryAcquire();
        });

        thread.Start();
        thread.Join();
        return acquired;
    }
}
