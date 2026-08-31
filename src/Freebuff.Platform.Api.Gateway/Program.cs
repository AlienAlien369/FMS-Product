using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

// YARP Reverse Proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? new[] { "http://localhost:5173" })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Health Checks
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseCors();

// Health endpoint
app.MapHealthChecks("/health");

// Gateway info endpoint
app.MapGet("/gateway", () => new
{
    service = "Freebuff API Gateway",
    version = "1.0.0",
    routes = new[]
    {
        new { path = "/api/v1/auth/*", service = "Identity Service" },
        new { path = "/api/v1/users/*", service = "Identity Service" },
        new { path = "/api/v1/roles/*", service = "Identity Service" },
        new { path = "/api/v1/companies/*", service = "Tenant Service" },
        new { path = "/api/v1/subscriptions/*", service = "Tenant Service" },
        new { path = "/api/v1/packages/*", service = "Tenant Service" },
        new { path = "/api/v1/modules/*", service = "Tenant Service" },
        new { path = "/api/v1/vehicles/*", service = "Fleet Service" },
        new { path = "/api/v1/drivers/*", service = "Fleet Service" },
        new { path = "/api/v1/trips/*", service = "Fleet Service" },
        new { path = "/api/v1/geofences/*", service = "Fleet Service" },
        new { path = "/api/v1/clients/*", service = "Fleet Service" },
        new { path = "/api/v1/alerts/*", service = "Monitoring Service" },
        new { path = "/api/v1/notifications/*", service = "Monitoring Service" },
        new { path = "/api/v1/fuel/*", service = "Monitoring Service" },
        new { path = "/api/v1/maintenance/*", service = "Monitoring Service" },
        new { path = "/api/v1/documents/*", service = "Monitoring Service" },
    }
});

// Reverse Proxy
app.MapReverseProxy();

app.Run();
