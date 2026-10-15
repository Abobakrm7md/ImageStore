using ImageStore.Domain.Common;
using ImageStore.Domain.Images;

namespace ImageStore.UnitTests.Domain;

public sealed class ImageTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 15, 9, 30, 0, DateTimeKind.Utc);

    private static ImageFileDetails File(long size = 1234) =>
        new("cat.jpg", "stored.jpg", "owner/stored.jpg", "image/jpeg", size, 640, 480);

    [Fact]
    public void Create_TrimsTitle_AndTurnsBlankDescriptionIntoNull()
    {
        var image = Image.Create(Guid.NewGuid(), Owner, "  Cat  ", "   ", File(), Now);

        Assert.Equal("Cat", image.Title);
        Assert.Null(image.Description);
        Assert.Equal(Owner, image.OwnerUserId);
        Assert.Equal(Now, image.CreatedAtUtc);
        Assert.Null(image.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RequiresTitle(string title)
    {
        var exception = Assert.Throws<DomainValidationException>(() => Image.Create(Guid.NewGuid(), Owner, title, null, File(), Now));
        Assert.Equal("title", exception.PropertyName);
    }

    [Fact]
    public void Create_RejectsTooLongTitleAndDescription()
    {
        Assert.Throws<DomainValidationException>(() =>
            Image.Create(Guid.NewGuid(), Owner, new string('t', ImageRules.TitleMaxLength + 1), null, File(), Now));
        Assert.Throws<DomainValidationException>(() =>
            Image.Create(Guid.NewGuid(), Owner, "ok", new string('d', ImageRules.DescriptionMaxLength + 1), File(), Now));
    }

    [Fact]
    public void Create_RejectsEmptyFile_MissingOwner_AndNonUtcTimestamps()
    {
        Assert.Throws<DomainValidationException>(() => Image.Create(Guid.NewGuid(), Owner, "t", null, File(size: 0), Now));
        Assert.Throws<DomainValidationException>(() => Image.Create(Guid.NewGuid(), Guid.Empty, "t", null, File(), Now));
        Assert.Throws<DomainValidationException>(() =>
            Image.Create(Guid.NewGuid(), Owner, "t", null, File(), DateTime.SpecifyKind(Now, DateTimeKind.Local)));
    }

    [Fact]
    public void UpdateMetadata_ChangesEditableFields_AndSetsUpdatedTimestamp()
    {
        var image = Image.Create(Guid.NewGuid(), Owner, "Old", "old", File(), Now);
        var tag = Tag.Create(Guid.NewGuid(), Owner, "Pets");

        image.UpdateMetadata(" New ", " Fresh description ", [tag], Now.AddHours(1));

        Assert.Equal("New", image.Title);
        Assert.Equal("Fresh description", image.Description);
        Assert.Equal(Now.AddHours(1), image.UpdatedAtUtc);
        Assert.Equal("Pets", Assert.Single(image.Tags).Name);
        Assert.Equal("cat.jpg", image.OriginalFileName); // File metadata is immutable.
    }

    [Fact]
    public void ReplaceTags_RejectsTagsOfAnotherOwner_DuplicatesAndTooMany()
    {
        var image = Image.Create(Guid.NewGuid(), Owner, "t", null, File(), Now);

        Assert.Throws<DomainValidationException>(() => image.ReplaceTags([Tag.Create(Guid.NewGuid(), Guid.NewGuid(), "x")]));
        Assert.Throws<DomainValidationException>(() =>
            image.ReplaceTags([Tag.Create(Guid.NewGuid(), Owner, "Dog"), Tag.Create(Guid.NewGuid(), Owner, "dog")]));

        var tooMany = Enumerable.Range(0, ImageRules.MaxTagsPerImage + 1)
            .Select(i => Tag.Create(Guid.NewGuid(), Owner, $"tag{i}"))
            .ToList();
        Assert.Throws<DomainValidationException>(() => image.ReplaceTags(tooMany));
    }

    [Fact]
    public void ReplaceTags_ReplacesTheWholeSet()
    {
        var image = Image.Create(Guid.NewGuid(), Owner, "t", null, File(), Now);
        var a = Tag.Create(Guid.NewGuid(), Owner, "a");
        var b = Tag.Create(Guid.NewGuid(), Owner, "b");
        var c = Tag.Create(Guid.NewGuid(), Owner, "c");

        image.ReplaceTags([a, b]);
        image.ReplaceTags([b, c]);

        Assert.Equal(["b", "c"], image.Tags.Select(tag => tag.Name).Order());
    }
}

public sealed class TagTests
{
    [Theory]
    [InlineData("  Summer   holiday ", "Summer holiday", "SUMMER HOLIDAY")]
    [InlineData("cats", "cats", "CATS")]
    [InlineData("tab\tand\nnewline", "tab and newline", "TAB AND NEWLINE")]
    public void Clean_And_Normalize(string input, string cleaned, string normalized)
    {
        Assert.Equal(cleaned, Tag.Clean(input));
        Assert.Equal(normalized, Tag.Normalize(input));
    }

    [Fact]
    public void Create_RejectsEmptyAndTooLongNames()
    {
        Assert.Throws<DomainValidationException>(() => Tag.Create(Guid.NewGuid(), Guid.NewGuid(), "   "));
        Assert.Throws<DomainValidationException>(() =>
            Tag.Create(Guid.NewGuid(), Guid.NewGuid(), new string('x', ImageRules.TagNameMaxLength + 1)));
    }
}
