using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// Parse the gRPC endpoint from the DTS connection string injected by AppHost
// Connection string format: Endpoint=http://host:port;Authentication=None
var dtsConnectionString = builder.Configuration["DURABLE_TASK_SCHEDULER_CONNECTION_STRING"]
    ?? "Endpoint=http://localhost:8080;Authentication=None";
var taskHubName = builder.Configuration["TASKHUB_NAME"] ?? "default";
var dtsEndpoint = ParseDtsEndpoint(dtsConnectionString);

builder.Services.AddDurableTaskWorker(workerBuilder =>
{
    workerBuilder.UseGrpc(dtsEndpoint);
    workerBuilder.AddTasks(registry =>
    {
        registry.AddOrchestrator<HelloOrchestrator>();
        registry.AddActivity<SayHelloActivity>();
    });
});

builder.Services.AddDurableTaskClient(clientBuilder =>
{
    clientBuilder.UseGrpc(dtsEndpoint);
});

builder.Services.AddHostedService<DurableTaskWorkerService>();

var app = builder.Build();
await app.RunAsync();

static string ParseDtsEndpoint(string connectionString)
{
    foreach (var segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
    {
        var pair = segment.Split('=', 2);
        if (pair.Length == 2 && pair[0].Trim().Equals("Endpoint", StringComparison.OrdinalIgnoreCase))
        {
            return pair[1].Trim();
        }
    }

    return "http://localhost:8080";
}
