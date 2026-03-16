var builder = DistributedApplication.CreateBuilder(args);

// Add the two sample APIs with a shared JWT bearer token configuration.
// Both APIs use the same signing key so tokens issued by ApiOne can be
// validated by ApiTwo and vice-versa (aspire-wide JWT solution).
var jwtKey = builder.AddParameter("JwtSigningKey", secret: true);

var apiOne = builder.AddProject<Projects.JwtAuth_ApiOne>("api-one")
    .WithEnvironment("Jwt__SigningKey", jwtKey);

var apiTwo = builder.AddProject<Projects.JwtAuth_ApiTwo>("api-two")
    .WithEnvironment("Jwt__SigningKey", jwtKey);

await builder.Build().RunAsync();
