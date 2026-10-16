using ImageStore.Domain.Common;
using ImageStore.Domain.Social;

namespace ImageStore.UnitTests.Social;

public sealed class UserProfileTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private static UserProfile NewProfile() => UserProfile.Create(Guid.NewGuid(), "alice.dev", "Alice", Now);

    [Fact]
    public void Create_Normalizes_The_Fields()
    {
        var profile = UserProfile.Create(Guid.NewGuid(), "alice.dev", "  Alice  ", "  Photographer.  ", Now);

        Assert.Equal("alice.dev", profile.UserName);
        Assert.Equal("ALICE.DEV", profile.NormalizedUserName);
        Assert.Equal("Alice", profile.DisplayName);
        Assert.Equal("Photographer.", profile.Bio);
        Assert.False(profile.HasProfilePicture);
        Assert.Null(profile.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("a-long-name-over-thirty-chars-xxx")]
    [InlineData("-alice")]
    [InlineData("alice-")]
    [InlineData("ali ce")]
    [InlineData("ali$ce")]
    public void Invalid_User_Names_Are_Rejected(string userName)
    {
        Assert.False(UserProfile.IsValidUserName(userName));
        var exception = Assert.Throws<DomainValidationException>(
            () => UserProfile.Create(Guid.NewGuid(), userName, "Alice", Now));
        Assert.Equal("userName", exception.PropertyName);
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("a.b-c_d")]
    [InlineData("Alice99")]
    public void Valid_User_Names_Are_Accepted(string userName) =>
        Assert.True(UserProfile.IsValidUserName(userName));

    [Fact]
    public void Create_Rejects_Empty_Display_Names()
    {
        var exception = Assert.Throws<DomainValidationException>(
            () => UserProfile.Create(Guid.NewGuid(), "alice", "   ", Now));
        Assert.Equal("displayName", exception.PropertyName);
    }

    [Fact]
    public void Update_Rejects_Oversized_Bios()
    {
        var profile = NewProfile();
        var exception = Assert.Throws<DomainValidationException>(
            () => profile.Update("Alice", new string('x', SocialRules.BioMaxLength + 1), Now));
        Assert.Equal("bio", exception.PropertyName);
    }

    [Fact]
    public void Create_Rejects_Non_Utc_Timestamps_And_Empty_Ids()
    {
        var local = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Local);
        Assert.Throws<DomainValidationException>(() => UserProfile.Create(Guid.NewGuid(), "alice", "Alice", local));
        Assert.Throws<DomainValidationException>(() => UserProfile.Create(Guid.Empty, "alice", "Alice", Now));
    }

    [Fact]
    public void Update_Replaces_Display_Name_And_Bio_And_Clears_Empty_Bios()
    {
        var profile = NewProfile();

        profile.Update("Ada", "New bio", Now);
        Assert.Equal("Ada", profile.DisplayName);
        Assert.Equal("New bio", profile.Bio);
        Assert.Equal(Now, profile.UpdatedAtUtc);

        profile.Update("Ada L.", "   ", Now);
        Assert.Null(profile.Bio); // whitespace clears the bio
    }

    [Fact]
    public void Profile_Picture_Can_Be_Set_And_Cleared()
    {
        var profile = NewProfile();
        Assert.False(profile.HasProfilePicture);

        profile.SetProfilePicture($"profiles/{profile.UserId:D}.png", "image/png", Now);
        Assert.True(profile.HasProfilePicture);
        Assert.Equal("image/png", profile.ProfilePictureContentType);

        profile.ClearProfilePicture(Now);
        Assert.False(profile.HasProfilePicture);
        Assert.Null(profile.ProfilePictureContentType);
    }

    [Fact]
    public void SetProfilePicture_Rejects_Invalid_Paths_And_Content_Types()
    {
        var profile = NewProfile();

        Assert.Throws<DomainValidationException>(() => profile.SetProfilePicture("../../etc/passwd", "image/png", Now));
        Assert.Throws<DomainValidationException>(() => profile.SetProfilePicture("  ", "image/png", Now));
        Assert.Throws<DomainValidationException>(() => profile.SetProfilePicture($"profiles/{profile.UserId:D}.png", "   ", Now));
        Assert.False(profile.HasProfilePicture);
    }

    [Theory]
    [InlineData("alice@example.com", "alice")]
    [InlineData("Alice.Smith+tag@example.com", "alice.smithtag")]
    [InlineData("weird! chars#here@example.com", "weirdcharshere")]
    [InlineData("", "")]
    public void SuggestUserName_Sanitizes_Emails(string email, string expected) =>
        Assert.Equal(expected, UserProfile.SuggestUserName(email));
}
