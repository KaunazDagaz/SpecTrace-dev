using SpecTrace.Pipeline;

namespace SpecTrace.Cli;

public sealed record CliHost(
    HttpMessageHandler Network,
    Func<string, string?> Environment,
    PromptSet Prompts,
    TextWriter Out,
    TextWriter Error);
