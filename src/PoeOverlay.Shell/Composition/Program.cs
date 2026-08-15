namespace PoeOverlay.Composition;

/// <summary>
/// Entry point (S4 2.1, 12.1).
/// </summary>
/// <remarks>
/// Stage 1 scaffolding only. The host builder, the single-instance guard, the overlay window and
/// the boot diagnostics reconciliation of S4 12.1 / 12.2 arrive with the Shell stage; this type
/// exists so the project has an entry point and compiles.
/// </remarks>
internal static class Program
{
    [STAThread]
    internal static void Main()
    {
    }
}
