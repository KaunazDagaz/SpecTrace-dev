using System.Reflection;

namespace SpecTrace.Pipeline;

public static class PipelineBuild
{
    public const string UnknownGitSha = "unknown";

    private static readonly string[] InformationalVersion =
        (typeof(PipelineBuild).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0")
        .Split('+', 2);

    public static string Version { get; } = InformationalVersion[0];

    public static string GitSha { get; } =
        InformationalVersion is [_, var sha] && !string.IsNullOrWhiteSpace(sha) ? sha : UnknownGitSha;
}
