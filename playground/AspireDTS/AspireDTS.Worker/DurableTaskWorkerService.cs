using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Background service that starts a sample <see cref="HelloOrchestrator"/> orchestration on startup.
/// </summary>
internal class DurableTaskWorkerService : BackgroundService
{
    private readonly ILogger<DurableTaskWorkerService> _logger;
    private readonly DurableTaskClient _client;

    public DurableTaskWorkerService(ILogger<DurableTaskWorkerService> logger, DurableTaskClient client)
    {
        _logger = logger;
        _client = client;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AspireDTS Worker starting – scheduling sample orchestration");

        var instanceId = await _client.ScheduleNewOrchestrationInstanceAsync(
            nameof(HelloOrchestrator), "World", cancellation: stoppingToken);

        _logger.LogInformation(
            "Orchestration '{OrchestratorName}' scheduled with instance ID: {InstanceId}",
            nameof(HelloOrchestrator),
            instanceId);

        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
