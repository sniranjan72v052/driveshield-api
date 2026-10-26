using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DriveShield.Api.Application;
using DriveShield.Api.Auth;
using DriveShield.Api.Domain;
using DriveShield.Api.Endpoints;
using DriveShield.Api.Hubs;
using DriveShield.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
// Your own settings (for example the admin email) go in appsettings.Local.json. It is never part of an update, so replacing
// the project files cannot reset it. It is read last, so it wins over appsettings.json and appsettings.Development.json.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
var config = builder.Configuration;
var isDevelopment = builder.Environment.IsDevelopment();

// ---------------------------------------------------------------- database (PostgreSQL + PostGIS)
builder.Services.AddDbContext<AppDbContext>(o => o
    .UseNpgsql(config.GetConnectionString("Default"), npgsql => npgsql.UseNetTopologySuite())
    .UseSnakeCaseNamingConvention());

// ---------------------------------------------------------------- app services
builder.Services.Configure<MatchingOptions>(config.GetSection("Matching"));
builder.Services.AddScoped<UserResolver>();
builder.Services.AddScoped<CaseWorkflowService>();
builder.Services.AddScoped<AdminAssignmentService>();
builder.Services.AddHostedService<OfferExpiryWorker>();
builder.Services.AddHostedService<AuthCleanupWorker>();
// Photos are kept on local disk for now. For production, swap in an Azure Blob implementation of IPhotoStore here.
builder.Services.AddSingleton<IPhotoStore, LocalPhotoStore>();

// Enums travel as strings ("EnRoute") in both REST and SignalR payloads.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// For Azure SignalR Service later: add the Microsoft.Azure.SignalR package and chain .AddAzureSignalR().
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The API description (/openapi/v1.json) with a "Bearer" sign-in scheme, so Swagger UI gets an Authorize button.
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "DriveShield API";
    document.Components ??= new OpenApiComponents();
    document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste the accessToken from POST /api/auth/otp/verify. No \"Bearer \" prefix."
    };
    document.SecurityRequirements.Add(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
    });
    return Task.CompletedTask;
}));

// ---------------------------------------------------------------- sign-in: one-time codes by email or SMS
builder.Services.Configure<JwtOptions>(config.GetSection("Auth:Jwt"));
builder.Services.Configure<OtpOptions>(config.GetSection("Auth:Otp"));
builder.Services.Configure<AccountOptions>(config.GetSection("Auth"));
builder.Services.Configure<SmtpOptions>(config.GetSection("Email:Smtp"));
builder.Services.Configure<TwilioOptions>(config.GetSection("Sms:Twilio"));

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<OtpService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddHttpClient("twilio", c => c.Timeout = TimeSpan.FromSeconds(15));

// "Console" writes codes to the API log instead of sending them. It must never run outside Development,
// or anyone with access to the logs could sign in as anyone.
var delivery = config["Auth:Otp:Delivery"] ?? "Live";
if (delivery.Equals("Console", StringComparison.OrdinalIgnoreCase))
{
    if (!isDevelopment)
        throw new InvalidOperationException(
            "Auth:Otp:Delivery is Console, which is for local development only. Set it to Live and fill in Email:Smtp and Sms:Twilio.");
    builder.Services.AddSingleton<IOtpSender, ConsoleOtpSender>();
}
else
{
    builder.Services.AddSingleton<IOtpSender, LiveOtpSender>();
}

var jwt = config.GetSection("Auth:Jwt").Get<JwtOptions>() ?? new JwtOptions();
//if (Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
//    throw new InvalidOperationException(
//        "Auth:Jwt:SigningKey is missing or shorter than 32 characters. Set it to a long random secret " +
//        "(environment variable Auth__Jwt__SigningKey).");
if (!isDevelopment && jwt.SigningKey.StartsWith("dev-only", StringComparison.Ordinal))
    throw new InvalidOperationException("The development signing key is being used outside Development. Set Auth__Jwt__SigningKey to your own secret.");

