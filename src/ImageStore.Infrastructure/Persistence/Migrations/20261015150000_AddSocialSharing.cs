using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ImageStore.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSocialSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserProfiles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NormalizedUserName = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Bio = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProfilePicturePath = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    ProfilePictureContentType = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserFollows",
                columns: table => new
                {
                    FollowerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FollowedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFollows", x => new { x.FollowerUserId, x.FollowedUserId });
                    table.ForeignKey(
                        name: "FK_UserFollows_AspNetUsers_FollowedUserId",
                        column: x => x.FollowedUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserFollows_AspNetUsers_FollowerUserId",
                        column: x => x.FollowerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImagePosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImagePosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImagePosts_Images_ImageId",
                        column: x => x.ImageId,
                        principalTable: "Images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImagePosts_UserProfiles_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "UserProfiles",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comments_ImagePosts_PostId",
                        column: x => x.PostId,
                        principalTable: "ImagePosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Comments_UserProfiles_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "UserProfiles",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PostReactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reaction = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostReactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostReactions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PostReactions_ImagePosts_PostId",
                        column: x => x.PostId,
                        principalTable: "ImagePosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_AuthorUserId",
                table: "Comments",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_PostId_CreatedAtUtc",
                table: "Comments",
                columns: new[] { "PostId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ImagePosts_AuthorUserId_CreatedAtUtc",
                table: "ImagePosts",
                columns: new[] { "AuthorUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ImagePosts_CreatedAtUtc",
                table: "ImagePosts",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ImagePosts_ImageId",
                table: "ImagePosts",
                column: "ImageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostReactions_PostId_UserId",
                table: "PostReactions",
                columns: new[] { "PostId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostReactions_UserId",
                table: "PostReactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserFollows_FollowedUserId",
                table: "UserFollows",
                column: "FollowedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_NormalizedUserName",
                table: "UserProfiles",
                column: "NormalizedUserName",
                unique: true);

            // Backfill: accounts created before the social features get a profile with a
            // deterministic, unique user name derived from their id and the email's local part.
            migrationBuilder.Sql("""
                INSERT INTO [UserProfiles]
                    ([UserId], [UserName], [NormalizedUserName], [DisplayName], [Bio],
                     [ProfilePicturePath], [ProfilePictureContentType], [CreatedAtUtc], [UpdatedAtUtc])
                SELECT
                    u.[Id],
                    n.[Name],
                    UPPER(n.[Name]),
                    COALESCE(NULLIF(LEFT(d.[Value], 100), N''), N'User'),
                    NULL,
                    NULL,
                    NULL,
                    u.[CreatedAtUtc],
                    NULL
                FROM [AspNetUsers] AS u
                CROSS APPLY (SELECT LEFT('user-' + LOWER(REPLACE(CAST(u.[Id] AS nvarchar(36)), '-', '')), 30)) AS n([Name])
                CROSS APPLY (SELECT COALESCE(NULLIF(u.[Email], N''), NULLIF(u.[UserName], N''), N'User')) AS e([Value])
                CROSS APPLY (SELECT CASE WHEN CHARINDEX(N'@', e.[Value]) > 1
                                         THEN LEFT(e.[Value], CHARINDEX(N'@', e.[Value]) - 1)
                                         ELSE e.[Value] END) AS d([Value])
                WHERE NOT EXISTS (SELECT 1 FROM [UserProfiles] AS p WHERE p.[UserId] = u.[Id]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Comments");

            migrationBuilder.DropTable(
                name: "PostReactions");

            migrationBuilder.DropTable(
                name: "UserFollows");

            migrationBuilder.DropTable(
                name: "ImagePosts");

            migrationBuilder.DropTable(
                name: "UserProfiles");
        }
    }
}
