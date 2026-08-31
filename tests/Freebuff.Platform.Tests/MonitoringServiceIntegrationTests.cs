using System.Security.Claims;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Shared.Models;
using Freebuff.Platform.Api.Monitoring.Data;
using Freebuff.Platform.Api.Monitoring.Controllers;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Freebuff.Platform.Tests;

/// <summary>
/// Monitoring controller unit tests. Uses a single ServiceCollection provider.
/// Tests alert CRUD, fuel records, and tenant isolation.
/// </summary>
public class MonitoringServiceIntegrationTests
{
    private static (ServiceProvider provider, IServiceScope scope, MonitoringDbContext db) CreateScope()
    {
        var services = new ServiceCollection();
        services.AddDbContext<MonitoringDbContext>(options =>
            options.UseInMemoryDatabase("MonitoringTest_" + Guid.NewGuid()));
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MonitoringDbContext>();
        return (provider, scope, db);
    }

    private static AlertsController CreateAlertsController(MonitoringDbContext db, Guid companyId)
    {
        var controller = new AlertsController(db);
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

    private static FuelController CreateFuelController(MonitoringDbContext db, Guid companyId)
    {
        var controller = new FuelController(db);
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
    public async Task AlertCRUD_FullFlow()
    {
        var companyId = Guid.NewGuid();
        var (provider, scope, db) = CreateScope();
        try
        {
            var controller = CreateAlertsController(db, companyId);

            // 1. List (empty)
            var listResult = await controller.GetAll(new PagedRequest());
            var listOk = Assert.IsType<OkObjectResult>(listResult.Result);
            var listData = Assert.IsType<ApiResponse<PagedResult<Alert>>>(listOk.Value);
            Assert.Empty(listData.Data!.Items);

            // 2. Create alert
            var createResult = await controller.Create(new CreateAlertDto
            {
                AlertType = "speed_violation", Severity = AlertSeverity.High,
                Title = "Speed Violation", Message = "Vehicle exceeded 120 km/h"
            });
            var created = Assert.IsType<CreatedAtActionResult>(createResult.Result);
            var createData = Assert.IsType<ApiResponse<Alert>>(created.Value);
            Assert.Equal("Speed Violation", createData.Data!.Title);

            // 3. List (should have 1)
            var list2Result = await controller.GetAll(new PagedRequest());
            var list2Ok = Assert.IsType<OkObjectResult>(list2Result.Result);
            var list2Data = Assert.IsType<ApiResponse<PagedResult<Alert>>>(list2Ok.Value);
            Assert.Single(list2Data.Data!.Items);
            Assert.Equal("Speed Violation", list2Data.Data.Items[0].Title);

            // 4. Get by ID
            var alertId = list2Data.Data.Items[0].Id;
            var getResult = await controller.GetById(alertId);
            var getOk = Assert.IsType<OkObjectResult>(getResult.Result);
            var getData = Assert.IsType<ApiResponse<Alert>>(getOk.Value);
            Assert.Equal("Speed Violation", getData.Data!.Title);
        }
        finally
        {
            scope.Dispose();
            provider.Dispose();
        }
    }

    [Fact]
    public async Task FuelRecords_CRUD_Flow()
    {
        var companyId = Guid.NewGuid();
        var (provider, scope, db) = CreateScope();
        try
        {
            var controller = CreateFuelController(db, companyId);

            // 1. Create fuel record
            var createResult = await controller.Create(new CreateFuelRecordDto
            {
                VehicleId = Guid.NewGuid(), FuelType = FuelType.Diesel,
                Quantity = 50m, PricePerUnit = 96.5m, TotalCost = 4825m,
                IsRefueling = true, RecordDate = DateTime.UtcNow
            });
            var created = Assert.IsType<CreatedAtActionResult>(createResult.Result);
            var createData = Assert.IsType<ApiResponse<FuelRecord>>(created.Value);
            Assert.Equal(50m, createData.Data!.Quantity);

            // 2. List (should have 1)
            var listResult = await controller.GetAll(new PagedRequest(), null);
            var listOk = Assert.IsType<OkObjectResult>(listResult.Result);
            var listData = Assert.IsType<ApiResponse<PagedResult<FuelRecord>>>(listOk.Value);
            Assert.Single(listData.Data!.Items);
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
            // Create alert as Company A
            var controllerA = CreateAlertsController(db, companyA);
            await controllerA.Create(new CreateAlertDto
            {
                AlertType = "a1", Severity = AlertSeverity.Info,
                Title = "A Alert", Message = "Company A alert"
            });

            // Create alert as Company B
            var controllerB = CreateAlertsController(db, companyB);
            await controllerB.Create(new CreateAlertDto
            {
                AlertType = "b1", Severity = AlertSeverity.Info,
                Title = "B Alert", Message = "Company B alert"
            });

            // List as Company A → should only see Company A's alert
            var listResult = await controllerA.GetAll(new PagedRequest());
            var listOk = Assert.IsType<OkObjectResult>(listResult.Result);
            var listData = Assert.IsType<ApiResponse<PagedResult<Alert>>>(listOk.Value);
            Assert.Single(listData.Data!.Items);
            Assert.Equal("A Alert", listData.Data.Items[0].Title);

            // List as Company B → should only see Company B's alert
            var listBResult = await controllerB.GetAll(new PagedRequest());
            var listBOk = Assert.IsType<OkObjectResult>(listBResult.Result);
            var listBData = Assert.IsType<ApiResponse<PagedResult<Alert>>>(listBOk.Value);
            Assert.Single(listBData.Data!.Items);
            Assert.Equal("B Alert", listBData.Data.Items[0].Title);
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
            var controller = CreateAlertsController(db, Guid.NewGuid());
            var result = await controller.GetById(Guid.NewGuid());
            Assert.IsType<NotFoundObjectResult>(result.Result);
        }
        finally
        {
            scope.Dispose();
            provider.Dispose();
        }
    }
}
