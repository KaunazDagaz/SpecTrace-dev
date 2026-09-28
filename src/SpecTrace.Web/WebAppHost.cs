using SpecTrace.Pipeline;

namespace SpecTrace.Web;

public sealed record WebAppHost(
    Workspace Workspace,
    Func<string, string?> Environment,
    bool OfflineFlag,
    TimeProvider Time)
{
    public bool Offline => PipelineLaunch.IsOffline(Environment, OfflineFlag);

    public bool HasApiKey => PipelineLaunch.HasApiKey(Environment);
}
