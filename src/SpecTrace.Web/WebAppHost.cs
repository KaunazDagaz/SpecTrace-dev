using SpecTrace.Pipeline;

namespace SpecTrace.Web;

public sealed record WebAppHost(
    Workspace Workspace,
    Func<string, string?> Environment,
    bool OfflineFlag,
    TimeProvider Time,
    ModelClientFactory ModelClients,
    PromptSet Prompts,
    TimeSpan RunWait)
{
    public const string PublicDemoVariable = "SPECTRACE_PUBLIC_DEMO";

    public static readonly TimeSpan DefaultRunWait = TimeSpan.FromSeconds(30);

    public bool PublicDemoFlag { get; init; }

    public bool Offline => PipelineLaunch.IsOffline(Environment, OfflineFlag);

    public bool PublicDemo => PipelineLaunch.IsSwitchedOn(Environment, PublicDemoVariable, PublicDemoFlag);

    public bool HasApiKey => PipelineLaunch.HasApiKey(Environment);
}
