using SpecTrace.Core;
using SpecTrace.Llm;

namespace SpecTrace.Pipeline;

public sealed record ArmMetrics(
    string RunId,
    string DocumentId,
    Arm Arm,
    string Model,
    string Channel,
    string? FinishReason,
    ClaimTally Claimed,
    ClaimTally? Delivered,
    CallCost? Cost,
    ChatCapture? Capture)
{
    public bool Reproducible => Arm != Arm.Chat;

    public string FileName => $"{RunId}.metrics.json";
}

public enum Arm
{
    Chat,
    Baseline,
    Pipeline,
}

public sealed record CallCost(
    int Calls,
    int CacheHits,
    int InputTokens,
    int OutputTokens,
    int WholeDocumentInputTokens,
    int WholeDocumentOutputTokens)
{
    public double CacheHitRate => Calls == 0 ? 0 : (double)CacheHits / Calls;

    public static CallCost Of(LlmResponse wholeDocument, IReadOnlyList<LlmResponse> calls)
    {
        ArgumentNullException.ThrowIfNull(wholeDocument);
        ArgumentNullException.ThrowIfNull(calls);

        return new CallCost(
            calls.Count,
            calls.Count(call => call.FromCache),
            calls.Sum(call => call.InputTokens),
            calls.Sum(call => call.OutputTokens),
            wholeDocument.InputTokens,
            wholeDocument.OutputTokens);
    }
}

public sealed record ChatCapture(
    string Transcript,
    DateTimeOffset CapturedAt,
    string Interface,
    string Mode,
    string Input,
    string ShareLink);
