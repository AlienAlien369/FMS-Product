using System.Security.Claims;
using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Shared.Models;
using Freebuff.Platform.Api.Identity.Data;
using Freebuff.Platform.Api.Identity.Services;
using Freebuff.Platform.Api.Identity.Controllers;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Freebuff.Platform.Tests;

/// <summary>
/// Identity controller unit tests. Uses a single ServiceCollection provider.
/// Tests auth flow, user CRUD, and role operations.
/// </summary>
public class IdentityServiceIntegrationTests
{
    private const string JwtKey = TestHelper.JwtKey;

    private static (ServiceProvider provider, IServiceScope scope, IdentityDbContext db) CreateScope()
    {
        var services = new ServiceCollection();
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseInMemoryDatabase("IdentityTest_" + Guid.NewGuid()));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = JwtKey, ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test"
            }).Build());
        services.AddScoped<IdentityAuthService>();
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return (provider, scope, db);
    }

    private static AuthController CreateAuthController(IdentityDbContext db)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = JwtKey, ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test"
            }).Build();
        var authService = new IdentityAuthService(db, config);
        return new AuthController(authService);
    }

    private static UsersController CreateUsersController(IdentityDbContext db, Guid companyId)
    {
        var controller = new UsersController(db);
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

    private static RolesController CreateRolesController(IdentityDbContext db, Guid companyId)
    {
        var controller = new RolesController(db);
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
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorized()
    {
        var (provider, scope, db) = CreateScope();
        try
        {
            var authController = CreateAuthController(db);
            var result = await authController.Login(new LoginDto("no@test.com", "bad"));
            Assert.IsType<UnauthorizedObjectResult>(result.Result);
        }
        finally
        {
            scope.Dispose();
            provider.Dispose();
        }
    }

    [Fact]
    public async Task Login_ThenCRUD_FullFlow()
    {
        var companyId = Guid.NewGuid();
        var (provider, scope, db) = CreateScope();
        try
        {
            var usersController = CreateUsersController(db, companyId);
            var authController = CreateAuthController(db);

            // 1. Create user via controller
            var createResult = await usersController.Create(new CreateUserDto
            {
                Email = "crud@test.com", Password = "Crud@123",
                FirstName = "Crud", LastName = "User", CompanyId = companyId
            });
            var created = Assert.IsType<CreatedAtActionResult>(createResult.Result);
            var createData = Assert.IsType<ApiResponse<UserDto>>(created.Value);
            Assert.Equal("crud@test.com", createData.Data!.Email);

            // 2. Login
            var loginResult = await authController.Login(new LoginDto("crud@test.com", "Crud@123"));
            var loginOk = Assert.IsType<OkObjectResult>(loginResult.Result);
            var loginData = Assert.IsType<ApiResponse<AuthResponseDto>>(loginOk.Value);
            Assert.NotNull(loginData.Data!.Token);

            // 3. List users (should have 1)
            var listResult = await usersController.GetAll(new PagedRequest());
            var listOk = Assert.IsType<OkObjectResult>(listResult.Result);
            var listData = Assert.IsType<ApiResponse<PagedResult<UserDto>>>(listOk.Value);
            Assert.Single(listData.Data!.Items);

            // 4. Create another user
            var companyId2 = Guid.NewGuid();
            var create2Result = await usersController.Create(new CreateUserDto
            {
                Email = "new@test.com", Password = "New@1234", FirstName = "New",
                LastName = "User", CompanyId = companyId2
            });
            Assert.IsType<CreatedAtActionResult>(create2Result.Result);

            // 5. List users (should have 2)
            var list2Result = await usersController.GetAll(new PagedRequest());
            var list2Ok = Assert.IsType<OkObjectResult>(list2Result.Result);
            var list2Data = Assert.IsType<ApiResponse<PagedResult<UserDto>>>(list2Ok.Value);
            Assert.Equal(2, list2Data.Data!.Items.Count);

            // 6. Delete new user
            var newUserId = list2Data.Data.Items.First(u => u.Email == "new@test.com").Id;
            var deleteResult = await usersController.Delete(newUserId);
            Assert.IsType<OkObjectResult>(deleteResult.Result);

            // 7. Get deleted → 404
            var getResult = await usersController.GetById(newUserId);
            Assert.IsType<NotFoundObjectResult>(getResult.Result);

            // 8. List users (back to 1)
            var list3Result = await usersController.GetAll(new PagedRequest());
            var list3Ok = Assert.IsType<OkObjectResult>(list3Result.Result);
            var list3Data = Assert.IsType<ApiResponse<PagedResult<UserDto>>>(list3Ok.Value);
            Assert.Single(list3Data.Data!.Items);
        }
        finally
        {
            scope.Dispose();
            provider.Dispose();
        }
    }

    [Fact]
    public async Task Roles_ListReturnsEmpty()
    {
        var companyId = Guid.NewGuid();
        var (provider, scope, db) = CreateScope();
        try
        {
            var rolesController = CreateRolesController(db, companyId);
            var listResult = await rolesController.GetAll(new PagedRequest());
            var listOk = Assert.IsType<OkObjectResult>(listResult.Result);
            var listData = Assert.IsType<ApiResponse<PagedResult<RoleDto>>>(listOk.Value);
            Assert.Empty(listData.Data!.Items);
        }
        finally
        {
            scope.Dispose();
            provider.Dispose();
        }
    }

    [Fact]
    public async Task Roles_CreateRole()
    {
        var companyId = Guid.NewGuid();
        var (provider, scope, db) = CreateScope();
        try
        {
            var rolesController = CreateRolesController(db, companyId);
            var createResult = await rolesController.Create(new CreateRoleDto
            {
                Name = "Fleet Manager", Description = "Manages fleet", CompanyId = companyId
            });
            var created = Assert.IsType<CreatedAtActionResult>(createResult.Result);
            var createData = Assert.IsType<ApiResponse<RoleDto>>(created.Value);
            Assert.Equal("Fleet Manager", createData.Data!.Name);

            // List should have 1
            var listResult = await rolesController.GetAll(new PagedRequest());
            var listOk = Assert.IsType<OkObjectResult>(listResult.Result);
            var listData = Assert.IsType<ApiResponse<PagedResult<RoleDto>>>(listOk.Value);
            Assert.Single(listData.Data!.Items);
        }
        finally
        {
            scope.Dispose();
            provider.Dispose();
        }
    }
}
