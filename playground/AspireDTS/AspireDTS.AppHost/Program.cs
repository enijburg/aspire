var builder = DistributedApplication.CreateBuilder(args);

// Azure Durable Task Scheduler emulator for local development
var dts = builder.AddContainer("dts-emulator", "mcr.microsoft.com/dts/dts-emulator", "latest")
    .WithEndpoint(name: "grpc", targetPort: 8080)
    .WithHttpEndpoint(name: "dashboard", targetPort: 8082)
    .ExcludeFromManifest();

var dtsGrpcEndpoint = dts.GetEndpoint("grpc");

// SQL Server with a persistent lifetime so data survives AppHost restarts
var sql = builder.AddSqlServer("sql")
    .WithLifetime(ContainerLifetime.Persistent);

var db = sql.AddDatabase("aspiradts");

// Worker service – receives the DTS connection string and SQL database reference
builder.AddProject<Projects.AspireDTS_Worker>("worker")
    .WithReference(db)
    .WaitFor(sql)
    .WithEnvironment(ctx =>
    {
        ctx.EnvironmentVariables["DURABLE_TASK_SCHEDULER_CONNECTION_STRING"] =
            ReferenceExpression.Create(
                $"Endpoint=http://{dtsGrpcEndpoint.Property(EndpointProperty.Host)}:{dtsGrpcEndpoint.Property(EndpointProperty.Port)};Authentication=None");
    })
    .WithEnvironment("TASKHUB_NAME", "default")
    .WaitFor(dts);

await builder.Build().RunAsync();
