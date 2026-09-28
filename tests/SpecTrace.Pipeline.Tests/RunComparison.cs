using System.Text;
using System.Text.RegularExpressions;

namespace SpecTrace.Pipeline.Tests;

internal static class RunComparison
{
    public static readonly string[] ManifestFieldsThatVaryBetweenRuns = ["started_at", "git_sha"];

    private const string Masked = "<varies between runs>";

    public static void AssertSameArtifacts(string referenceDirectory, string runDirectory)
    {
        var referenceFiles = FileNamesIn(referenceDirectory);

        Assert.Contains(RunArtifacts.ManifestFile, referenceFiles);
        Assert.Contains(RunArtifacts.MatrixHtmlFile, referenceFiles);
        Assert.Equal(referenceFiles, FileNamesIn(runDirectory));

        foreach (var file in referenceFiles)
        {
            var expected = File.ReadAllBytes(Path.Combine(referenceDirectory, file));
            var actual = File.ReadAllBytes(Path.Combine(runDirectory, file));

            if (file == RunArtifacts.ManifestFile)
            {
                Assert.Equal(MaskVaryingFields(expected), MaskVaryingFields(actual));
                continue;
            }

            if (!expected.AsSpan().SequenceEqual(actual))
            {
                Assert.Equal(Encoding.UTF8.GetString(expected), Encoding.UTF8.GetString(actual));
                Assert.Fail($"{file} differs from {referenceDirectory}/{file} in bytes that decode to the same text.");
            }
        }
    }

    public static Dictionary<string, string> VaryingValues(byte[] manifest)
    {
        var text = Encoding.UTF8.GetString(manifest);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var field in ManifestFieldsThatVaryBetweenRuns)
        {
            var matches = FieldPattern(field).Matches(text);

            Assert.True(matches.Count == 1, $"'{field}' occurs {matches.Count} times in manifest.json; expected exactly once.");
            values[field] = matches[0].Groups["value"].Value;
        }

        return values;
    }

    public static Dictionary<string, string> Snapshot(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    path => Path.GetRelativePath(directory, path),
                    path => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))),
                    StringComparer.Ordinal)
            : [];

    private static List<string> FileNamesIn(string directory) =>
        Directory.EnumerateFileSystemEntries(directory)
            .Select(entry => Path.GetFileName(entry))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string MaskVaryingFields(byte[] manifest)
    {
        var text = Encoding.UTF8.GetString(manifest);

        foreach (var field in ManifestFieldsThatVaryBetweenRuns)
        {
            text = FieldPattern(field).Replace(text, $"\"{field}\": \"{Masked}\"");
        }

        return text;
    }

    private static Regex FieldPattern(string field) =>
        new($"\"{Regex.Escape(field)}\": \"(?<value>[^\"]*)\"", RegexOptions.CultureInvariant);
}
