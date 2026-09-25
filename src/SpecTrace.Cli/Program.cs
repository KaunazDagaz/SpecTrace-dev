using SpecTrace.Pipeline;

namespace SpecTrace.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        using var network = new SocketsHttpHandler();

        var host = new CliHost(
            network,
            Environment.GetEnvironmentVariable,
            PromptSet.Embedded,
            Console.Out,
            Console.Error);

        return await SpecTraceCli.RunAsync(args, host, cancellation.Token).ConfigureAwait(false);
    }
}
