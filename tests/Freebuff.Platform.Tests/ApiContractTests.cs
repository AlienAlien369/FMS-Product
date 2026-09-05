using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Domain.Common;
using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Shared.Models;
using Xunit;

namespace Freebuff.Platform.Tests;

public class BaseEntityTests
{
    [Fact]
    public void BaseEntity_DefaultValues_AreCorrect()
    {
        var entity = new TestEntity();
        // Id defaults to Guid.Empty - EF Core / application layer assigns it
        Assert.Equal(Guid.Empty, entity.Id);
        Assert.False(entity.IsDeleted);
        Assert.Null(entity.DeletedAt);
        Assert.Null(entity.DeletedBy);
        Assert.Equal(0, entity.Version);
        Assert.NotNull(entity.DomainEvents);
        Assert.Empty(entity.DomainEvents);
    }

    [Fact]
    public void BaseEntity_DomainEvents_CanBeAddedAndCleared()
    {
        var entity = new TestEntity();
        var evt = new TestDomainEvent();
        entity.AddDomainEvent(evt);
        Assert.Single(entity.DomainEvents);
        entity.ClearDomainEvents();
        Assert.Empty(entity.DomainEvents);
    }

    private class TestEntity : BaseEntity { }
    private class TestDomainEvent : IDomainEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime OccurredOn { get; } = DateTime.UtcNow;
        public string EventType => "Test";
    }
}

public class PagedResultTests
{
    [Fact]
    public void PagedResult_CalculatesTotalPages_Correctly()
    {
        var result = new PagedResult<string> { Items = new(), TotalCount = 45, Page = 1, PageSize = 10 };
        Assert.Equal(5, result.TotalPages);
        Assert.True(result.HasNext);
        Assert.False(result.HasPrevious);
    }

    [Fact]
    public void PagedResult_EmptyResults_HasNoPages()
    {
        var result = new PagedResult<string> { Items = new(), TotalCount = 0, Page = 1, PageSize = 10 };
        Assert.Equal(0, result.TotalPages);
        Assert.False(result.HasNext);
        Assert.False(result.HasPrevious);
    }

    [Fact]
    public void PagedResult_LastPage_HasNoNext()
    {
        var result = new PagedResult<string> { Items = new(), TotalCount = 25, Page = 3, PageSize = 10 };
        Assert.Equal(3, result.TotalPages);
        Assert.False(result.HasNext);
        Assert.True(result.HasPrevious);
    }
}

public class ApiResponseTests
{
    [Fact]
    public void ApiResponse_Ok_ReturnsSuccessWithData()
    {
        var response = ApiResponse<string>.Ok("hello");
        Assert.True(response.Success);
        Assert.Equal("hello", response.Data);
        Assert.Null(response.Code);
    }

    [Fact]
    public void ApiResponse_Fail_ReturnsErrorWithCode()
    {
        var response = ApiResponse.Fail("NOT_FOUND", "Not found");
        Assert.False(response.Success);
        Assert.Equal("NOT_FOUND", response.Code);
        Assert.Equal("Not found", response.Message);
    }
}

public class DtoTests
{
    [Fact]
    public void LoginDto_IsRecord()
    {
        var dto1 = new LoginDto("test@test.com", "pass123");
        var dto2 = new LoginDto("test@test.com", "pass123");
        Assert.Equal(dto1, dto2); // Records use value equality
    }

    [Fact]
    public void UserDto_FullName_CanBeConstructed()
    {
        var user = new UserDto
        {
            Id = Guid.NewGuid(),
            Email = "test@test.com",
            FirstName = "John",
            LastName = "Doe",
            CompanyId = Guid.NewGuid(),
            Roles = new List<string> { "Admin" }
        };
        Assert.Equal("John", user.FirstName);
        Assert.Equal("Doe", user.LastName);
        Assert.Single(user.Roles);
    }

