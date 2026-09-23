using SpecTrace.Llm;

namespace SpecTrace.Pipeline.Tests;

public sealed class OfflineCompositionTests
{
    [Fact]
    public async Task OfflineWithNoKeyACacheMissThrowsAndNothingReachesTheNetwork()
    {
        using var cache = new ScratchDirectory();
        var network = new NoNetworkHandler();
        using var httpClient = new HttpClient(network);

        var client = LlmClientFactory.Create(httpClient, cache.Path, offline: true, apiKey: null);
        var request = new RequirementExtractor(client, LlmClientFactory.DefaultModel)
            .RequestFor(Corpus.DocumentId, Corpus.Raw);

        await Assert.ThrowsAsync<OfflineCacheMissException>(
            () => client.CompleteAsync(request, CancellationToken.None));

        Assert.Equal(0, network.Attempts);
    }

    [Fact]
    public void OnlineWithNoKeyRefusesToStartRatherThanFailingOnTheFirstCall()
    {
        using var cache = new ScratchDirectory();
        using var httpClient = new HttpClient(new NoNetworkHandler());

        var exception = Assert.Throws<InvalidOperationException>(
            () => LlmClientFactory.Create(httpClient, cache.Path, offline: false, apiKey: null));

        Assert.Contains(GeminiLlmClient.ApiKeyVariable, exception.Message, StringComparison.Ordinal);
        Assert.Contains(CachingLlmClient.OfflineVariable, exception.Message, StringComparison.Ordinal);
    }
}
