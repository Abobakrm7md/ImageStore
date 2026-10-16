using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace ImageStore.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ImageStoreDbContext))]
[Migration("20261016090000_AddChat")]
public sealed partial class AddChat : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE [Conversations] (
                [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_Conversations] PRIMARY KEY,
                [PairKey] nvarchar(65) NOT NULL,
                [CreatedAtUtc] datetime2 NOT NULL,
                [UpdatedAtUtc] datetime2 NOT NULL,
                [LastMessageAtUtc] datetime2 NULL,
                [LastSequence] bigint NOT NULL);
            CREATE UNIQUE INDEX [IX_Conversations_PairKey] ON [Conversations] ([PairKey]);
            CREATE TABLE [ConversationParticipants] (
                [ConversationId] uniqueidentifier NOT NULL,
                [UserId] uniqueidentifier NOT NULL,
                [JoinedAtUtc] datetime2 NOT NULL,
                [LastReadSequence] bigint NOT NULL,
                CONSTRAINT [PK_ConversationParticipants] PRIMARY KEY ([ConversationId], [UserId]),
                CONSTRAINT [FK_ConversationParticipants_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE CASCADE,
                CONSTRAINT [FK_ConversationParticipants_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]));
            CREATE INDEX [IX_ConversationParticipants_UserId] ON [ConversationParticipants] ([UserId]);
            CREATE TABLE [ChatMessages] (
                [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_ChatMessages] PRIMARY KEY,
                [ConversationId] uniqueidentifier NOT NULL,
                [SenderUserId] uniqueidentifier NOT NULL,
                [Sequence] bigint NOT NULL,
                [Content] nvarchar(4000) NOT NULL,
                [CreatedAtUtc] datetime2 NOT NULL,
                [MessageType] nvarchar(8) NOT NULL,
                [SharedPostId] uniqueidentifier NULL,
                CONSTRAINT [CK_ChatMessages_Content] CHECK (LEN([Content]) <= 4000 AND ([MessageType] = 'post' OR ([MessageType] = 'text' AND LEN(LTRIM(RTRIM([Content]))) > 0))),
                CONSTRAINT [FK_ChatMessages_ConversationParticipants_ConversationId_SenderUserId] FOREIGN KEY ([ConversationId], [SenderUserId]) REFERENCES [ConversationParticipants] ([ConversationId], [UserId]),
                CONSTRAINT [FK_ChatMessages_ImagePosts_SharedPostId] FOREIGN KEY ([SharedPostId]) REFERENCES [ImagePosts] ([Id]) ON DELETE SET NULL);
            CREATE UNIQUE INDEX [IX_ChatMessages_ConversationId_Sequence] ON [ChatMessages] ([ConversationId], [Sequence]);
            CREATE INDEX [IX_ChatMessages_ConversationId_SenderUserId] ON [ChatMessages] ([ConversationId], [SenderUserId]);
            CREATE INDEX [IX_ChatMessages_SharedPostId] ON [ChatMessages] ([SharedPostId]);
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP TABLE [ChatMessages]; DROP TABLE [ConversationParticipants]; DROP TABLE [Conversations];");
}
