using System.Net.Http.Json;
using ImageStore.Application.Chat;
using ImageStore.Domain.Chat;
using ImageStore.Domain.Notifications;
using ImageStore.Infrastructure.Persistence;
using ImageStore.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace ImageStore.IntegrationTests;

// A real SQL Server is necessary: EF InMemory cannot prove unique-index/transaction behavior.
public sealed class SqlChatFactAttribute : FactAttribute
{
    public SqlChatFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IMAGESTORE_TEST_SQL")!))
            Skip = "Set IMAGESTORE_TEST_SQL to a SQL Server connection with CREATE DATABASE permission.";
    }
}
public sealed class ChatSqlServerTests
{
    private sealed class SqlFactory : ImageStoreApiFactory
    {
        private readonly string connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("IMAGESTORE_TEST_SQL")!)
        { InitialCatalog = "ImageStoreChatTest_" + Guid.NewGuid().ToString("N") }.ConnectionString;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "SqlServer", ["Database:ApplyMigrationsOnStartup"] = "true", ["ConnectionStrings:ImageStore"] = connection,
            }));
        }
    }
    [SqlChatFact]
    public async Task Concurrent_Pair_Creation_Send_Retries_And_Duplicate_Participants()
    {
        using var factory = new SqlFactory();
        try
        {
            using var alice = await ApiClient.CreateAuthenticatedAsync(factory);
            using var bob = await ApiClient.CreateAuthenticatedAsync(factory);
            var conversations = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => i % 2 == 0 ? ChatTests.Open(alice, bob.User!.Id) : ChatTests.Open(bob, alice.User!.Id)));
            var id = Assert.Single(conversations.Select(c => c.Id).Distinct());
            var request = new SendMessageRequest(Guid.NewGuid(), "exactly once");
            var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => alice.Http.PostAsJsonAsync($"/api/conversations/{id}/messages", request)));
            Assert.All(responses, response => response.EnsureSuccessStatusCode());
            Assert.Equal(1, await factory.WithDbContextAsync(db => db.Set<ChatMessage>().CountAsync(m => m.ConversationId == id)));
            Assert.Equal(1, await factory.WithDbContextAsync(db => db.Set<Notification>().CountAsync(n => n.ConversationId == id && n.RecipientUserId == bob.User!.Id)));
            var sends = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => alice.Http.PostAsJsonAsync($"/api/conversations/{id}/messages", new SendMessageRequest(Guid.NewGuid(), $"parallel {i}"))));
            Assert.All(sends, response => response.EnsureSuccessStatusCode());
            var history = await bob.Http.GetFromJsonAsync<MessageDto[]>($"/api/conversations/{id}/messages");
            Assert.Equal(new long[] { 6, 5, 4, 3, 2, 1 }, history!.Select(m => m.Sequence));
            var reads = await Task.WhenAll(new long[] { 6, 2, 4 }.Select(sequence => bob.Http.PutAsJsonAsync($"/api/conversations/{id}/read", new { sequence })));
            Assert.All(reads, response => response.EnsureSuccessStatusCode());
            var summary = await alice.Http.GetFromJsonAsync<ConversationDto>($"/api/conversations/{id}");
            Assert.Equal(6, summary!.Participants.Single(p => p.Id == bob.User!.Id).LastReadSequence);
            Assert.Equal(0, await factory.WithDbContextAsync(db => db.Set<Notification>().CountAsync(n => n.ConversationId == id && n.ReadAtUtc == null)));
            await Assert.ThrowsAsync<DbUpdateException>(() => factory.WithDbContextAsync(async db =>
            {
                db.Add(new ConversationParticipant { ConversationId = id, UserId = alice.User!.Id, JoinedAtUtc = DateTime.UtcNow });
                return await db.SaveChangesAsync();
            }));
        }
        finally { await factory.WithDbContextAsync(db => db.Database.EnsureDeletedAsync()); }
    }
    [Fact]
    public void Chat_Migration_Model_And_Constraints_Are_Present()
    {
        using var db = new ImageStoreDbContext(new DbContextOptionsBuilder<ImageStoreDbContext>().UseSqlServer("Server=localhost;Database=DesignOnly;Integrated Security=true").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Contains("20261016090000_AddChat", db.Database.GetMigrations());
        var conversation = db.Model.FindEntityType(typeof(Conversation))!;
        Assert.True(conversation.GetIndexes().Single(x => x.Properties[0].Name == "PairKey").IsUnique);
        Assert.True(conversation.FindProperty("LastSequence")!.IsConcurrencyToken);
        Assert.Equal(2, db.Model.FindEntityType(typeof(ConversationParticipant))!.FindPrimaryKey()!.Properties.Count);
    }
}
