using SpecTrace.Pipeline;

namespace SpecTrace.Web;

public static class Program
{
    public const string OfflineFlag = "--offline";

    public const string PublicDemoFlag = "--public-demo";

    public const string RunsOption = "--runs";

    public const string ReferenceOption = "--reference";

    public static async Task<int> Main(string[] args)
    {
        var offline = args.Contains(OfflineFlag, StringComparer.Ordinal);
        var publicDemo = args.Contains(PublicDemoFlag, StringComparer.Ordinal);
        var remaining = new List<string>();
        var workspace = Workspace.Default;

        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] is OfflineFlag or PublicDemoFlag)
            {
                continue;
            }

            if (args[index] is RunsOption or ReferenceOption)
            {
                if (index + 1 >= args.Length)
                {
                    await Console.Error.WriteLineAsync($"{args[index]} needs a directory.").ConfigureAwait(false);
                    return 64;
                }

                workspace = args[index] == RunsOption
                    ? workspace with { Runs = args[++index] }
                    : workspace with { Reference = args[++index] };
                continue;
            }

            remaining.Add(args[index]);
        }

        using var network = new SocketsHttpHandler();
        using var httpClient = new HttpClient(network, disposeHandler: false) { Timeout = PipelineLaunch.CallTimeout };
        Func<string, string?> environment = Environment.GetEnvironmentVariable;

        var host = new WebAppHost(
            workspace,
            environment,
            offline,
            TimeProvider.System,
            PipelineLaunch.ModelClients(httpClient, environment),
            PromptSet.Embedded,
            WebAppHost.DefaultRunWait)
        {
            PublicDemoFlag = publicDemo,
        };

        await using var app = SpecTraceWebApp.Build(host, [.. remaining]);

        await app.RunAsync().ConfigureAwait(false);

        return 0;
    }
}
