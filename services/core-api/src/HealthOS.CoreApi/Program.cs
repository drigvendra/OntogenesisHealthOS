using System.Text.Json;
using HealthOS.CoreApi.Composition;
using HealthOS.CoreApi.Health;
using HealthOS.CoreApi.Security;
using HealthOS.Modules.Sdk.Compliance;
using HealthOS.Modules.Sdk.Composition;
using HealthOS.Modules.Sdk.Context;
using HealthOS.Modules.Sdk.Security;
using HealthOS.Platform.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// `modules list`: the modules compiled into this package (used by CI to verify bundles; no database needed).
if (args is ["modules", "list"])
{
    var installed = InstalledModules.Create();
    var resolution = PackageResolver.Resolve(installed.Select(m => m.Manifest), PlatformInfo.CoreVersion);
    if (!resolution.Succeeded)
    {
        await Console.Error.WriteLineAsync(string.Join(Environment.NewLine, resolution.Issues)).ConfigureAwait(false);
        return 1;
    }

    foreach (var m in resolution.OrderedModules)
    {
        Console.WriteLine(m.Id);
    }

    return 0;
}

// Content root = the application's own directory, so appsettings*.json load regardless of the
// working directory the process was started from (e.g. `dotnet path/to/HealthOS.CoreApi.dll`).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// ---- Logging: structured JSON, classified data (PHI/PII/secrets) erased -------------------------
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);
builder.Logging.EnableRedaction();
builder.Services.AddHealthOsRedaction();

// ---- Observability: OpenTelemetry, exported only when an OTLP endpoint is configured ------------
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("healthos-core-api", serviceVersion: PlatformInfo.CoreVersion))
        .WithTracing(t => t
            .AddAspNetCoreInstrumentation(o => o.RecordException = false) // exception messages may carry data
            .AddHttpClientInstrumentation()
            .AddOtlpExporter())
        .WithMetrics(m => m
            .AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter());
}

// ---- Configuration validated at startup (fail fast) ---------------------------------------------
builder.Services.AddOptions<DatabaseSettings>()
    .BindConfiguration(DatabaseSettings.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(sp =>
    NpgsqlDataSource.Create(sp.GetRequiredService<IOptions<DatabaseSettings>>().Value.ConnectionString));
builder.Services.AddSingleton(sp => new PlatformDatabaseOptions
{
    AppConnectionString = sp.GetRequiredService<IOptions<DatabaseSettings>>().Value.ConnectionString,
    Roles = new DatabaseRoles(sp.GetRequiredService<IConfiguration>()["Database:RolePrefix"] ?? "hos"),
});

// ---- Security --------------------------------------------------------------------------------------
builder.Services.AddBffAuthentication(builder.Environment);
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<RequestContext>();
builder.Services.AddScoped<IRequestContext>(sp => sp.GetRequiredService<RequestContext>());
builder.Services.AddScoped<ISessionManager, CookieSessionManager>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddAuthorization(o =>
{
    // Defense in depth: an endpoint that somehow lacks a declaration still requires authentication.
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});
builder.Services.AddProblemDetails();

// ---- Health ----------------------------------------------------------------------------------------
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"]);

// ---- Modules ---------------------------------------------------------------------------------------
builder.AddHealthOsModules(InstalledModules.Create());

var app = builder.Build();

if (DatabaseCommands.IsDatabaseCommand(args))
{
    return await DatabaseCommands.RunAsync(app, args).ConfigureAwait(false);
}

if (PlatformCommands.IsPlatformCommand(args))
{
    return await PlatformCommands.RunAsync(app, args).ConfigureAwait(false);
}

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;
    h.XContentTypeOptions = "nosniff";
    h.XFrameOptions = "DENY";
    h["Referrer-Policy"] = "no-referrer";
    h.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
    await next().ConfigureAwait(false);
});

app.UseMiddleware<CsrfHeaderMiddleware>();
app.UseAuthentication();
app.UseMiddleware<RequestContextMiddleware>();
app.UseMiddleware<ModuleGateMiddleware>();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = WriteHealth })
    .AllowPublic("Liveness probe for orchestrators; reveals no data.");
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready"), ResponseWriter = WriteHealth })
    .AllowPublic("Readiness probe for orchestrators; reports dependency status only.");

app.MapBffEndpoints();
app.MapNavigation();
app.MapHealthOsModules();

await app.RunAsync().ConfigureAwait(false);
return 0;

static Task WriteHealth(HttpContext ctx, HealthReport report)
{
    ctx.Response.ContentType = "application/json";
    var body = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
    };
    return ctx.Response.WriteAsync(JsonSerializer.Serialize(body));
}

public partial class Program;