    [Fact]
    public void VehicleDto_FuelType_MapsCorrectly()
    {
        var dto = new VehicleDto { FuelType = (int)FuelType.Electric };
        Assert.Equal(4, dto.FuelType);
    }
}

public class EntityRelationshipTests
{
    [Fact]
    public void Company_HasExpectedNavProperties()
    {
        var company = new Company();
        Assert.NotNull(company.Users);
        Assert.NotNull(company.Vehicles);
        Assert.NotNull(company.Drivers);
        Assert.NotNull(company.Clients);
        Assert.NotNull(company.Geofences);
        Assert.NotNull(company.Trips);
        Assert.NotNull(company.Roles);
        Assert.NotNull(company.Configurations);
    }

    [Fact]
    public void Vehicle_DefaultFuelType_IsDiesel()
    {
        var vehicle = new Vehicle();
        Assert.Equal(FuelType.Diesel, vehicle.FuelType);
    }

    [Fact]
    public void User_FullName_CombinesFirstAndLastName()
    {
        var user = new User { FirstName = "John", LastName = "Doe" };
        Assert.Equal("John Doe", user.FullName);
    }

    [Fact]
    public void User_FullName_Trimmed()
    {
        var user = new User { FirstName = "John", LastName = "" };
        Assert.Equal("John", user.FullName);
    }

    [Fact]
    public void Driver_FullName_CombinesFirstAndLastName()
    {
        var driver = new Driver { FirstName = "Jane", LastName = "Smith" };
        Assert.Equal("Jane Smith", driver.FullName);
    }
}

public class SoftDeleteTests
{
    [Fact]
    public void SoftDelete_SetFields_Correctly()
    {
        var entity = new TestSoftDeleteEntity();
        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = "admin";

        Assert.True(entity.IsDeleted);
        Assert.NotNull(entity.DeletedAt);
        Assert.Equal("admin", entity.DeletedBy);
    }

    private class TestSoftDeleteEntity : BaseEntity { }
}

public class EnumTests
{
    [Theory]
    [InlineData(EntityStatus.Active, 0)]
    [InlineData(EntityStatus.Inactive, 1)]
    [InlineData(VehicleStatus.Active, 0)]
    [InlineData(VehicleStatus.InMaintenance, 2)]
    [InlineData(DriverStatus.Active, 0)]
    [InlineData(DriverStatus.OnTrip, 2)]
    [InlineData(TripStatus.Draft, 0)]
    [InlineData(TripStatus.Completed, 3)]
    [InlineData(FuelType.Diesel, 1)]
    [InlineData(FuelType.Electric, 4)]
    [InlineData(AlertSeverity.Critical, 4)]
    [InlineData(ConfigurationScope.User, 5)]
    [InlineData(PermissionAction.Manage, 10)]
    public void Enum_Values_MatchExpected(object enumValue, int expectedInt)
    {
        Assert.Equal(expectedInt, Convert.ToInt32(enumValue));
    }
}

public class SubscriptionPricingTests
{
    [Fact]
    public void Subscription_EffectivePrice_CalculatesCorrectly()
    {
        var sub = new Subscription
        {
            CurrentPrice = 100,
            DiscountPercentage = 10,
            TaxPercentage = 18
        };
        // 100 * (1 - 10/100) * (1 + 18/100) = 100 * 0.9 * 1.18 = 106.2
        Assert.Equal(106.2m, sub.EffectivePrice);
    }

    [Fact]
    public void Subscription_EffectivePrice_NoDiscountNoTax()
    {
        var sub = new Subscription { CurrentPrice = 100 };
        Assert.Equal(100m, sub.EffectivePrice);
    }

    [Fact]
    public void Package_UnlimitedLimits_UseNegativeOne()
    {
        var pkg = new Package { MaxUsers = -1, MaxVehicles = -1 };
        Assert.Equal(-1, pkg.MaxUsers);
        Assert.Equal(-1, pkg.MaxVehicles);
    }
}
