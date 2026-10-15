using ImageStore.Domain.Images;
using ImageStore.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImageStore.Infrastructure.Persistence.Configurations;

internal sealed class ImageConfiguration : IEntityTypeConfiguration<Image>
{
    public const string ImageTagsTable = "ImageTags";

    public void Configure(EntityTypeBuilder<Image> builder)
    {
        builder.ToTable("Images");
        builder.HasKey(image => image.Id);
        builder.Property(image => image.Id).ValueGeneratedNever();

        builder.Property(image => image.Title).HasMaxLength(ImageRules.TitleMaxLength).IsRequired();
        builder.Property(image => image.Description).HasMaxLength(ImageRules.DescriptionMaxLength);
        builder.Property(image => image.OriginalFileName).HasMaxLength(ImageRules.OriginalFileNameMaxLength).IsRequired();
        builder.Property(image => image.StoredFileName).HasMaxLength(ImageRules.StoredFileNameMaxLength).IsUnicode(false).IsRequired();
        builder.Property(image => image.RelativeStoragePath).HasMaxLength(ImageRules.RelativeStoragePathMaxLength).IsUnicode(false).IsRequired();
        builder.Property(image => image.ContentType).HasMaxLength(ImageRules.ContentTypeMaxLength).IsUnicode(false).IsRequired();
        builder.Property(image => image.CreatedAtUtc).IsRequired();

        // Two records must never point at the same file.
        builder.HasIndex(image => image.RelativeStoragePath).IsUnique();

        // Supports the default gallery query: WHERE OwnerUserId = @u ORDER BY CreatedAtUtc DESC.
        builder.HasIndex(image => new { image.OwnerUserId, image.CreatedAtUtc });

        // Supports title sorting and prefix lookups within a user's images.
        builder.HasIndex(image => new { image.OwnerUserId, image.Title });

        // Restrict (not cascade): deleting a user must go through a workflow that also removes files.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(image => image.OwnerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(image => image.Tags)
            .WithMany(tag => tag.Images)
            .UsingEntity<Dictionary<string, object>>(
                ImageTagsTable,
                right => right.HasOne<Tag>().WithMany().HasForeignKey("TagId").OnDelete(DeleteBehavior.Cascade),
                left => left.HasOne<Image>().WithMany().HasForeignKey("ImageId").OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable(ImageTagsTable);
                    join.HasKey("ImageId", "TagId");
                    join.HasIndex("TagId");
                });

        builder.Navigation(image => image.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
