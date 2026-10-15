using ImageStore.Domain.Posts;
using ImageStore.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ImageStore.Infrastructure.Persistence.Configurations;

internal sealed class PostReactionConfiguration : IEntityTypeConfiguration<PostReaction>
{
    public void Configure(EntityTypeBuilder<PostReaction> builder)
    {
        builder.ToTable("PostReactions");
        builder.HasKey(reaction => reaction.Id);
        builder.Property(reaction => reaction.Id).ValueGeneratedNever();

        // Stable integer values (see ReactionType); new reactions are appended, never renumbered.
        builder.Property(reaction => reaction.Reaction).HasConversion<int>();
        builder.Property(reaction => reaction.CreatedAtUtc).IsRequired();

        // At most one active reaction per user and post – enforced by the database, so
        // concurrent requests cannot create duplicates.
        builder.HasIndex(reaction => new { reaction.PostId, reaction.UserId }).IsUnique();

        // A reaction cannot exist without its post: deleting the post removes its reactions.
        builder.HasOne<ImagePost>()
            .WithMany()
            .HasForeignKey(reaction => reaction.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: reactions are kept until an account-removal workflow decides otherwise.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(reaction => reaction.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
