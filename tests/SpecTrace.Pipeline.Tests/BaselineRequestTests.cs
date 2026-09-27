using SpecTrace.Llm;

namespace SpecTrace.Pipeline.Tests;

public sealed class BaselineRequestTests
{
    private const string AgreedPrompt =
        "List every requirement in the following specification. For each one, quote the exact sentence it "
        + "comes from, then write test cases for it with a title, input and expected result.";

    [Fact]
    public void TheBaselinePromptFileHoldsTheAgreedPromptWordForWordAndNothingElse()
    {
        Assert.Equal($"{AgreedPrompt}\n", PromptFile.Baseline.Text);
        Assert.Equal("baseline.user.md", PromptFile.Baseline.Name);
    }

    [Fact]
    public void TheDocumentFollowsThePromptInOneUserMessageAfterABlankLine()
    {
        var request = BaselineRun.RequestFor(Corpus.Raw, LlmClientFactory.DefaultModel, PromptFile.Baseline);

        Assert.Equal($"{AgreedPrompt}\n\n{Corpus.Raw}", request.UserPrompt);
    }

    [Fact]
    public void TheRequestHasNoSystemPromptNoSchemaAndTemperatureZero()
    {
        var request = BaselineRun.RequestFor(Corpus.Raw, LlmClientFactory.DefaultModel, PromptFile.Baseline);

        Assert.Equal(string.Empty, request.SystemPrompt);
        Assert.Null(request.JsonSchema);
        Assert.Equal(0, request.Temperature);
        Assert.Equal(PromptFile.Baseline.Sha256, request.PromptSha256);
    }

    [Fact]
    public void TheOutputCeilingIsTheModelsOwnSoOurSettingNeverCutsTheAnswerShort()
    {
        var request = BaselineRun.RequestFor(Corpus.Raw, LlmClientFactory.DefaultModel, PromptFile.Baseline);

        Assert.Equal("gemini-3.5-flash-lite", request.Model);
        Assert.Equal(65_536, request.MaxOutputTokens);
    }

    [Fact]
    public void TheBaselineRunIdNamesTheDocumentAndTheArmAndDiffersFromThePipelineRunId()
    {
        var baseline = BaselineRun.RunIdFor(Corpus.DocumentId, Corpus.Raw, LlmClientFactory.DefaultModel, PromptFile.Baseline);
        var pipeline = PipelineRun.RunIdFor(Corpus.DocumentId, Corpus.Raw, LlmClientFactory.DefaultModel, PromptSet.Embedded);

        Assert.Matches("^rfc6902-baseline-[0-9a-f]{12}$", baseline);
        Assert.Equal("rfc6902-3ff2234db6aa", pipeline);
    }
}
