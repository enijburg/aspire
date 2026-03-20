var builder = DistributedApplication.CreateBuilder(args);

// Azure Durable Task Scheduler emulator for local development
var dts = builder.AddContainer("dts-emulator", "mcr.microsoft.com/dts/dts-emulator", "latest")
    .WithEndpoint(name: "grpc", targetPort: 8080)
    .WithHttpEndpoint(name: "http", targetPort: 8081)
    .WithHttpEndpoint(name: "dashboard", targetPort: 8082)
    .ExcludeFromManifest();

// SQL Server with a persistent lifetime so data survives AppHost restarts
var sql = builder.AddSqlServer("sql")
    .WithLifetime(ContainerLifetime.Persistent);

var db = sql.AddDatabase("aspiradts");

var grpcEndpoint = dts.GetEndpoint("grpc");
var dtsConnectionString = ReferenceExpression.Create($"Endpoint=http://{grpcEndpoint.Property(EndpointProperty.Host)}:{grpcEndpoint.Property(EndpointProperty.Port)};TaskHub=default;Authentication=None");

// Worker service – receives the DTS connection string and SQL database reference
builder.AddProject<Projects.AspireDTS_Worker>("worker")
    .WithReference(db)
    .WaitFor(sql)
    .WithEnvironment("DURABLE_TASK_SCHEDULER_CONNECTION_STRING", dtsConnectionString)
    .WaitFor(dts);

// Client service – schedules orchestrations against the DTS
builder.AddProject<Projects.AspireDTS_Client>("client")
    .WithEnvironment("DURABLE_TASK_SCHEDULER_CONNECTION_STRING", dtsConnectionString)
    .WaitFor(dts)
    .WithExplicitStart();

await builder.Build().RunAsync();