// ---------------------------------------------------------------- authentication
// Everyone signs in with a code and then sends the access token we issue as "Authorization: Bearer ...".
// In Development only, the header "X-Dev-User: name" (or ?dev_user=name) also works, for curl and quick tests.
const string DevOrBearer = "DevOrBearer";
var auth = builder.Services.AddAuthentication(o =>
{
    var scheme = isDevelopment ? DevOrBearer : JwtBearerDefaults.AuthenticationScheme;
    o.DefaultScheme = scheme;
    o.DefaultAuthenticateScheme = scheme;
    o.DefaultChallengeScheme = scheme;
})
.AddJwtBearer(o =>
{
    o.MapInboundClaims = false; // keep raw claim names: "sub", "name"
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwt.Issuer,
        ValidateAudience = true,
        ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    };
    o.Events = new JwtBearerEvents
    {
        // Browsers cannot set headers on WebSocket connections, so SignalR sends the token in the query string.
        OnMessageReceived = ctx =>
        {
            var token = ctx.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                ctx.Token = token;
            return Task.CompletedTask;
        }
    };
});

if (isDevelopment)
{
    auth.AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.Scheme, null);
    auth.AddPolicyScheme(DevOrBearer, "Dev header or bearer token", o =>
    {
        o.ForwardDefaultSelector = ctx =>
            ctx.Request.Headers.ContainsKey("X-Dev-User") || ctx.Request.Query.ContainsKey("dev_user")
                ? DevAuthHandler.Scheme
                : JwtBearerDefaults.AuthenticationScheme;
    });
}
builder.Services.AddAuthorization();

// Slows down guessing and abuse on the sign-in routes. The limits per email/number and per code live in OtpService.
// Behind a proxy or load balancer the address seen here is the proxy's: turn on forwarded headers there
// (in Azure App Service for Linux, set ASPNETCORE_FORWARDEDHEADERS_ENABLED=true).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// ---------------------------------------------------------------- CORS for the React web app
var origins = config.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

// A token without a subject claim is a 401, not a 500.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (UnauthorizedAccessException) when (!ctx.Response.HasStarted) { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; }
});

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Swagger UI at /swagger. Development only: production exposes neither this page nor the API description.
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "DriveShield API v1");
        o.RoutePrefix = "swagger";
        o.EnablePersistAuthorization(); // keeps the pasted token when the page is reloaded
    });
    await DevSetup.InitializeAsync(app.Services, config);
}

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapVehicleEndpoints();
app.MapCaseEndpoints();
app.MapCommentEndpoints();
app.MapProviderEndpoints();
app.MapAdminEndpoints();
app.MapHub<CaseHub>("/hubs/case");

var adminCount = config.GetSection("Auth:AdminEmails").Get<string[]>()?.Count(e => !string.IsNullOrWhiteSpace(e) && !e.Contains("replace-with-your-email")) ?? 0;
app.Logger.LogInformation(
    adminCount == 0
        ? "No admin email is set. Copy appsettings.Local.json.example to appsettings.Local.json and put your email in it."
        : "Admin emails configured: {Count}.",
    adminCount);

app.Run();

/// <summary>Development convenience: create the database from the EF model and seed an admin user.</summary>
static class DevSetup
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Skip when you prefer db/schema.sql or EF migrations: set Database:AutoCreate to false.
        if (config.GetValue("Database:AutoCreate", true))
        {
            var creator = db.GetService<IRelationalDatabaseCreator>();
            if (!await creator.ExistsAsync()) await creator.CreateAsync();
            await db.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS postgis");
            await db.Database.EnsureCreatedAsync(); // creates the tables when the database is still empty

            // EnsureCreated does nothing once tables exist, so newer tables arrive as small idempotent SQL patches
            // (db/patches/*.sql, copied next to the API by the project file).
            var patchDir = Path.Combine(AppContext.BaseDirectory, "db", "patches");
            if (Directory.Exists(patchDir))
            {
                foreach (var patch in Directory.GetFiles(patchDir, "*.sql").OrderBy(f => f, StringComparer.Ordinal))
                    await db.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(patch));
            }
        }

        // For curl and quick tests: header X-Dev-User: dev-admin acts as an admin. The web app signs in with a code instead
        // (add your email to Auth:AdminEmails to be an admin there).
        if (!await db.Users.AnyAsync(u => u.ExternalId == "dev-admin"))
        {
            db.Users.Add(new AppUser { ExternalId = "dev-admin", DisplayName = "Dev Admin", Role = UserRole.Admin });
            await db.SaveChangesAsync();
        }
    }
}
