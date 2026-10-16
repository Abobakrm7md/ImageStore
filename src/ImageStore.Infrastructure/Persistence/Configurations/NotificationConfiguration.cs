using ImageStore.Domain.Notifications;
using ImageStore.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ImageStore.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>, IEntityTypeConfiguration<NotificationPreferences>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("Notifications");
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.EventKey).HasMaxLength(160).IsRequired();
        b.Property(x => x.Announcement).HasMaxLength(240);
        b.Property(x => x.Type).HasConversion<int>();
        b.HasIndex(x => new { x.RecipientUserId, x.EventKey }).IsUnique();
        b.HasIndex(x => new { x.RecipientUserId, x.CreatedAtUtc, x.Id });
        b.HasIndex(x => new { x.RecipientUserId, x.ReadAtUtc, x.CreatedAtUtc }).HasFilter("[DismissedAtUtc] IS NULL");
        b.HasIndex(x => new { x.DispatchedAtUtc, x.NextAttemptAtUtc }).HasFilter("[DispatchedAtUtc] IS NULL");
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.NoAction);
        // Source IDs are logical references: history survives content deletion. All navigation is
        // re-authorized at projection time; deletion never grants or preserves access.
    }
    public void Configure(EntityTypeBuilder<NotificationPreferences> b)
    {
        b.ToTable("NotificationPreferences", t => t.HasCheckConstraint("CK_NotificationPreferences_Volume", "[SoundVolume] >= 0 AND [SoundVolume] <= 100"));
        b.HasKey(x => x.UserId); b.Property(x => x.UserId).ValueGeneratedNever();
        b.HasOne<ApplicationUser>().WithOne().HasForeignKey<NotificationPreferences>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
