using System.Security.Claims;
using JwtAuth.ApiOne;
using JwtAuth.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);


builder.AddServiceDefaults();


// Add services to the container.
builder.Services.AddOpenApi();

builder.Services.AddAuthentication().AddJwtBearer();
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/weatherforecast", [Authorize(Roles = "api-one")] () =>
{
    var summaries = new[] { "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching" };
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
.WithSummary("Get weather forecast (requires bearer token)");

app.MapGet("/me", [Authorize(Roles = "api-one")] (ClaimsPrincipal user) =>
    Results.Ok(new
    {
        username = user.Identity?.Name,
        roles = user.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value),
        service = "ApiOne"
    }))
.WithName("GetCurrentUser")
.WithSummary("Get the current authenticated user (requires bearer token)");

app.MapDefaultEndpoints();

await app.RunAsync();