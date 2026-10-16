using ImageStore.Api.Chat;
using ImageStore.Api.Notifications;
using ImageStore.Application.Notifications;
using ImageStore.Domain.Notifications;
using ImageStore.Infrastructure.Notifications;
using ImageStore.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
namespace ImageStore.IntegrationTests;

public sealed class NotificationPersistenceTests
{
    [Fact]
    public void Model_Migration_And_Recipient_Indexes_Are_In_Sync()
    {
        using var db = new ImageStoreDbContext(new DbContextOptionsBuilder<ImageStoreDbContext>()
            .UseSqlServer("Server=localhost;Database=DesignOnly;Integrated Security=true").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Contains("20261016180000_AddNotifications", db.Database.GetMigrations());
        var model = db.Model.FindEntityType(typeof(Notification))!;
        Assert.True(model.GetIndexes().Single(x => x.Properties.Select(p => p.Name).SequenceEqual(["RecipientUserId", "EventKey"])).IsUnique);
        Assert.Contains(model.GetIndexes(), x => x.Properties.Select(p => p.Name).SequenceEqual(["RecipientUserId", "ReadAtUtc", "CreatedAtUtc"]));
    }
    [Fact]
    public void Creation_Policy_Suppresses_Self_And_Uses_Stable_Event_Identity()
    {
        var service = new NotificationCreationService();
        var user = Guid.NewGuid(); var actor = Guid.NewGuid();
        var e = new NotificationEvent("event", user, actor, NotificationType.PostComment, DateTime.UtcNow);
        Assert.Equal(service.Create(e)!.Id, service.Create(e with { CreatedAtUtc = DateTime.UtcNow.AddMinutes(1) })!.Id);
        Assert.NotEqual(service.Create(e)!.Id, service.Create(e with { Recipient = Guid.NewGuid() })!.Id);
        Assert.Null(service.Create(e with { Actor = user }));
    }
    [Fact]
    public async Task Retention_Redacts_Read_Content_Without_Removing_Unread_Items_Or_Dedup_Keys()
    {
        using var db = new ImageStoreDbContext(new DbContextOptionsBuilder<ImageStoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new NotificationCreationService(); var recipient = Guid.NewGuid();
        var read = service.Create(new("old-read", recipient, null, NotificationType.SystemAnnouncement, DateTime.UtcNow.AddDays(-100), Announcement: "Read notice"))!;
        var unread = service.Create(new("old-unread", recipient, null, NotificationType.SystemAnnouncement, DateTime.UtcNow.AddDays(-100), Announcement: "Unread notice"))!;
        read.ReadAtUtc = DateTime.UtcNow.AddDays(-99); db.AddRange(read, unread); await db.SaveChangesAsync();
        var dispatcher = new NotificationDispatcher(db, new RecordingDelivery(), TimeProvider.System, NullLogger<NotificationDispatcher>.Instance);
        await dispatcher.RetainAsync(CancellationToken.None);
        Assert.NotNull(read.DismissedAtUtc); Assert.Null(read.Announcement); Assert.Equal("old-read", read.EventKey);
        Assert.Null(unread.DismissedAtUtc); Assert.Equal("Unread notice", unread.Announcement);
        Assert.Equal(2, await db.Set<Notification>().CountAsync());
    }
    [Fact]
    public async Task SignalR_Transport_Targets_Only_Server_Selected_User_And_Sends_Only_The_Id()
    {
        var clients = new RecordingClients();
        var delivery = new SignalRNotificationDelivery(new RecordingHubContext(clients));
        var user = Guid.NewGuid(); var id = Guid.NewGuid();
        await delivery.ChangedAsync(user, id, CancellationToken.None);
        Assert.Equal(user.ToString(), clients.Target);
        Assert.Equal("NotificationChanged", clients.Proxy.Method);
        Assert.Equal(id, Assert.IsType<Guid>(Assert.Single(clients.Proxy.Arguments)));
    }
    private sealed class RecordingDelivery : INotificationDelivery
    {
        public Task ChangedAsync(Guid recipientUserId, Guid? notificationId, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class RecordingHubContext(RecordingClients clients) : IHubContext<ChatHub>
    {
        public IHubClients Clients => clients;
        public IGroupManager Groups => throw new NotSupportedException();
    }
    private sealed class RecordingProxy : IClientProxy
    {
        public string? Method { get; private set; }
        public object?[] Arguments { get; private set; } = [];
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        { Method = method; Arguments = args; return Task.CompletedTask; }
    }
    private sealed class RecordingClients : IHubClients
    {
        public string? Target { get; private set; }
        public RecordingProxy Proxy { get; } = new();
        public IClientProxy User(string userId) { Target = userId; return Proxy; }
        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        IClientProxy IHubClients<IClientProxy>.Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }
}
