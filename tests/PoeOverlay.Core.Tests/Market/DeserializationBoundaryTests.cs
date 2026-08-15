using PoeOverlay.Core.Domain;
using Xunit;

namespace PoeOverlay.Core.Tests.Market;

/// <summary>
/// S2 11.7 M8, M12, M12′, M12″ — the skeleton and step 2' null check.
/// </summary>
/// <remarks>
/// Without step 2', <c>{"lines":null}</c> deserialises successfully and step 4's <c>.Length</c>
/// throws <see cref="NullReferenceException"/>. That is not a <c>JsonException</c>, so no catch in
/// Market sees it, it climbs to Polling's last line of defence, and on a user-initiated path it
/// lands on the UI thread.
/// </remarks>
public sealed class DeserializationBoundaryTests
{
    [Fact]
    public async Task M8_NoCoreKeyAtAll_FailsWithDeserialization()
    {
        var body = """{"lines":[{"id":"item-0","primaryValue":1}]}""";

        Assert.Equal(FailureKind.Deserialization, CategoryFetchTests.Why(await CategoryFetchTests.FetchAsync(body).ConfigureAwait(false)).Kind);
    }

    [Fact]
    public async Task M12_CoreIsNull_FailsWithDeserialization()
    {
        // required would not have stopped this: {"core":null} deserialises happily.
        var body = """{"core":null,"lines":[{"id":"item-0","primaryValue":1}]}""";

        var why = CategoryFetchTests.Why(await CategoryFetchTests.FetchAsync(body).ConfigureAwait(false));

        Assert.Equal(FailureKind.Deserialization, why.Kind);
        Assert.Equal("skeleton", why.Detail);
    }

    [Fact]
    public async Task M12Prime_LinesIsNull_FailsWithDeserialization()
    {
        var body = """{"core":{"primary":"chaos","items":[]},"lines":null}""";

        var why = CategoryFetchTests.Why(await CategoryFetchTests.FetchAsync(body).ConfigureAwait(false));

        Assert.Equal(FailureKind.Deserialization, why.Kind);
        Assert.Equal("skeleton", why.Detail);
    }

    [Fact]
    public async Task M12DoublePrime_TheWholeBodyIsANullLiteral_FailsWithDeserialization()
    {
        var why = CategoryFetchTests.Why(await CategoryFetchTests.FetchAsync("null").ConfigureAwait(false));

        Assert.Equal(FailureKind.Deserialization, why.Kind);
        Assert.Equal("skeleton", why.Detail);
    }

    [Fact]
    public async Task CoreItemsIsNull_FailsWithDeserialization()
    {
        var body = """{"core":{"primary":"chaos","items":null},"lines":[{"id":"a","primaryValue":1}]}""";

        Assert.Equal(FailureKind.Deserialization, CategoryFetchTests.Why(await CategoryFetchTests.FetchAsync(body).ConfigureAwait(false)).Kind);
    }

    [Fact]
    public async Task PrimaryIsMissing_FailsWithDeserializationRatherThanMismatch()
    {
        var body = """{"core":{"items":[]},"lines":[{"id":"a","primaryValue":1}]}""";

        Assert.Equal(FailureKind.Deserialization, CategoryFetchTests.Why(await CategoryFetchTests.FetchAsync(body).ConfigureAwait(false)).Kind);
    }

    [Fact]
    public async Task MalformedJson_FailsWithDeserializationAndCarriesTheExceptionType()
    {
        var why = CategoryFetchTests.Why(await CategoryFetchTests.FetchAsync("{ not json ").ConfigureAwait(false));

        Assert.Equal(FailureKind.Deserialization, why.Kind);
        Assert.Equal("JsonException", why.ExceptionType);
    }
}
