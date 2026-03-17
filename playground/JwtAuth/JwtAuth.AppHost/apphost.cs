using Aspire.Hosting.DevJwt;

var builder = DistributedApplication.CreateBuilder(args);

var devJwt = builder.AddSharedDevJwtAuthority();

var apiOne = builder.AddJwtProject<Projects.JwtAuth_ApiOne>("api-one", devJwt);
var apiTwo = builder.AddJwtProject<Projects.JwtAuth_ApiTwo>("api-two", devJwt);

builder.AddProject<Projects.JwtAuth_Tests>("tests")
    .WithCurrentDevJwtToken(devJwt)
    .WithNewDevJwtToken(devJwt, name: "test-user", subject: "test-user", roles: ["admin", "reader"])
    .WithNewDevJwtToken(devJwt, name: "readonly", subject: "test-reader", roles: ["reader"])
    .WithNewDevJwtToken(devJwt, name: "noscopes", subject: "test-bare")
    .WithReference(apiOne)
    .WithReference(apiTwo)
    .WaitFor(apiOne)
    .WaitFor(apiTwo)
    .WithArgs("--settings", "test.runsettings")
    .WithExplicitStart();


await builder.Build().RunAsync();

