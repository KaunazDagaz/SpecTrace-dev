using SpecTrace.Pipeline;

namespace SpecTrace.Web;

public sealed class RunLifetime : IHostedService
{
    private readonly RunCoordinator _coordinator;

    public RunLifetime(RunCoordinator coordinator) => _coordinator = coordinator;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _coordinator.RecoverInterrupted();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.WhenAny(_coordinator.Current, Task.Delay(Timeout.Infinite, cancellationToken));
}
