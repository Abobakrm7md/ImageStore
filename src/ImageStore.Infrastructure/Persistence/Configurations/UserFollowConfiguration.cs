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

        // Follow relationships are meaningless without their users: they go with the account.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(follow => follow.FollowerUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(follow => follow.FollowedUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
