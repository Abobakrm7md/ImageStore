using ImageStore.Domain.Images;
using ImageStore.Domain.Posts;
using ImageStore.Domain.Social;
using ImageStore.Infrastructure.Identity;
using ImageStore.Infrastructure.Persistence.Converters;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ImageStore.Infrastructure.Persistence;

public sealed class ImageStoreDbContext(DbContextOptions<ImageStoreDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Image> Images => Set<Image>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public DbSet<UserFollow> UserFollows => Set<UserFollow>();

    public DbSet<ImagePost> ImagePosts => Set<ImagePost>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<PostReaction> PostReactions => Set<PostReaction>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ImageStoreDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // SQL Server's datetime2 does not store DateTimeKind. Every timestamp in this model is UTC,
        // so values read back are marked as UTC and serialize with a trailing "Z".
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }
}
