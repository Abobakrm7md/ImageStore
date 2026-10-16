using ImageStore.Api.Chat;
using ImageStore.Application.Notifications;
using Microsoft.AspNetCore.SignalR;
namespace ImageStore.Api.Notifications;

public sealed class SignalRNotificationDelivery(IHubContext<ChatHub> hub) : INotificationDelivery
{
    public Task ChangedAsync(Guid recipientUserId, Guid? notificationId, CancellationToken ct) =>
        hub.Clients.User(recipientUserId.ToString()).SendAsync("NotificationChanged", notificationId, ct);
}
