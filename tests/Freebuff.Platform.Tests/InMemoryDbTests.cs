using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Freebuff.Platform.Api.Fleet.Data;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Xunit;
using Xunit.Abstractions;

namespace Freebuff.Platform.Tests;

public class InMemoryDbTests
{
    private readonly ITestOutputHelper _output;
    public InMemoryDbTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task InMemoryDb_SharedStore_AcrossScopes()
    {
        var dbName = "SharedStore_" + Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddDbContext<FleetDbContext>(options => options.UseInMemoryDatabase(dbName));
        var provider = services.BuildServiceProvider();
        var companyId = Guid.NewGuid();

        using (var scope1 = provider.CreateScope())
        {
            var db = scope1.ServiceProvider.GetRequiredService<FleetDbContext>();
            db.Vehicles.Add(new Vehicle
            {
                Id = Guid.NewGuid(), RegistrationNumber = "TEST-001", Name = "Test",
                Make = "Tata", CompanyId = companyId, Status = VehicleStatus.Active
            });
            await db.SaveChangesAsync();
            var count = await db.Vehicles.CountAsync();
            _output.WriteLine($"Scope1 count: {count}");
            Assert.Equal(1, count);
        }

        using (var scope2 = provider.CreateScope())
        {
            var db2 = scope2.ServiceProvider.GetRequiredService<FleetDbContext>();
            var count = await db2.Vehicles.CountAsync();
            _output.WriteLine($"Scope2 count: {count}");
            Assert.Equal(1, count);
        }
    }
}
