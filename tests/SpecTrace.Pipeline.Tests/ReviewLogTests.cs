using System.Text;
using SpecTrace.Core;

namespace SpecTrace.Pipeline.Tests;

public sealed class ReviewLogTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 28, 9, 15, 2, TimeSpan.Zero);

    private static readonly LoggedDecision[] OneOfEachKind =
    [
        new ReviewRecord("rfc6902-3ff2234db6aa", "TC-07e6ff-01", CaseDecision.Accept, null, "Ann Reviewer", At),
        new ReviewRecord(
            "rfc6902-3ff2234db6aa",
            "TC-07e6ff-02",
            CaseDecision.Edit,
            new CaseEdit("Two \"op\" members are rejected", CaseType.Negative, "A document.\nAnother line.", string.Empty, "Rejected: ünïcode, \\ and \t survive."),
            "Ann Reviewer",
            At),
        new ReviewRecord("rfc6902-3ff2234db6aa", "TC-07e6ff-03", CaseDecision.Reject, null, "Бо", At),
        new QueueResolution("rfc6902-3ff2234db6aa", "DQ-rfc6902-5a829e", "REQ-rfc6902-5a829e", QueueDecision.NotTestable, "Ann Reviewer", At),
        new QueueResolution("rfc6902-3ff2234db6aa", "DQ-rfc6902-1dd2c2", null, QueueDecision.Defer, "Ann Reviewer", At),
    ];

    [Fact]
    public async Task EveryKindOfDecisionIsWrittenAsOneLineAndReadBackUnchanged()
    {
        using var scratch = new ScratchDirectory();
        var path = Path.Combine(scratch.Path, "reviews", "run.jsonl");

        foreach (var decision in OneOfEachKind)
        {
            await ReviewLog.AppendAsync(path, decision, CancellationToken.None);
        }

        var lines = (await File.ReadAllTextAsync(path)).Split('\n');
        var read = await ReviewLog.ReadAsync(path, CancellationToken.None);

        Assert.Equal(OneOfEachKind.Length + 1, lines.Length);
        Assert.Equal(string.Empty, lines[^1]);
        Assert.Equal(OneOfEachKind, read);
        Assert.StartsWith(
            "{\"run_id\":\"rfc6902-3ff2234db6aa\",\"target\":\"test_case\",\"test_case_id\":\"TC-07e6ff-01\",\"decision\":\"accept\","
            + "\"author\":\"Ann Reviewer\",\"author_self_declared\":true,\"at\":\"2026-09-28T09:15:02.0000000+00:00\"}",
            lines[0],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingLogReadsAsNoDecisions()
    {
        using var scratch = new ScratchDirectory();

        Assert.Empty(await ReviewLog.ReadAsync(Path.Combine(scratch.Path, "none.jsonl"), CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentAppendsNeverInterleaveOrTruncateALine()
    {
        using var scratch = new ScratchDirectory();
        var path = Path.Combine(scratch.Path, "run.jsonl");
        const int Appends = 200;

        await Task.WhenAll(Enumerable.Range(0, Appends).Select(index => Task.Run(() => ReviewLog.AppendAsync(
            path,
            new ReviewRecord("run-000000000000", $"TC-aaaaaa-{index:D3}", CaseDecision.Reject, null, $"Reviewer {index}", At),
            CancellationToken.None))));

        var read = await ReviewLog.ReadAsync(path, CancellationToken.None);

        Assert.Equal(Appends, read.Count);
        Assert.Equal(
            Enumerable.Range(0, Appends).Select(index => $"TC-aaaaaa-{index:D3}").Order(StringComparer.Ordinal),
            read.Select(decision => decision.TargetId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AnAppendAfterAnIncompleteLastLineIsRefusedAndTheFileIsLeftAsItWas()
    {
        using var scratch = new ScratchDirectory();
        var path = Path.Combine(scratch.Path, "run.jsonl");
        var complete = ReviewLog.Serialize(OneOfEachKind[0]);
        byte[] partial = [.. complete, .. complete.AsSpan(0, 20)];

        await File.WriteAllBytesAsync(path, partial);

        var refusal = await Assert.ThrowsAsync<InvalidReviewLogException>(() =>
            ReviewLog.AppendAsync(path, OneOfEachKind[2], CancellationToken.None));

        Assert.Contains("incomplete", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(partial, await File.ReadAllBytesAsync(path));

        var unreadable = await Assert.ThrowsAsync<InvalidReviewLogException>(() => ReviewLog.ReadAsync(path, CancellationToken.None));

        Assert.Contains("line 2", unreadable.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json", "line 2")]
    [InlineData("[]", "not a JSON object")]
    [InlineData("{\"run_id\":\"r\",\"target\":\"test_case\",\"test_case_id\":\"TC\",\"decision\":\"accept\",\"author\":\"A\",\"author_self_declared\":false,\"at\":\"2026-09-28T09:00:00+00:00\"}", "author_self_declared")]
    [InlineData("{\"run_id\":\"r\",\"target\":\"test_case\",\"test_case_id\":\"TC\",\"decision\":\"approve\",\"author\":\"A\",\"author_self_declared\":true,\"at\":\"2026-09-28T09:00:00+00:00\"}", "approve")]
    [InlineData("{\"run_id\":\"r\",\"target\":\"test_case\",\"test_case_id\":\"TC\",\"decision\":\"accept\",\"author\":\"A\",\"author_self_declared\":true,\"at\":\"2026-09-28T09:00:00+00:00\",\"extra\":1}", "extra")]
    [InlineData("{\"run_id\":\"r\",\"target\":\"test_case\",\"test_case_id\":\"TC\",\"decision\":\"edit\",\"author\":\"A\",\"author_self_declared\":true,\"at\":\"2026-09-28T09:00:00+00:00\"}", "edited_case")]
    [InlineData("{\"run_id\":\"r\",\"target\":\"requirement\",\"author\":\"A\",\"author_self_declared\":true,\"at\":\"2026-09-28T09:00:00+00:00\"}", "not a review target")]
    [InlineData("", "line 2")]
    public async Task AMalformedLineMakesTheWholeLogUnreadableAndNamesTheLine(string line, string expected)
    {
        using var scratch = new ScratchDirectory();
        var path = Path.Combine(scratch.Path, "run.jsonl");

        await File.WriteAllBytesAsync(path, [.. ReviewLog.Serialize(OneOfEachKind[0]), .. Encoding.UTF8.GetBytes(line + "\n")]);

        var exception = await Assert.ThrowsAsync<InvalidReviewLogException>(() => ReviewLog.ReadAsync(path, CancellationToken.None));

        Assert.Contains("line 2", exception.Message, StringComparison.Ordinal);
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALogThatIsNotUtf8IsUnreadable()
    {
        using var scratch = new ScratchDirectory();
        var path = Path.Combine(scratch.Path, "run.jsonl");

        await File.WriteAllBytesAsync(path, [0xFF, 0xFE, (byte)'\n']);

        await Assert.ThrowsAsync<InvalidReviewLogException>(() => ReviewLog.ReadAsync(path, CancellationToken.None));
    }
}
