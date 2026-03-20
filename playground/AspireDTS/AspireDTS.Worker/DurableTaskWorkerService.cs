using Microsoft.DurableTask.Client;

namespace AspireDTS.Worker;

/// <summary>
/// Background service that starts a sample <see cref="HelloOrchestrator"/> orchestration on startup.
/// </summary>
internal class DurableTaskWorkerService(ILogger<DurableTaskWorkerService> logger, DurableTaskClient client)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AspireDTS Worker starting – scheduling sample orchestration");

        var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(HelloOrchestrator), "World", cancellation: stoppingToken);

        logger.LogInformation(
            "Orchestration '{OrchestratorName}' scheduled with instance ID: {InstanceId}",
            nameof(HelloOrchestrator),
            instanceId);

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}