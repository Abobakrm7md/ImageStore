using ImageStore.Domain.Images;
using ImageStore.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImageStore.Infrastructure.Persistence.Configurations;

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("Tags");
        builder.HasKey(tag => tag.Id);
        builder.Property(tag => tag.Id).ValueGeneratedNever();
        builder.Property(tag => tag.Name).HasMaxLength(ImageRules.TagNameMaxLength).IsRequired();
        builder.Property(tag => tag.NormalizedName).HasMaxLength(ImageRules.TagNameMaxLength).IsRequired();

        // Tag names are unique per user (case-insensitive via NormalizedName).
        builder.HasIndex(tag => new { tag.OwnerUserId, tag.NormalizedName }).IsUnique();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(tag => tag.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(tag => tag.Images).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
