using System.Text;
using System.Threading.RateLimiting;
using Freebuff.Platform.Application.Interfaces;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Api.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ──────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/freebuff-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();
builder.Host.UseSerilog();

// ── Database ─────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.MigrationsAssembly("Freebuff.Platform.Infrastructure");
        npgsql.EnableRetryOnFailure(3);
    }));

// ── Multi-tenancy ────────────────────────────────────────
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();

// ── Authentication ───────────────────────────────────────
var jwtKey = builder.Configuration["Jwt:Key"] ?? "DevSecretKey_Change_In_Production_32chars!";
// Distributed cache for the permitted-company set: Redis when configured (prod),
// in-memory otherwise (local dev) — see Redis:Url config / REDIS_URL env.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Redis:Url"])
    || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REDIS_URL")))
{
    var redisUrl = builder.Configuration["Redis:Url"]
        ?? Environment.GetEnvironmentVariable("REDIS_URL");
    builder.Services.AddStackExchangeRedisCache(o => { o.Configuration = redisUrl; o.InstanceName = "freebuff:"; });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}
builder.Services.AddScoped<Freebuff.Platform.Infrastructure.CompanyScope.ICompanyScopeResolver, Freebuff.Platform.Infrastructure.CompanyScope.CompanyScopeResolver>();
builder.Services.AddScoped<Freebuff.Platform.Infrastructure.CompanyScope.TargetCompanyResolver>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "freebuff",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "freebuff",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
        // SignalR browsers cannot set Authorization headers on WebSocket
        // connections, so the SignalR JS client appends the JWT as the
        // access_token query parameter (standard SignalR convention). The same
        // applies to <img> requests for stored POD photos — the photo-serve path
        // accepts the token in the query string for that reason only.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken)
                    && (path.StartsWithSegments("/hubs")
                        || (path.StartsWithSegments("/api/v1/trips") && path.Value!.Contains("/pod/photos/"))))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// ── Services ─────────────────────────────────────────────
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<VehicleService>();
builder.Services.AddScoped<DriverService>();
builder.Services.AddScoped<TripLifecycleService>();
builder.Services.AddScoped<TripGeofenceEventProducer>();
builder.Services.AddScoped<DriverBehaviorAlertProducer>();
builder.Services.AddScoped<ProofOfDeliveryService>(sp => new ProofOfDeliveryService(
    sp.GetRequiredService<ApplicationDbContext>(),
    builder.Configuration["Storage:UploadsPath"] ?? "uploads"));
builder.Services.AddScoped<TripShareLinkService>();
builder.Services.AddScoped<FleetPolicyService>();
builder.Services.AddScoped<SensorPolicyAlertProducer>();
builder.Services.AddHostedService<SensorRetentionService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();

// ── Device abstraction layer ─────────────────────────────
builder.Services.AddSingleton<Freebuff.Platform.Ingestion.Registry.VendorAdapterRegistry>(
    Freebuff.Platform.Ingestion.Registry.VendorAdapterRegistry.CreateBuiltIn());
builder.Services.AddSingleton<Freebuff.Platform.Ingestion.Contracts.IVendorAdapterRegistry>(
    sp => sp.GetRequiredService<Freebuff.Platform.Ingestion.Registry.VendorAdapterRegistry>());
builder.Services.AddScoped<DeviceService>();
builder.Services.AddScoped<DeviceIngestionService>();
builder.Services.AddScoped<IAlertTypeEnforcement, AlertTypeEnforcement>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<Freebuff.Platform.Infrastructure.Services.INotificationRealtimeChannel,
    Freebuff.Platform.Api.Hubs.SignalRNotificationChannel>();

// ── Rate limiting (public, unauthenticated surfaces) ─────
// The Customer Tracking Link endpoints are reachable without login by design,
// so they get their own limiter keyed per TOKEN (a scraped link can't be
// hammered) AND per IP (rapid token-guessing bursts are throttled — each guess
// is a fresh token, so a token-keyed limiter alone would never engage). One
// shared policy chains both limiters; a rejection from either yields 429.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = new PublicTrackingRateLimitPolicy(new FixedWindowRateLimiterOptions
    {
        PermitLimit = 60,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0
    }).Limiter;
});

// ── Controllers + Swagger ────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Freebuff Platform API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ── CORS ─────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>();
        if (origins == null || origins.Length == 0)
        {
            origins = new[]
            {
                "http://localhost:5173",
                "http://localhost:5174",
                "https://fms-product-lakshyas-projects-c97e54f6.vercel.app",
                "https://fms-product.vercel.app"
            };
        }
        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// ── Health Checks ────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgresql");

var app = builder.Build();

// ── Pipeline ─────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors();
// Explicit routing BEFORE the rate limiter: RateLimiterMiddleware resolves the
// [EnableRateLimiting] attribute + route values (token) from the matched
// endpoint — with implicit routing it sees no endpoint and silently skips.
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
// Resolves X-Company-Scope into an effective per-request company scope (stateless).
app.UseMiddleware<CompanyScopeMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health");
// Real-time notification channel: the bell subscribes here and receives
// "notification.new" pushes instead of polling.
app.MapHub<Freebuff.Platform.Api.Hubs.NotificationHub>("/hubs/notifications");

// ── Auto-setup database ─────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.EnsureCreatedAsync();
    // EnsureCreated is a no-op on existing DBs, so tables added after the initial
    // release are created here (idempotent) before the seed migration runs.
    await Freebuff.Platform.Infrastructure.Data.SchemaBootstrap.EnsureSchemaAsync(db);
    await Freebuff.Platform.Infrastructure.Data.SeedData.SeedAsync(db);
    // Device Abstraction Layer: seed vendor catalog + backfill legacy
    // Vehicle.Device* columns into Device/VehicleDevice/TelemetryState.
    var migrationLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("DeviceDataMigration");
    await Freebuff.Platform.Infrastructure.Data.DeviceDataMigration.EnsureAsync(db, migrationLogger);
}

app.Run();

// Exposes the auto-generated Program to WebApplicationFactory<Program> so the
// integration/e2e test project can boot the real API in-process (startup seed
// and schema bootstrap included) against a real Postgres.
public partial class Program { }

