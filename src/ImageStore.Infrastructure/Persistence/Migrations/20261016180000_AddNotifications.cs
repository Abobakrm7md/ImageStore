using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace ImageStore.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ImageStoreDbContext))]
[Migration("20261016180000_AddNotifications")]
public sealed partial class AddNotifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("ParentCommentId", "Comments", type: "uniqueidentifier", nullable: true);
        migrationBuilder.CreateTable(name: "Notifications", columns: table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
            Type = table.Column<int>(type: "int", nullable: false),
            EventKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
            PostId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
            CommentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
            ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
            MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
            Announcement = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: true),
            Critical = table.Column<bool>(type: "bit", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
            DismissedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
            DispatchedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
            NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            DeliveryAttempts = table.Column<int>(type: "int", nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_Notifications", x => x.Id);
            table.ForeignKey("FK_Notifications_AspNetUsers_RecipientUserId", x => x.RecipientUserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_Notifications_AspNetUsers_ActorUserId", x => x.ActorUserId, "AspNetUsers", "Id");
        });
        migrationBuilder.CreateTable(name: "NotificationPreferences", columns: table => new
        {
            UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            InAppEnabled = table.Column<bool>(type: "bit", nullable: false),
            ChatEnabled = table.Column<bool>(type: "bit", nullable: false),
            FollowEnabled = table.Column<bool>(type: "bit", nullable: false),
            CommentsEnabled = table.Column<bool>(type: "bit", nullable: false),
            ReactionsEnabled = table.Column<bool>(type: "bit", nullable: false),
            MentionsEnabled = table.Column<bool>(type: "bit", nullable: false),
            AnnouncementsEnabled = table.Column<bool>(type: "bit", nullable: false),
            SoundEnabled = table.Column<bool>(type: "bit", nullable: false),
            SuppressActiveConversation = table.Column<bool>(type: "bit", nullable: false),
            SoundVolume = table.Column<int>(type: "int", nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_NotificationPreferences", x => x.UserId);
            table.ForeignKey("FK_NotificationPreferences_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
            table.CheckConstraint("CK_NotificationPreferences_Volume", "[SoundVolume] >= 0 AND [SoundVolume] <= 100");
        });
        migrationBuilder.CreateIndex("IX_Notifications_RecipientUserId_EventKey", "Notifications", new[] { "RecipientUserId", "EventKey" }, unique: true);
        migrationBuilder.CreateIndex("IX_Notifications_RecipientUserId_CreatedAtUtc_Id", "Notifications", new[] { "RecipientUserId", "CreatedAtUtc", "Id" });
        migrationBuilder.CreateIndex("IX_Notifications_RecipientUserId_ReadAtUtc_CreatedAtUtc", "Notifications", new[] { "RecipientUserId", "ReadAtUtc", "CreatedAtUtc" }, filter: "[DismissedAtUtc] IS NULL");
        migrationBuilder.CreateIndex("IX_Notifications_DispatchedAtUtc_NextAttemptAtUtc", "Notifications", new[] { "DispatchedAtUtc", "NextAttemptAtUtc" }, filter: "[DispatchedAtUtc] IS NULL");
        migrationBuilder.CreateIndex("IX_Notifications_ActorUserId", "Notifications", new[] { "ActorUserId" });
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("Notifications");
        migrationBuilder.DropTable("NotificationPreferences");
        migrationBuilder.DropColumn("ParentCommentId", "Comments");
    }
}
