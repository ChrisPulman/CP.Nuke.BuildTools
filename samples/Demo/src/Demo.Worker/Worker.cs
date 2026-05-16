using Demo.Shared;

namespace Demo.Worker;

public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker loaded {DemoName}", DemoCatalog.Current.DisplayName);
        return Task.CompletedTask;
    }
}
