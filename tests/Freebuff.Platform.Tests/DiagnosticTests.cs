using Microsoft.EntityFrameworkCore;
using Freebuff.Platform.Api.Fleet.Data;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.Tests;

public class DiagnosticTests
{
    private readonly ITestOutputHelper _output;
    public DiagnosticTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Fleet_InMemoryDb_DirectQuery()
    {
        var options = new DbContextOptionsBuilder<FleetDbContext>()
            .UseInMemoryDatabase("DirectDiag_" + Guid.NewGuid())
            .Options;

        using var db = new FleetDbContext(options);
        var companyId = Guid.NewGuid();
        db.Vehicles.Add(new Vehicle
        {
            Id = Guid.NewGuid(), RegistrationNumber = "DIAG-001", Name = "Test",
            Make = "Tata", CompanyId = companyId, Status = VehicleStatus.Active
        });
        await db.SaveChangesAsync();

        _output.WriteLine($"Saved vehicle. Count: {await db.Vehicles.CountAsync()}");

        var results = await db.Vehicles.AsNoTracking()
            .Where(v => !v.IsDeleted && v.CompanyId == companyId)
            .ToListAsync();

        _output.WriteLine($"Query returned {results.Count} vehicles");
        Assert.Single(results);
    }
}
