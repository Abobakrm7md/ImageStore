using ImageStore.Domain.Common;
using ImageStore.Domain.Social;

namespace ImageStore.UnitTests.Social;

public sealed class UserFollowTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_Builds_A_Directed_Relationship()
    {
        var follower = Guid.NewGuid();
        var followed = Guid.NewGuid();

        var follow = UserFollow.Create(follower, followed, Now);

        Assert.Equal(follower, follow.FollowerUserId);
        Assert.Equal(followed, follow.FollowedUserId);
        Assert.Equal(Now, follow.CreatedAtUtc);
    }

    [Fact]
    public void Self_Follows_Are_Rejected()
    {
        var user = Guid.NewGuid();
        var exception = Assert.Throws<DomainValidationException>(() => UserFollow.Create(user, user, Now));
        Assert.Equal("followedUserId", exception.PropertyName);
    }

    [Fact]
    public void Empty_Ids_And_Non_Utc_Timestamps_Are_Rejected()
    {
        var user = Guid.NewGuid();
        Assert.Throws<DomainValidationException>(() => UserFollow.Create(Guid.Empty, user, Now));
        Assert.Throws<DomainValidationException>(() => UserFollow.Create(user, Guid.Empty, Now));
        Assert.Throws<DomainValidationException>(
            () => UserFollow.Create(user, Guid.NewGuid(), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)));
    }
}
