using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using PoeOverlay.Core.Catalog;
using PoeOverlay.Core.Domain;
using Xunit;

namespace PoeOverlay.Core.Tests.Catalog;

/// <summary>
/// The shipped catalogue answers with rows, with nothing, or with nothing again — never with an
/// exception (FR-01-1 / S2 6.8).
/// </summary>
/// <remarks>
/// The failure paths carry the weight. The file sits beside the exe so a user can drop a newer
/// league's copy in, which means every shape below is reachable in the field: absent, not JSON,
/// naming a category this build does not have.
/// </remarks>
public sealed class ItemCatalogTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "PoeOverlay.CatalogTests." + Guid.NewGuid().ToString("N"));

    public ItemCatalogTests() => Directory.CreateDirectory(_directory);

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A stale temp folder is not a test failure.
        }
    }

    [Fact]
    public void BeforeTheFirstLookup_TheFileHasNotBeenRead()
    {
        Write("""{ "abyss-scarab": { "cat": "Scarab", "en": "Abyss Scarab" } }""");

        var catalog = Create();

        // -1 rather than 0: "not read yet" and "read, and empty" are different states, and the
        // deferred load is only observable while they stay different.
        Assert.Equal(-1, catalog.Count);

        _ = catalog.Entries;

        Assert.Equal(1, catalog.Count);
    }

    [Fact]
    public void ARowCarriesItsCategoryAndEnglishName()
    {
        Write("""{ "abyss-scarab": { "cat": "Scarab", "en": "Abyss Scarab" } }""");

        Assert.True(Create().TryGet(new ItemId("abyss-scarab"), out var entry));
        Assert.Equal(ExchangeCategory.Scarab, entry.Category);
        Assert.Equal("Abyss Scarab", entry.EnglishName);
    }

    [Fact]
    public void ASlugTheFileDoesNotMention_IsNotFound()
    {
        Write("""{ "abyss-scarab": { "cat": "Scarab", "en": "Abyss Scarab" } }""");

        Assert.False(Create().TryGet(new ItemId("divine"), out _));
    }

    [Fact]
    public void NoFile_LeavesAnEmptyCatalogueAndThrowsNothing()
    {
        var catalog = Create();

        Assert.Empty(catalog.Entries);
        Assert.Equal(0, catalog.Count);
    }

    [Fact]
    public void AFileThatIsNotJson_LeavesAnEmptyCatalogueAndThrowsNothing()
    {
        Write("{ this is not json");

        var catalog = Create();

        Assert.Empty(catalog.Entries);
        Assert.Equal(0, catalog.Count);
    }

    [Theory]
    [InlineData("""{ "x": { "cat": "NotACategory", "en": "X" }, "divine": { "cat": "Currency", "en": "Divine Orb" } }""")]
    [InlineData("""{ "x": { "cat": "Currency", "en": "" }, "divine": { "cat": "Currency", "en": "Divine Orb" } }""")]
    [InlineData("""{ "x": { "cat": "currency", "en": "X" }, "divine": { "cat": "Currency", "en": "Divine Orb" } }""")]
    public void AnUnusableRow_IsDroppedAndTheRestSurvive(string json)
    {
        // The third case is lower-cased on purpose: the file is generated, so a name differing by
        // case means the generator changed, not that the reader should be lenient.
        Write(json);

        var catalog = Create();

        Assert.True(catalog.TryGet(new ItemId("divine"), out _));
        Assert.False(catalog.TryGet(new ItemId("x"), out _));

        // After the lookups, never before: Count is -1 until the file is actually read, which is
        // the state the deferred load exists to keep visible.
        Assert.Equal(1, catalog.Count);
    }

    private ItemCatalog Create() => new(_directory, NullLogger<ItemCatalog>.Instance);

    private void Write(string json)
        => File.WriteAllText(Path.Combine(_directory, ItemCatalog.FileName), json);
}
