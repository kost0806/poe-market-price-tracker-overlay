using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PoeOverlay.Core.Domain;
using PoeOverlay.Overlay;
using Xunit;

namespace PoeOverlay.Shell.Tests.Overlay;

/// <summary>
/// <see cref="ItemIconSource"/> answers with a picture, with nothing, or with nothing again — and
/// never with an exception (S3 4.10.2 / S4 16.11).
/// </summary>
/// <remarks>
/// The failure paths matter more than the happy one here. An icon is decoration: if the manifest is
/// gone, or names a file that is not there, or is not JSON at all, the overlay must still draw its
/// prices. Each of those is asserted as an observable answer rather than as a log line.
/// </remarks>
public sealed class ItemIconSourceTests : IDisposable
{
    private readonly string _directory;

    /// <summary>Gives each test its own icon folder.</summary>
    public ItemIconSourceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PoeOverlay.Icons." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A bitmap that failed to close would show up as a leaked handle here. CacheOption.OnLoad
            // is what stops that, and this catch keeps a stale temp folder from failing the run.
        }
    }

    [Fact]
    public void AMappedSlugWithAFileOnDisk_ResolvesToAFrozenImage()
    {
        WriteManifest("""{ "chaos": "chaos.png" }""");
        WritePng("chaos.png");

        var image = Create().Resolve(new ItemId("chaos"));

        Assert.NotNull(image);

        // Frozen, so the same instance can be handed to any number of rows and outlive the pass.
        Assert.True(image!.IsFrozen);
    }

    [Fact]
    public void ASlugTheManifestDoesNotMention_ResolvesToNothing()
    {
        WriteManifest("""{ "chaos": "chaos.png" }""");
        WritePng("chaos.png");

        Assert.Null(Create().Resolve(new ItemId("divine")));
    }

    [Fact]
    public void AMappedSlugWhoseFileArrivesLate_StaysUnresolved()
    {
        // The point of the test is the caching, and the only honest way to observe a cache is to
        // change the thing behind it and watch the answer not change. Counting calls would assert
        // on a mock; asserting "still null after the file exists" cannot pass without a real cache.
        WriteManifest("""{ "chaos": "chaos.png" }""");
        var source = Create();

        Assert.Null(source.Resolve(new ItemId("chaos")));

        WritePng("chaos.png");

        Assert.Null(source.Resolve(new ItemId("chaos")));
    }

    [Fact]
    public void NoManifest_LeavesEveryRowWithoutAnIconAndThrowsNothing()
    {
        var source = Create();

        Assert.Null(source.Resolve(new ItemId("chaos")));
        Assert.Equal(0, source.MappedCount);
    }

    [Fact]
    public void BeforeTheFirstLookup_TheManifestHasNotBeenRead()
    {
        WriteManifest("""{ "chaos": "chaos.png" }""");

        var source = Create();

        // -1 rather than 0: "not read yet" and "read, and empty" are different states, and the
        // deferred load (S3 4.10.2) is only observable if they stay different.
        Assert.Equal(-1, source.MappedCount);

        _ = source.Resolve(new ItemId("chaos"));

        Assert.Equal(1, source.MappedCount);
    }

    [Fact]
    public void AManifestThatIsNotJson_LeavesEveryRowWithoutAnIconAndThrowsNothing()
    {
        WriteManifest("{ this is not json");

        var source = Create();

        Assert.Null(source.Resolve(new ItemId("chaos")));
        Assert.Equal(0, source.MappedCount);
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("nested/chaos.png")]
    [InlineData("nested\\chaos.png")]
    [InlineData("")]
    public void AFileNameThatIsNotAPlainName_IsDropped(string fileName)
    {
        // The name is escaped into the JSON rather than pasted into it. Pasted, `nested\chaos.png`
        // becomes `\c` — not a valid JSON escape — so the manifest failed to parse and the case
        // proved nothing about backslashes; it re-ran the unparseable-manifest test above, which is
        // why `divine` came back null too. The rejection under test happens after parsing.
        WriteManifest($$"""{ "chaos": {{JsonSerializer.Serialize(fileName)}}, "divine": "divine.png" }""");
        WritePng("divine.png");

        var source = Create();

        Assert.Null(source.Resolve(new ItemId("chaos")));
        Assert.NotNull(source.Resolve(new ItemId("divine")));
        Assert.Equal(1, source.MappedCount);
    }

    private ItemIconSource Create()
        => new(_directory, NullLogger<ItemIconSource>.Instance);

    private void WriteManifest(string json)
        => File.WriteAllText(Path.Combine(_directory, ItemIconSource.ManifestFileName), json);

    /// <summary>Writes a 1×1 opaque white PNG — the smallest thing WPF will decode.</summary>
    /// <remarks>
    /// Written out byte by byte rather than assembled at run time so that a decoder failure is a
    /// failure of the code under test. The chunk CRCs are real; a hand-tallied set was wrong on the
    /// first try and would have made every test here fail for the wrong reason.
    /// </remarks>
    private void WritePng(string fileName)
    {
        byte[] png =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0B, 0x49, 0x44, 0x41,
            0x54, 0x78, 0xDA, 0x63, 0xF8, 0x0F, 0x04, 0x00,
            0x09, 0xFB, 0x03, 0xFD, 0x68, 0xFA, 0x1C, 0xCC,
            0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44,
            0xAE, 0x42, 0x60, 0x82,
        ];

        File.WriteAllBytes(Path.Combine(_directory, fileName), png);
    }
}
