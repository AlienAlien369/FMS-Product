using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.E2eTests;

/// <summary>
/// xUnit fixture: one uniquely-named Postgres database + one booted API host per
/// test class. The REAL Program.cs runs (EnsureCreated + SchemaBootstrap + seed)
/// against real Postgres. Implement IAsyncLifetime so the DB is created before the
/// class's tests run and dropped after.
/// </summary>
public sealed class E2eFixture : IAsyncLifetime
{
    public E2eDb Db = null!;

    public async Task InitializeAsync() => Db = await E2eDb.CreateAsync();

    public async Task DisposeAsync()
    {
        if (Db != null) await Db.DisposeAsync();
    }
}

/// <summary>
/// A uniquely-named Postgres database with the REAL API booted against it, plus an
/// HttpClient and raw SQL access. Connection to the server comes from
/// E2E_TEST_POSTGRES (any DB on the target server is fine); defaults to the local
/// docker-compose Postgres (postgres/postgres on localhost:5432).
/// </summary>
public sealed class E2eDb : IAsyncDisposable
{
    public string DbName { get; }
    public string AppConnectionString { get; }
    private readonly string _adminConnectionString;
    private WebApplicationFactory<Program> _factory = null!;
    public HttpClient Client { get; private set; } = null!;

    private E2eDb(string adminConn, string appConn, string dbName)
    {
        _adminConnectionString = adminConn;
        AppConnectionString = appConn;
        DbName = dbName;
    }

    /// <summary>Admin connection (points at the postgres maintenance DB, not the test DB).</summary>
    public NpgsqlConnection OpenAdminConnection()
    {
        var conn = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = "postgres" }.ConnectionString);
        conn.Open();
        return conn;
    }

    /// <summary>Connection directly to the test database (raw SQL for shaping/assertions).</summary>
    public NpgsqlConnection OpenAppConnection()
    {
        var conn = new NpgsqlConnection(AppConnectionString);
        conn.Open();
        return conn;
    }

    public async Task<string?> ScalarAsync(string sql)
    {
        await using var conn = OpenAppConnection();
        await using var cmd = new NpgsqlCommand(sql, conn);
        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString();
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var conn = OpenAppConnection();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Shuts the current host down and boots a fresh one against the SAME database.
    /// Startup (EnsureCreated + SchemaBootstrap + seed migrations) therefore re-runs
    /// against whatever state the tests left behind — how the repair-on-legacy-drift
    /// scenarios are exercised.
    /// </summary>
    public async Task RebootAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await BootAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();

        // Drop the test database (FORCE kills lingering pooled connections).
        await using var conn = OpenAdminConnection();
        await using (var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{DbName}\" WITH (FORCE)", conn))
        {
            await drop.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Creates a unique test DB and boots the real API against it, waiting until
    /// /health returns 200 (i.e. schema bootstrap + seed completed).
    /// </summary>
    public static async Task<E2eDb> CreateAsync()
    {
        var adminConn = Environment.GetEnvironmentVariable("E2E_TEST_POSTGRES")
            ?? "Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres";

        var dbName = "freebuff_e2e_" + Guid.NewGuid().ToString("N")[..12];
        var appConn = new NpgsqlConnectionStringBuilder(adminConn) { Database = dbName }.ConnectionString;

        await using (var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(adminConn) { Database = "postgres" }.ConnectionString))
        {
            admin.Open();
            await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", admin))
            {
                await create.ExecuteNonQueryAsync();
            }
        }

        var db = new E2eDb(adminConn, appConn, dbName);
        await db.BootAsync();
        return db;
    }

    private async Task BootAsync()
    {
        var contentRoot = FindApiContentRoot();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseContentRoot(contentRoot);
                builder.UseSetting("ConnectionStrings:DefaultConnection", AppConnectionString);
            });

        Client = _factory.CreateClient();

        // Wait for health (startup runs EnsureCreated + SchemaBootstrap + seed first).
        var deadline = DateTime.UtcNow.AddSeconds(180);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var resp = await Client.GetAsync("/health");
                if (resp.IsSuccessStatusCode) return;
            }
            catch { /* host still starting */ }
            await Task.Delay(1000);
        }
        throw new TimeoutException("API did not become healthy within 180s against the e2e database.");
    }

    /// <summary>Locates src/Freebuff.Platform.Api (appsettings.json content root) from the test bin.</summary>
    private static string FindApiContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Freebuff.Platform.Api");
            if (File.Exists(Path.Combine(candidate, "Freebuff.Platform.Api.csproj")))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate src/Freebuff.Platform.Api from test output.");
    }
}

