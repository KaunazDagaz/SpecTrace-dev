using System.Globalization;
using SpecTrace.Core;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace SpecTrace.Pipeline;

public static class GoldFile
{
    public static AnnotatedFile Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var stream = new YamlStream();

        try
        {
            stream.Load(new StringReader(text));
        }
        catch (YamlException exception)
        {
            throw new InvalidGoldFileException(LineOf(exception.Start), exception.Message);
        }

        if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidGoldFileException(1, "the file is not one YAML mapping");
        }

        return new AnnotatedFile(
            Scalar(root, "document"),
            Scalar(root, "annotation_rules_commit"),
            Scalar(root, "annotator"),
            Scalar(root, "annotated_at"),
            [.. Items(root, "candidates").Select(Candidate)]);
    }

    public static async Task<GoldCheckResult> CheckAsync(
        string goldPath,
        string documentPath,
        string frozenRulesCommit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(goldPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);

        var text = await File.ReadAllTextAsync(goldPath, cancellationToken).ConfigureAwait(false);
        var raw = await File.ReadAllTextAsync(documentPath, cancellationToken).ConfigureAwait(false);

        return GoldCheck.Run(Parse(text), ExtractionRun.DocumentIdFor(documentPath), raw, frozenRulesCommit);
    }

    public static async Task<GoldStandard> LoadAsync(
        string goldPath,
        string documentPath,
        string frozenRulesCommit,
        CancellationToken cancellationToken)
    {
        var result = await CheckAsync(goldPath, documentPath, frozenRulesCommit, cancellationToken).ConfigureAwait(false);

        return result.Standard ?? throw new InvalidGoldFileException(result.Problems);
    }

    private static AnnotatedCandidate Candidate(YamlNode node)
    {
        var candidate = Mapping(node, "a candidate");
        var number = Scalar(candidate, "candidate");

        if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidGoldFileException(LineOf(node.Start), $"candidate '{number}' is not a number");
        }

        return new AnnotatedCandidate(
            LineOf(node.Start),
            parsed,
            Scalar(candidate, "sentence"),
            Scalar(candidate, "decision"),
            [.. Items(candidate, "obligations").Select(Obligation)]);
    }

    private static AnnotatedObligation Obligation(YamlNode node)
    {
        var obligation = Mapping(node, "an obligation");

        return new AnnotatedObligation(
            LineOf(node.Start),
            Scalar(obligation, "quote"),
            Scalar(obligation, "modality"),
            Scalar(obligation, "testability"),
            Scalar(obligation, "section"));
    }

    private static YamlMappingNode Mapping(YamlNode node, string what) =>
        node as YamlMappingNode
        ?? throw new InvalidGoldFileException(LineOf(node.Start), $"{what} is not a mapping of fields");

    private static string Scalar(YamlMappingNode mapping, string key)
    {
        if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out var value))
        {
            return string.Empty;
        }

        return value is YamlScalarNode scalar
            ? scalar.Value ?? string.Empty
            : throw new InvalidGoldFileException(LineOf(value.Start), $"'{key}' holds a list or mapping, not a single value");
    }

    private static IEnumerable<YamlNode> Items(YamlMappingNode mapping, string key)
    {
        if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out var value))
        {
            return [];
        }

        return value switch
        {
            YamlSequenceNode sequence => sequence.Children,
            YamlScalarNode { Value: null or "" } => [],
            _ => throw new InvalidGoldFileException(LineOf(value.Start), $"'{key}' is not a list"),
        };
    }

    private static int LineOf(Mark mark) => (int)mark.Line;
}

public sealed class InvalidGoldFileException : Exception
{
    public InvalidGoldFileException(int line, string problem)
        : this([new GoldProblem(line, problem)])
    {
    }

    public InvalidGoldFileException(IReadOnlyList<GoldProblem> problems)
        : base($"The gold file does not load: {string.Join("; ", problems.Select(Describe))}.")
    {
        Problems = problems;
    }

    public IReadOnlyList<GoldProblem> Problems { get; }

    public static string Describe(GoldProblem problem) =>
        problem.Line > 0
            ? string.Create(CultureInfo.InvariantCulture, $"line {problem.Line}: {problem.Message}")
            : problem.Message;
}
