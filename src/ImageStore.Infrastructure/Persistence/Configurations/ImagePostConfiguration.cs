using ImageStore.Domain.Posts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImageStore.Infrastructure.Persistence.Configurations;

internal sealed class ImagePostConfiguration : IEntityTypeConfiguration<ImagePost>
{
    public void Configure(EntityTypeBuilder<ImagePost> builder)
    {
        builder.ToTable("ImagePosts");
        builder.HasKey(post => post.Id);
        builder.Property(post => post.Id).ValueGeneratedNever();

        builder.Property(post => post.Title).HasMaxLength(PostRules.TitleMaxLength).IsRequired();
        builder.Property(post => post.Description).HasMaxLength(PostRules.DescriptionMaxLength);
        builder.Property(post => post.CreatedAtUtc).IsRequired();

        // One image is published as at most one post.
        builder.HasIndex(post => post.ImageId).IsUnique();

        // Supports a user's published gallery: WHERE AuthorUserId = @u ORDER BY CreatedAtUtc DESC.
        builder.HasIndex(post => new { post.AuthorUserId, post.CreatedAtUtc });

        // Supports the feed ordering (posts of followed users, newest first).
        builder.HasIndex(post => post.CreatedAtUtc);

        // A post cannot exist without its image: deleting the image unpublishes it (and
        // cascades to its comments and reactions). The image file itself is untouched.
        builder.HasOne(post => post.Image)
            .WithMany()
            .HasForeignKey(post => post.ImageId)
            .OnDelete(DeleteBehavior.Cascade);

        // The author's profile carries the display data. Restrict (like Image.OwnerUserId):
        // removing an account is an explicit workflow, not a side effect of a cascade.
        builder.HasOne(post => post.Author)
            .WithMany()
            .HasForeignKey(post => post.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
