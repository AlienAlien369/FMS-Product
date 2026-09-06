using Freebuff.Platform.Domain.Entities;
using Freebuff.Platform.Domain.Enums;
using Freebuff.Platform.Infrastructure.Data;
using Freebuff.Platform.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Freebuff.Platform.Tests.Notifications;

/// <summary>
/// Verifies NotificationService emits realtime channel pushes: one per delivered
/// recipient, never for muted users, and per-user from the direct NotifyUserAsync
/// path. The push happens only after the row is committed (save-before-push
/// ordering), so failed saves can't emit phantom events.
/// </summary>
public class NotificationRealtimeChannelTests
{
    private sealed class RecordingChannel : INotificationRealtimeChannel
    {
        public List<(Guid UserId, string EventType, string Title, string Message, int Severity)> Pushes { get; } = new();
        public Task PushToUserAsync(Guid userId, string eventType, string title, string message, int severity)
        {
            Pushes.Add((userId, eventType, title, message, severity));
            return Task.CompletedTask;
        }
    }

    private static ApplicationDbContext CreateDb(string name)
    {
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"NotifChannel_{name}_{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(opts);
    }

    private static async Task<(ApplicationDbContext Db, Guid CompanyId, Guid AdminA, Guid AdminB)> SeedCompanyWithAdminsAsync(string dbName)
    {
        var db = CreateDb(dbName);
        var companyId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var adminA = Guid.NewGuid();
        var adminB = Guid.NewGuid();

        db.Companies.Add(new Company { Id = companyId, Name = "Test Co", Status = EntityStatus.Active });
        db.Roles.Add(new Role { Id = roleId, CompanyId = companyId, Name = "Company Admin", IsSystemRole = true, Status = EntityStatus.Active });
        db.Users.AddRange(
            new User { Id = adminA, CompanyId = companyId, Email = "a@test.com", Status = EntityStatus.Active },
            new User { Id = adminB, CompanyId = companyId, Email = "b@test.com", Status = EntityStatus.Active });
        db.UserRoles.AddRange(
            new UserRole { Id = Guid.NewGuid(), UserId = adminA, RoleId = roleId },
            new UserRole { Id = Guid.NewGuid(), UserId = adminB, RoleId = roleId });
        await db.SaveChangesAsync();
        return (db, companyId, adminA, adminB);
    }

    [Fact]
    public async Task CompanyAdminDispatch_PushesToEachDeliveredRecipient()
    {
        var (db, companyId, adminA, adminB) = await SeedCompanyWithAdminsAsync("dispatch");
        using var _ = db;
        var channel = new RecordingChannel();
        var svc = new NotificationService(db, new PermissionService(db), channel);

        var delivered = await svc.NotifyCompanyAdminsAsync(companyId, "alert.fired", "Title", "Message", 3);

        Assert.Equal(2, delivered);
        Assert.Equal(2, channel.Pushes.Count);
        Assert.Contains(channel.Pushes, p => p.UserId == adminA && p.EventType == "alert.fired" && p.Severity == 3);
        Assert.Contains(channel.Pushes, p => p.UserId == adminB && p.Title == "Title" && p.Message == "Message");
    }

    [Fact]
    public async Task MutedUser_ReceivesRowButNoPush()
    {
        using var db = CreateDb("muted");
        var companyId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var adminA = Guid.NewGuid();
        var adminB = Guid.NewGuid();
        db.Companies.Add(new Company { Id = companyId, Name = "Test Co", Status = EntityStatus.Active });
        db.Roles.Add(new Role { Id = roleId, CompanyId = companyId, Name = "Company Admin", IsSystemRole = true, Status = EntityStatus.Active });
        db.Users.AddRange(
            new User { Id = adminA, CompanyId = companyId, Email = "a@test.com", Status = EntityStatus.Active },
            new User { Id = adminB, CompanyId = companyId, Email = "b@test.com", Status = EntityStatus.Active });
        db.UserRoles.AddRange(
            new UserRole { Id = Guid.NewGuid(), UserId = adminA, RoleId = roleId },
            new UserRole { Id = Guid.NewGuid(), UserId = adminB, RoleId = roleId });
        // adminB mutes alert.fired — row is stored but no push may be sent to them.
        db.NotificationPreferences.Add(new NotificationPreference
        {
            Id = Guid.NewGuid(), UserId = adminB, CompanyId = companyId,
            EventType = "alert.fired", Enabled = false
        });
        await db.SaveChangesAsync();

        var channel = new RecordingChannel();
        var svc = new NotificationService(db, new PermissionService(db), channel);

        var delivered = await svc.NotifyCompanyAdminsAsync(companyId, "alert.fired", "Title", "Message", 2);

        Assert.Equal(1, delivered);
        var push = Assert.Single(channel.Pushes);
        Assert.Equal(adminA, push.UserId);
    }

    [Fact]
    public async Task DirectUserNotification_PushesToThatUser()
    {
        using var db = CreateDb("direct");
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        db.Users.Add(new User { Id = userId, CompanyId = companyId, Email = "u@test.com", Status = EntityStatus.Active });
        await db.SaveChangesAsync();

        var channel = new RecordingChannel();
        var svc = new NotificationService(db, new PermissionService(db), channel);

        await svc.NotifyUserAsync(companyId, userId, "company.config_changed", "Config", "Changed", 1);

        var push = Assert.Single(channel.Pushes);
        Assert.Equal(userId, push.UserId);
        Assert.Equal("company.config_changed", push.EventType);
        Assert.Equal(1, push.Severity);
    }
}