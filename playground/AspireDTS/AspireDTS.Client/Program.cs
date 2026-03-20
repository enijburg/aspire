using AspireDTS.ServiceDefaults;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

var dtsConnectionString =
    builder.Configuration.GetValue<string>("DURABLE_TASK_SCHEDULER_CONNECTION_STRING")
    ?? throw new InvalidOperationException("DURABLE_TASK_SCHEDULER_CONNECTION_STRING is not set");

builder.Services.AddDurableTaskClient(clientBuilder =>
{
    clientBuilder.UseDurableTaskScheduler(dtsConnectionString);
});

var app = builder.Build();

var logger = app.Services.GetRequiredService<ILogger<Program>>();
var client = app.Services.GetRequiredService<DurableTaskClient>();

logger.LogInformation("AspireDTS Client starting – scheduling sample orchestration");

var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
    "HelloOrchestrator", "World");

logger.LogInformation(
    "Orchestration '{OrchestratorName}' scheduled with instance ID: {InstanceId}",
    "HelloOrchestrator",
    instanceId);

var result = await client.WaitForInstanceCompletionAsync(
    instanceId, getInputsAndOutputs: true);

logger.LogInformation(
    "Orchestration completed with status: {Status}, output: {Output}",
    result.RuntimeStatus,
    result.ReadOutputAs<string>());
