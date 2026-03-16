using Aspire.Hosting.DevJwt;

var builder = DistributedApplication.CreateBuilder(args);

var devJwt = builder.AddSharedDevJwtAuthority();

builder.AddJwtProject<Projects.JwtAuth_ApiOne>("api-one", devJwt);
builder.AddJwtProject<Projects.JwtAuth_ApiTwo>("api-two", devJwt);

await builder.Build().RunAsync();
