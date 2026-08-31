using System.Security.Claims;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Shared.Models;
using Freebuff.Platform.Api.Fleet.Data;
using Freebuff.Platform.Api.Fleet.Controllers;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Freebuff.Platform.Tests;

/// <summary>
/// Fleet controller unit tests. Uses a single ServiceCollection provider (no dual-provider issues).
/// Tests actual controller logic including tenant isolation, CRUD, and query behavior.
/// </summary>
public class FleetServiceIntegrationTests
{
    private static (ServiceProvider provider, IServiceScope scope, FleetDbContext db) CreateScope()
    {
        var services = new ServiceCollection();
        services.AddDbContext<FleetDbContext>(options =>
            options.UseInMemoryDatabase("FleetTest_" + Guid.NewGuid()));
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        return (provider, scope, db);
    }

    private static VehiclesController CreateController(FleetDbContext db, Guid companyId)
    {
        var controller = new VehiclesController(db);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new("tenant_id", companyId.ToString()),
            new(ClaimTypes.Role, "Admin")
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
            }
        };
        return controller;
    }

    [Fact]
    public async Task VehicleCRUD_FullFlow()
    {
        var (provider, scope, db) = CreateScope();
        var companyId = Guid.NewGuid();

        try
        {
            var controller = CreateController(db, companyId);

            // 1. List (empty)
            var listResult = await controller.GetAll(new PagedRequest());
            var listOk = Assert.IsType<OkObjectResult>(listResult.Result);
            var listData = Assert.IsType<ApiResponse<PagedResult<VehicleDto>>>(listOk.Value);
            Assert.Empty(listData.Data!.Items);

            // 2. Create
            var createResult = await controller.Create(new CreateVehicleDto
            {
                RegistrationNumber = "KA-01-AB-1234", Name = "Truck 1",
                Make = "Tata", Model = "Ace"
            });
            var created = Assert.IsType<CreatedAtActionResult>(createResult.Result);
            var createData = Assert.IsType<ApiResponse<VehicleDto>>(created.Value);
            Assert.Equal("KA-01-AB-1234", createData.Data!.RegistrationNumber);
            var vehicleId = createData.Data.Id;

            // 3. Create second
            var create2Result = await controller.Create(new CreateVehicleDto
            {
                RegistrationNumber = "MH-02-CD-5678", Name = "Truck 2", Make = "Mahindra"
            });
            Assert.IsType<CreatedAtActionResult>(create2Result.Result);

            // 4. List (should have 2)
            var list2Result = await controller.GetAll(new PagedRequest());
            var list2Ok = Assert.IsType<OkObjectResult>(list2Result.Result);
            var list2Data = Assert.IsType<ApiResponse<PagedResult<VehicleDto>>>(list2Ok.Value);
            Assert.Equal(2, list2Data.Data!.Items.Count);

            // 5. Get by ID
            var getResult = await controller.GetById(vehicleId);
            var getOk = Assert.IsType<OkObjectResult>(getResult.Result);
            var getData = Assert.IsType<ApiResponse<VehicleDto>>(getOk.Value);
            Assert.Equal("KA-01-AB-1234", getData.Data!.RegistrationNumber);

            // 6. Update
            var updateResult = await controller.Update(vehicleId, new UpdateVehicleDto { Name = "Updated Truck" });
            var updateOk = Assert.IsType<OkObjectResult>(updateResult.Result);
            var updateData = Assert.IsType<ApiResponse<VehicleDto>>(updateOk.Value);
            Assert.Equal("Updated Truck", updateData.Data!.Name);

            // 7. Delete
            var deleteResult = await controller.Delete(vehicleId);
            Assert.IsType<OkObjectResult>(deleteResult.Result);

            // 8. Get deleted → 404
            var getDeletedResult = await controller.GetById(vehicleId);
            Assert.IsType<NotFoundObjectResult>(getDeletedResult.Result);
        }
        finally
        {
            scope.Dispose();
            provider.Dispose();
        }
    }

    [Fact]
    public async Task TenantIsolation_CrossCompany_Blocked()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var (provider, scope, db) = CreateScope();

        try
        {
            // Create vehicle as Company A
            var controllerA = CreateController(db, companyA);
            await controllerA.Create(new CreateVehicleDto
            {
                RegistrationNumber = "A-001", Name = "A Vehicle"
            });

            // Create vehicle as Company B
            var controllerB = CreateController(db, companyB);
            await controllerB.Create(new CreateVehicleDto
            {
                RegistrationNumber = "B-001", Name = "B Vehicle"
            });

            // List as Company A → should only see Company A's vehicle
            var listResult = await controllerA.GetAll(new PagedRequest());
            var listOk = Assert.IsType<OkObjectResult>(listResult.Result);
            var listData = Assert.IsType<ApiResponse<PagedResult<VehicleDto>>>(listOk.Value);
            Assert.Single(listData.Data!.Items);
            Assert.Equal("A-001", listData.Data.Items[0].RegistrationNumber);

            // List as Company B → should only see Company B's vehicle
            var listBResult = await controllerB.GetAll(new PagedRequest());
            var listBOk = Assert.IsType<OkObjectResult>(listBResult.Result);
            var listBData = Assert.IsType<ApiResponse<PagedResult<VehicleDto>>>(listBOk.Value);
            Assert.Single(listBData.Data!.Items);
            Assert.Equal("B-001", listBData.Data.Items[0].RegistrationNumber);
        }
        finally
        {
            scope.Dispose();
            provider.Dispose();
        }
    }

    [Fact]
    public async Task GetById_NotFound_Returns404()
    {
        var (provider, scope, db) = CreateScope();
        try
        {
            var controller = CreateController(db, Guid.NewGuid());
            var result = await controller.GetById(Guid.NewGuid());
            Assert.IsType<NotFoundObjectResult>(result.Result);
        }
        finally
        {
            scope.Dispose();
            provider.Dispose();
        }
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        var (provider, scope, db) = CreateScope();
        try
        {
            var controller = CreateController(db, Guid.NewGuid());
            var result = await controller.Delete(Guid.NewGuid());
            Assert.IsType<NotFoundObjectResult>(result.Result);
        }
        finally
        {
            scope.Dispose();
            provider.Dispose();
        }
    }
}
