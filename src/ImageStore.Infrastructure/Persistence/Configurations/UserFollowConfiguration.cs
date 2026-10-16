using ImageStore.Domain.Social;
using ImageStore.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImageStore.Infrastructure.Persistence.Configurations;

internal sealed class UserFollowConfiguration : IEntityTypeConfiguration<UserFollow>
{
    public void Configure(EntityTypeBuilder<UserFollow> builder)
    {
        builder.ToTable("UserFollows");

        // The composite key enforces at the database level that a follower/followed pair can
        // never exist twice, and that nobody can follow themselves (checked in the domain too).
        builder.HasKey(follow => new { follow.FollowerUserId, follow.FollowedUserId });

        builder.Property(follow => follow.FollowerUserId).ValueGeneratedNever();
        builder.Property(follow => follow.FollowedUserId).ValueGeneratedNever();
        builder.Property(follow => follow.CreatedAtUtc).IsRequired();

        // Supports "profiles of my followers": WHERE FollowedUserId = @u.
        // (The key already covers WHERE FollowerUserId = @u for the following list.)
        builder.HasIndex(follow => follow.FollowedUserId);

        // Restrict on both sides. SQL Server forbids two cascade paths from AspNetUsers into the
        // same table ("may cause cycles or multiple cascade paths"), and account deletion is
        // already blocked by the Restrict FKs on authored content: follow rows are removed
        // explicitly before a user is deleted, so nothing unrelated is ever taken with it.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(follow => follow.FollowerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(follow => follow.FollowedUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
