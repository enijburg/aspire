using Aspire.Hosting.DevJwt;

var builder = DistributedApplication.CreateBuilder(args);

var devJwt = builder.AddSharedDevJwtAuthority();

var apiOne = builder.AddJwtProject<Projects.JwtAuth_ApiOne>("api-one", devJwt);
var apiTwo = builder.AddJwtProject<Projects.JwtAuth_ApiTwo>("api-two", devJwt);

builder.AddProject<Projects.JwtAuth_Tests>("tests")
    .WithCurrentDevJwtToken(devJwt)
    .WithNewDevJwtToken(devJwt, name: "api-one-user", subject: "api-one-user", roles: ["api-one"])
    .WithNewDevJwtToken(devJwt, name: "both-user", subject: "both-user", roles: ["api-one", "api-two"])
    .WithNewDevJwtToken(devJwt, name: "api-two-user", subject: "api-two-user", roles: ["api-two"])
    .WithNewDevJwtToken(devJwt, name: "noscopes", subject: "test-bare")
    .WithReference(apiOne)
    .WithReference(apiTwo)
    .WaitFor(apiOne)
    .WaitFor(apiTwo)
    .WithArgs("--settings", "test.runsettings")
    .WithExplicitStart();


await builder.Build().RunAsync();

