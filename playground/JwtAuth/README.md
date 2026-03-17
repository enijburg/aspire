# JwtAuth Playground

A .NET Aspire playground that demonstrates shared JWT bearer authentication across multiple API services, with an integration test project orchestrated by the AppHost.

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                   JwtAuth.AppHost                       │
│                                                         │
│  ┌──────────────┐   env vars   ┌──────────────────────┐ │
│  │  dev-jwt      │────────────▶│  api-one (ApiOne)    │ │
│  │  (authority)  │────────────▶│  api-two (ApiTwo)    │ │
│  │              │────────────▶│  tests  (Tests)      │ │
│  └──────────────┘             └──────────────────────┘ │
│                                       │                 │
│              WithReference(apiOne) ───┘                 │
│              WithReference(apiTwo) ───┘                 │
└─────────────────────────────────────────────────────────┘
```

The AppHost creates a shared development JWT authority (`dev-jwt`) and distributes its signing key, issuer, and audience to all services and the test project via environment variables.

## Projects

| Project | Description |
|---|---|
| **JwtAuth.AppHost** | Aspire orchestrator. Registers the JWT authority, both APIs, and the test project. |
| **JwtAuth.ApiOne** | Minimal API with `/weatherforecast` and `/me` endpoints, protected by `[Authorize]`. |
| **JwtAuth.ApiTwo** | Minimal API with `/products`, `/products/{id}`, and `/me` endpoints, protected by `[Authorize]`. |
| **JwtAuth.Tests** | MSTest integration tests that run against both APIs using a self-minted JWT. |
| **JwtAuth.ServiceDefaults** | Shared Aspire service defaults (OpenTelemetry, resilience, service discovery). |
| **Aspire.Hosting.DevJwt** | Reusable library providing the `AddSharedDevJwtAuthority`, `AddJwtProject`, and `WithSharedDevJwt` extension methods. |

## How JWT Authentication Works

1. **Key generation** — `AddSharedDevJwtAuthority()` checks the AppHost's user-secrets for a signing key (`DevJwt:SigningKey`). If none exists, a 256-bit HMAC-SHA256 key is generated and persisted automatically.

2. **Environment variable injection** — `WithSharedDevJwt(devJwt)` (or the shorthand `AddJwtProject`) injects these environment variables into each service:

   | Variable | Example value |
   |---|---|
   | `Authentication__Schemes__Bearer__ValidIssuer` | `https://dev-jwt.local` |
   | `Authentication__Schemes__Bearer__ValidAudiences__0` | `microservices-dev` |
   | `Authentication__Schemes__Bearer__SigningKeys__0__Issuer` | `https://dev-jwt.local` |
   | `Authentication__Schemes__Bearer__SigningKeys__0__Value` | *(base64 key)* |

3. **Bearer validation** — Each API calls `builder.Services.AddAuthentication().AddJwtBearer()` with no extra configuration. ASP.NET Core's `JwtBearerOptions` auto-binds from the `Authentication:Schemes:Bearer:*` configuration keys, which are populated by the environment variables above.

4. **Token minting** — The test project (and the dashboard's "Generate JWT" command) use `JwtTokenFactory.CreateToken(...)` to produce signed tokens from the same key.

## API Endpoints

### ApiOne (`api-one`)

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/weatherforecast` | Bearer | Returns 5 random weather forecasts. |
| GET | `/me` | Bearer | Returns the authenticated user's claims with `"service": "ApiOne"`. |

### ApiTwo (`api-two`)

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/products` | Bearer | Returns the full product catalogue. |
| GET | `/products/{id}` | Bearer | Returns a single product or 404. |
| GET | `/me` | Bearer | Returns the authenticated user's claims with `"service": "ApiTwo"`. |

## Test Project

`JwtAuth.Tests` is an MSTest project registered in the AppHost with `AddProject` and configured with:

- **`WithSharedDevJwt(devJwt)`** — receives the signing key and JWT configuration.
- **`WithReference(apiOne)` / `WithReference(apiTwo)`** — receives service endpoint URLs as `services__api-one__https__0` (and `http` fallback) environment variables.
- **`WaitFor(apiOne)` / `WaitFor(apiTwo)`** — ensures both APIs are healthy before the tests start.
- **`WithArgs("--settings", "test.runsettings")`** — passes the runsettings file to the MSTest runner so `Console.WriteLine` output flows to stdout and appears in the Aspire dashboard console logs.
- **`WithExplicitStart()`** — the test project does not auto-start with the AppHost; it must be started manually from the Aspire dashboard.
- **`EnableMSTestRunner`** — set to `true` in the `.csproj` so the project runs tests when launched as an executable (required for Aspire orchestration).
- **`test.runsettings`** — disables MSTest's `CaptureTraceOutput` (which redirects `Console.Out` into per-test buffers) so diagnostic output reaches stdout and is visible in the Aspire dashboard.

### Test Flow

1. `ClassInitialize` reads the JWT signing key and service URLs from environment variables.
2. A bearer token is minted using `JwtTokenFactory.CreateToken` with subject `test-user` and roles `admin`, `reader`.
3. Each test creates an `HttpClient` with the bearer token and calls API endpoints.
4. Unauthorized access tests verify that requests without a token return `401`.

### Running the Tests

Start the AppHost (F5 or `dotnet run` from `JwtAuth.AppHost`), then trigger the `tests` resource from the Aspire dashboard. The tests will execute in-process using the MSTest runner and report results back to the dashboard.

## AppHost Configuration

```csharp
// apphost.cs
var devJwt = builder.AddSharedDevJwtAuthority();

var apiOne = builder.AddJwtProject<Projects.JwtAuth_ApiOne>("api-one", devJwt);
var apiTwo = builder.AddJwtProject<Projects.JwtAuth_ApiTwo>("api-two", devJwt);

builder.AddProject<Projects.JwtAuth_Tests>("tests")
    .WithSharedDevJwt(devJwt)
    .WithReference(apiOne)
    .WithReference(apiTwo)
    .WaitFor(apiOne)
    .WaitFor(apiTwo)
    .WithArgs("--settings", "test.runsettings")
    .WithExplicitStart();
```

## Prerequisites

- .NET 10 SDK
- Aspire AppHost SDK 13.1.2
