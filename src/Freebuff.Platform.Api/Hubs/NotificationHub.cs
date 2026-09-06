using Freebuff.Platform.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Freebuff.Platform.Api.Hubs;

/// <summary>
/// Authenticated SignalR hub for instant notification delivery. Connections are
/// addressed server-side via Clients.User(userId) — the default IUserIdProvider
/// resolves the user id from the JWT's NameIdentifier claim, which the login
/// endpoint issues (http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier).
/// No client-to-server messages are needed; this hub exists so the server can
/// push "notification.new" the moment NotificationService commits a row.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("connected", new { connectedAt = DateTime.UtcNow });
        await base.OnConnectedAsync();
    }
}

/// <summary>
/// IHubContext-based INotificationRealtimeChannel implementation. Broadcasts a
/// "notification.new" event to every connection of the target user, who reacts
/// by re-fetching their unread count + recent list (badge updates instantly).
/// </summary>
public sealed class SignalRNotificationChannel : INotificationRealtimeChannel
{
    private readonly IHubContext<NotificationHub> _hub;

    public SignalRNotificationChannel(IHubContext<NotificationHub> hub) => _hub = hub;

    public async Task PushToUserAsync(Guid userId, string eventType, string title, string message, int severity)
    {
        await _hub.Clients.User(userId.ToString()).SendAsync("notification.new", new
        {
            eventType,
            severity,
            title,
            message,
            createdAt = DateTime.UtcNow
        });
    }
}