using System.Security.Claims;
using JwtAuth.ApiTwo;
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

Product[] products =
[
    new(1, "Widget A", 9.99m),
    new(2, "Widget B", 19.99m),
    new(3, "Gadget X", 49.99m),
    new(4, "Gadget Y", 99.99m),
    new(5, "Super Tool", 149.99m),
];

app.MapGet("/products", [Authorize] () => products)
.WithName("GetProducts")
.WithSummary("Get product catalogue (requires bearer token)");

app.MapGet("/products/{id:int}", [Authorize] (int id) =>
{
    var product = products.FirstOrDefault(p => p.Id == id);
    return product is not null ? Results.Ok(product) : Results.NotFound();
})
.WithName("GetProductById")
.WithSummary("Get a product by id (requires bearer token)");

app.MapGet("/me", [Authorize] (ClaimsPrincipal user) =>
    Results.Ok(new
    {
        username = user.Identity?.Name,
        roles = user.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value),
        service = "ApiTwo"
    }))
.WithName("GetCurrentUser")
.WithSummary("Get the current authenticated user (requires bearer token)");

await app.RunAsync();

namespace JwtAuth.ApiTwo
{
    internal record Product(int Id, string Name, decimal Price);
}
