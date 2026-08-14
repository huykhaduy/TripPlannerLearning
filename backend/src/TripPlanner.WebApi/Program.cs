using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using TripPlanner.Application;
using TripPlanner.Infrastructure;
using TripPlanner.Infrastructure.Identity;
using TripPlanner.Infrastructure.Persistence;
using TripPlanner.WebApi;
using TripPlanner.WebApi.Middleware;

// Local overrides (API keys etc.) — git-ignored .env file, loaded into process
// environment variables before the builder reads them. Config keys use "__" to
// express nesting (e.g. Geoapify__ApiKey -> Geoapify:ApiKey), since
// AddEnvironmentVariables() (added by CreateBuilder below) treats "__" as the
// section separator.
DotNetEnv.Env.Load();

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// 1. Register the layers. Each layer owns its own DI extension method, so this
//    composition root stays small and readable.
// ---------------------------------------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddWebApi();

builder.Services.AddControllers();

// Liveness for the container orchestrator. No database probe on purpose: migrations
// are applied before app.Run() below, so the app answers no HTTP at all until they
// have finished — a plain 200 here already means "migrated and serving", which is the
// only question Coolify asks. A DbContextCheck would add a NuGet package to report
// something the next real request reports anyway.
builder.Services.AddHealthChecks();

// ---------------------------------------------------------------------------
// 2. Authentication — validate the JWTs issued by JwtTokenGenerator.
// ---------------------------------------------------------------------------
// JwtSettings is bound and validated in AddInfrastructure (including the
// fail-at-startup checks on Jwt__Key). Reading it through IOptions here rather than
// off builder.Configuration keeps configuration resolution deferred, which is what
// lets the test host supply its own values — see CustomWebApplicationFactory.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((bearer, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            // JwtSettings defaults these, so no ?? fallback is needed here.
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ClockSkew = TimeSpan.Zero,
        };
    });

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// 3. CORS — allow the React dev server (Vite) to call the API.
// ---------------------------------------------------------------------------
const string CorsPolicy = "AllowFrontend";
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                      ?? ["http://localhost:5173"];
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
    });
});

// ---------------------------------------------------------------------------
// 4. Swagger / OpenAPI with a JWT "Authorize" button.
// ---------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "TripPlanner API", Version = "v1" });

    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the JWT returned from /api/auth/login.",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
    };
    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });
});

var app = builder.Build();

// ---------------------------------------------------------------------------
// 5. Apply EF Core migrations automatically on startup (handy in development).
//    Run `dotnet ef migrations add InitialCreate` once before the first start.
//    Skipped in the "Testing" environment: WebApplicationFactory-based tests
//    swap in the EF Core InMemory provider, which doesn't support migrations
//    at all (GetPendingMigrations throws on a non-relational provider).
// ---------------------------------------------------------------------------
if (!app.Environment.IsEnvironment("Testing"))
{
    await ApplyMigrationsAsync(app);
}

// ---------------------------------------------------------------------------
// 6. HTTP request pipeline. Order matters.
// ---------------------------------------------------------------------------
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(CorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Anonymous by design — the healthcheck in docker-compose.deploy.yml runs curl from
// inside the container and has no token. The body is the literal string "Healthy" and
// discloses nothing else. Outside /api, so it cannot collide with a controller route.
app.MapHealthChecks("/health");

app.Run();

static async Task ApplyMigrationsAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (db.Database.GetPendingMigrations().Any())
    {
        await db.Database.MigrateAsync();
    }
}

// Exposed so the integration-test host (WebApplicationFactory) can reference it.
public partial class Program { }
