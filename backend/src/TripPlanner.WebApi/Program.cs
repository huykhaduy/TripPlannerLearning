using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
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

// ---------------------------------------------------------------------------
// 2. Authentication — validate the JWTs issued by JwtTokenGenerator.
// ---------------------------------------------------------------------------
var jwtSection = builder.Configuration.GetSection(JwtSettings.SectionName);

// Fail fast, naming both the setting and the file it belongs in. Note the check
// is for BLANK, not just missing: .env.example ships "Jwt__Key=" with no value,
// so the usual first-run mistake is an empty string. Left unguarded, startup
// died inside Encoding.UTF8.GetBytes/SymmetricSecurityKey with a message
// ("Value cannot be null. (Parameter 's')") that names neither.
var jwtKey = jwtSection["Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException(
        "Jwt__Key is not configured. Copy backend/src/TripPlanner.WebApi/.env.example to .env "
        + "and set Jwt__Key to a random secret of at least 32 characters.");
}

var jwtKeyBytes = Encoding.UTF8.GetBytes(jwtKey);
if (jwtKeyBytes.Length < JwtSettings.MinKeyBytes)
{
    // A short key survives startup but throws on the FIRST login attempt,
    // deep inside the signing call — catch it here where the fix is obvious.
    throw new InvalidOperationException(
        $"Jwt__Key is too short ({jwtKeyBytes.Length} bytes). HMAC-SHA256 signing requires at least "
        + $"{JwtSettings.MinKeyBytes} — set Jwt__Key in backend/src/TripPlanner.WebApi/.env to a longer random secret.");
}

var signingKey = new SymmetricSecurityKey(jwtKeyBytes);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSection["Issuer"] ?? JwtSettings.DefaultIssuer,
            ValidAudience = jwtSection["Audience"] ?? JwtSettings.DefaultAudience,
            IssuerSigningKey = signingKey,
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
