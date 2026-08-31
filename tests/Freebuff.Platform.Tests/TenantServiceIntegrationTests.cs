using System.Security.Claims;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Shared.Models;
using Freebuff.Platform.Api.Tenant.Data;
using Freebuff.Platform.Api.Tenant.Controllers;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Freebuff.Platform.Tests;

/// <summary>
/// Tenant controller unit tests. Uses a single ServiceCollection provider.
/// Tests company CRUD, module listing.
/// </summary>
public class TenantServiceIntegrationTests
{
    private static (ServiceProvider provider, IServiceScope scope, TenantDbContext db) CreateScope()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TenantDbContext>(options =>
            options.UseInMemoryDatabase("TenantTest_" + Guid.NewGuid()));
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        return (provider, scope, db);
    }

    private static CompaniesController CreateCompaniesController(TenantDbContext db)
    {
        var controller = new CompaniesController(db);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new("tenant_id", Guid.NewGuid().ToString()),
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

    private static ModulesController CreateModulesController(TenantDbContext db)
    {
        var controller = new ModulesController(db);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
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
    public async Task CompanyCRUD_FullFlow()
    {
        var (provider, scope, db) = CreateScope();
        try
        {
            var controller = CreateCompaniesController(db);

            // 1. List (empty)
            var listResult = await controller.GetAll(new PagedRequest());
            var listOk = Assert.IsType<OkObjectResult>(listResult.Result);
            var listData = Assert.IsType<ApiResponse<PagedResult<CompanyDto>>>(listOk.Value);
            Assert.Empty(listData.Data!.Items);

            // 2. Create
            var createResult = await controller.Create(new CreateCompanyDto
            {
                Name = "Test Fleet Co", ContactEmail = "info@testfleet.com", Country = "India"
            });
            var created = Assert.IsType<CreatedAtActionResult>(createResult.Result);
            var createData = Assert.IsType<ApiResponse<CompanyDto>>(created.Value);
            Assert.Equal("Test Fleet Co", createData.Data!.Name);
            var companyId = createData.Data.Id;

            // 3. Get by ID
            var getResult = await controller.GetById(companyId);
            var getOk = Assert.IsType<OkObjectResult>(getResult.Result);
            var getData = Assert.IsType<ApiResponse<CompanyDto>>(getOk.Value);
            Assert.Equal("Test Fleet Co", getData.Data!.Name);

            // 4. Update
            var updateResult = await controller.Update(companyId, new UpdateCompanyDto { Name = "Updated Fleet Co" });
            var updateOk = Assert.IsType<OkObjectResult>(updateResult.Result);
            var updateData = Assert.IsType<ApiResponse<CompanyDto>>(updateOk.Value);
            Assert.Equal("Updated Fleet Co", updateData.Data!.Name);

            // 5. Delete
            var deleteResult = await controller.Delete(companyId);
            Assert.IsType<OkObjectResult>(deleteResult.Result);

            // 6. Get deleted → 404
            var getDeletedResult = await controller.GetById(companyId);
            Assert.IsType<NotFoundObjectResult>(getDeletedResult.Result);
        }
        finally
        {
            scope.Dispose();
            provider.Dispose();
        }
    }

    [Fact]
    public async Task Modules_ReturnsEmptyList()
    {
        var (provider, scope, db) = CreateScope();
        try
        {
            var controller = CreateModulesController(db);
            var listResult = await controller.GetAll();
            var listOk = Assert.IsType<OkObjectResult>(listResult.Result);
            var listData = Assert.IsType<ApiResponse<List<Module>>>(listOk.Value);
            Assert.Empty(listData.Data!);
        }
        finally
        {
            scope.Dispose();
            provider.Dispose();
        }
    }

    [Fact]
    public async Task Modules_WithData_ReturnsSortedByDisplayOrder()
    {
        var (provider, scope, db) = CreateScope();
        try
        {
            // Seed modules directly (no HTTP needed)
            db.Modules.AddRange(
                new Module { Id = Guid.NewGuid(), Code = "vehicles", Name = "Vehicles", Status = EntityStatus.Active, DisplayOrder = 2 },
                new Module { Id = Guid.NewGuid(), Code = "fleet", Name = "Fleet Management", Status = EntityStatus.Active, DisplayOrder = 1 }
            );
            await db.SaveChangesAsync();

            var controller = CreateModulesController(db);
            var listResult = await controller.GetAll();
            var listOk = Assert.IsType<OkObjectResult>(listResult.Result);
            var listData = Assert.IsType<ApiResponse<List<Module>>>(listOk.Value);
            Assert.Equal(2, listData.Data!.Count);
            Assert.Equal("Fleet Management", listData.Data[0].Name);
            Assert.Equal("Vehicles", listData.Data[1].Name);
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
            var controller = CreateCompaniesController(db);
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
