using ImageStore.Domain.Posts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImageStore.Infrastructure.Persistence.Configurations;

internal sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("Comments");
        builder.HasKey(comment => comment.Id);
        builder.Property(comment => comment.Id).ValueGeneratedNever();

        builder.Property(comment => comment.Content).HasMaxLength(PostRules.CommentMaxLength).IsRequired();
        builder.Property(comment => comment.CreatedAtUtc).IsRequired();

        // Supports "comments of a post, oldest first".
        builder.HasIndex(comment => new { comment.PostId, comment.CreatedAtUtc });

        // A comment cannot exist without its post: deleting the post removes its comments.
        builder.HasOne<ImagePost>()
            .WithMany()
            .HasForeignKey(comment => comment.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: deleting an account must not silently erase a discussion; account removal
        // is an explicit workflow that decides what happens to the user's comments.
        builder.HasOne(comment => comment.Author)
            .WithMany()
            .HasForeignKey(comment => comment.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
