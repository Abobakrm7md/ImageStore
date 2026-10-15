using ImageStore.Domain.Social;
using ImageStore.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImageStore.Infrastructure.Persistence.Configurations;

internal sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("UserProfiles");
        builder.HasKey(profile => profile.UserId);
        builder.Property(profile => profile.UserId).ValueGeneratedNever();

        builder.Property(profile => profile.UserName).HasMaxLength(SocialRules.UserNameMaxLength).IsRequired();
        builder.Property(profile => profile.NormalizedUserName).HasMaxLength(SocialRules.UserNameMaxLength).IsUnicode(false).IsRequired();
        builder.Property(profile => profile.DisplayName).HasMaxLength(SocialRules.DisplayNameMaxLength).IsRequired();
        builder.Property(profile => profile.Bio).HasMaxLength(SocialRules.BioMaxLength);
        builder.Property(profile => profile.ProfilePicturePath).HasMaxLength(SocialRules.ProfilePicturePathMaxLength).IsUnicode(false);
        builder.Property(profile => profile.ProfilePictureContentType).HasMaxLength(SocialRules.ProfilePictureContentTypeMaxLength).IsUnicode(false);
        builder.Property(profile => profile.CreatedAtUtc).IsRequired();

        // User names are unique (case-insensitive via NormalizedUserName).
        builder.HasIndex(profile => profile.NormalizedUserName).IsUnique();

        // The profile is owned by the account: deleting the user removes the profile.
        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<UserProfile>(profile => profile.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