/// <summary>JSON helpers for the ApiResponse envelope { success, data, code, message }.</summary>
public static class ApiJson
{
    public static async Task<(int Status, JsonElement? Data)> SendAsync(HttpClient client, HttpMethod method,
        string url, object? body = null, string? token = null)
    {
        using var req = new HttpRequestMessage(method, url);
        if (token != null) req.Headers.Authorization = new("Bearer", token);
        if (body != null) req.Content = JsonContent.Create(body);
        using var resp = await client.SendAsync(req);
        var raw = await resp.Content.ReadAsStringAsync();
        JsonElement? data = null;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("data", out var d) && d.ValueKind != JsonValueKind.Null)
                data = d.Clone();
        }
        return ((int)resp.StatusCode, data);
    }

    /// <summary>Like SendAsync but returns the FULL JSON envelope so error codes
    /// (root-level "code" on failures) can be asserted too.</summary>
    public static async Task<(int Status, JsonElement Root)> SendRawAsync(HttpClient client, HttpMethod method,
        string url, object? body = null, string? token = null)
    {
        using var req = new HttpRequestMessage(method, url);
        if (token != null) req.Headers.Authorization = new("Bearer", token);
        if (body != null) req.Content = JsonContent.Create(body);
        using var resp = await client.SendAsync(req);
        var raw = await resp.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(raw))
            return ((int)resp.StatusCode, default);
        using var doc = JsonDocument.Parse(raw);
        return ((int)resp.StatusCode, doc.RootElement.Clone());
    }

    public static async Task<string?> LoginAsync(HttpClient client, string email, string password)
    {
        var (status, data) = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/login",
            new { email, password });
        if (status != 200 || data == null) return null;
        return data.Value.GetProperty("token").GetString();
    }

    public static bool ContainsPermission(JsonElement? data, string code)
    {
        if (data == null || !data.Value.TryGetProperty("permissions", out var perms)) return false;
        if (perms.ValueKind != JsonValueKind.Array) return false;
        return perms.EnumerateArray().Any(p => p.GetString() == code);
    }

    public static int PermissionCount(JsonElement? data)
    {
        if (data == null || !data.Value.TryGetProperty("permissions", out var perms)) return 0;
        return perms.ValueKind == JsonValueKind.Array ? perms.GetArrayLength() : 0;
    }
}

/// <summary>Small check recorder — collects named PASS/FAIL lines, prints them, fails at the end.</summary>
public sealed class Checker
{
    private readonly ITestOutputHelper _output;
    private readonly List<(string Name, bool Ok, string Detail)> _results = new();

    public Checker(ITestOutputHelper output) => _output = output;

    public void Check(string name, bool ok, string detail = "")
    {
        _results.Add((name, ok, detail));
        _output.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " — " + detail : "")}");
    }

    public void AssertAll()
    {
        var failed = _results.Where(r => !r.Ok).ToList();
        if (failed.Count > 0)
        {
            var msg = string.Join("\n", failed.Select(f => $"FAIL: {f.Name} {f.Detail}"));
            throw new Xunit.Sdk.XunitException($"E2E failed {failed.Count}/{_results.Count}:\n{msg}");
        }
    }
}
