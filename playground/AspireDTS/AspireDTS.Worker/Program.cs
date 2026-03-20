using AspireDTS.ServiceDefaults;
using AspireDTS.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

var dtsConnectionString =
    builder.Configuration.GetValue<string>("DURABLE_TASK_SCHEDULER_CONNECTION_STRING")
    ?? throw new InvalidOperationException("DURABLE_TASK_SCHEDULER_CONNECTION_STRING is not set");

builder.Services.AddDurableTaskWorker(workerBuilder =>
{
    workerBuilder.UseDurableTaskScheduler(dtsConnectionString);
    workerBuilder.AddTasks(registry =>
    {
        registry.AddOrchestrator<HelloOrchestrator>();
        registry.AddActivity<SayHelloActivity>();
    });
});

builder.Services.AddDurableTaskClient(clientBuilder =>
{
    clientBuilder.UseDurableTaskScheduler(dtsConnectionString);
});

builder.Services.AddHostedService<DurableTaskWorkerService>();

var app = builder.Build();
await app.RunAsync();
