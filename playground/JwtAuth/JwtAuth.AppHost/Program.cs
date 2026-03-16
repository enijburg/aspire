var builder = DistributedApplication.CreateBuilder(args);

var apiOne = builder.AddProject<Projects.JwtAuth_ApiOne>("api-one");

var apiTwo = builder.AddProject<Projects.JwtAuth_ApiTwo>("api-two");

await builder.Build().RunAsync();
