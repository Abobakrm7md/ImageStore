using ImageStore.Domain.Chat;
using ImageStore.Domain.Posts;
using ImageStore.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace ImageStore.Infrastructure.Persistence.Configurations;

internal sealed class ChatConfiguration : IEntityTypeConfiguration<Conversation>,
    IEntityTypeConfiguration<ConversationParticipant>, IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<Conversation> b)
    {
        b.ToTable("Conversations"); b.HasKey(x => x.Id);
        b.Property(x => x.PairKey).HasMaxLength(65).IsRequired();
        b.HasIndex(x => x.PairKey).IsUnique();
        b.Property(x => x.LastSequence).IsConcurrencyToken();
        b.HasMany(x => x.Participants).WithOne().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
    public void Configure(EntityTypeBuilder<ConversationParticipant> b)
    {
        b.ToTable("ConversationParticipants"); b.HasKey(x => new { x.ConversationId, x.UserId });
        b.HasIndex(x => x.UserId);
        b.Property(x => x.LastReadSequence).IsConcurrencyToken();
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
    }
    public void Configure(EntityTypeBuilder<ChatMessage> b)
    {
        b.ToTable("ChatMessages", t => t.HasCheckConstraint("CK_ChatMessages_Content", "LEN([Content]) <= 4000 AND ([MessageType] = 'post' OR ([MessageType] = 'text' AND LEN(LTRIM(RTRIM([Content]))) > 0))"));
        b.HasKey(x => x.Id); b.Property(x => x.Content).HasMaxLength(4000).IsRequired();
        b.Property(x => x.MessageType).HasMaxLength(8).IsRequired();
        b.HasIndex(x => new { x.ConversationId, x.Sequence }).IsUnique();
        b.HasOne<ConversationParticipant>().WithMany().HasForeignKey(x => new { x.ConversationId, UserId = x.SenderUserId }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<ImagePost>().WithMany().HasForeignKey(x => x.SharedPostId).OnDelete(DeleteBehavior.SetNull);
    }
}
