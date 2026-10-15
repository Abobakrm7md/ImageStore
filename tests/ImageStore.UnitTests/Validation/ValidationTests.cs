using ImageStore.Application.Images.Storage;
using ImageStore.Application.Images.Validation;
using ImageStore.Domain.Images;

namespace ImageStore.UnitTests.Validation;

public sealed class FileNameSanitizerTests
{
    [Theory]
    [InlineData("cat.jpg", "cat.jpg")]
    [InlineData("../../../etc/passwd.png", "passwd.png")]
    [InlineData("..\\..\\Windows\\System32\\evil.jpg", "evil.jpg")]
    [InlineData("C:\\Users\\me\\Pictures\\holiday.webp", "holiday.webp")]
    [InlineData("/var/www/html/shell.png", "shell.png")]
    [InlineData("in<va>li:d\"|?*name.png", "invalidname.png")]
    [InlineData("new\r\nline\u0000.jpg", "newline.jpg")]
    [InlineData("  .hidden.png  ", "hidden.png")]
    [InlineData("photo.jpg.", "photo.jpg")]
    [InlineData("", FileNameSanitizer.Fallback)]
    [InlineData("   ", FileNameSanitizer.Fallback)]
    [InlineData("../", FileNameSanitizer.Fallback)]
    [InlineData("...", FileNameSanitizer.Fallback)]
    public void Sanitize_Removes_Paths_And_Dangerous_Characters(string input, string expected) =>
        Assert.Equal(expected, FileNameSanitizer.Sanitize(input));

    [Fact]
    public void Sanitize_Truncates_Long_Names_But_Keeps_The_Extension()
    {
        var result = FileNameSanitizer.Sanitize(new string('a', 400) + ".jpeg");

        Assert.Equal(ImageRules.OriginalFileNameMaxLength, result.Length);
        Assert.EndsWith(".jpeg", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_Never_Splits_Surrogate_Pairs()
    {
        var name = new string('a', ImageRules.OriginalFileNameMaxLength - 5) + "😀😀😀😀😀.png";

        var result = FileNameSanitizer.Sanitize(name);

        Assert.True(result.Length <= ImageRules.OriginalFileNameMaxLength);
        Assert.False(char.IsHighSurrogate(result[^5]), "A high surrogate must not precede the extension.");
    }

    [Theory]
    [InlineData("Holiday.JPG", ".jpg")]
    [InlineData("archive.tar.png", ".png")]
    [InlineData("noextension", "")]
    public void GetExtension_Is_Lower_Case(string name, string expected) =>
        Assert.Equal(expected, FileNameSanitizer.GetExtension(name));

    [Theory]
    [InlineData("sunset.jpg", "sunset")]
    [InlineData(".png", "Untitled image")]
    public void ToTitle_Uses_The_Name_Without_Extension(string name, string expected) =>
        Assert.Equal(expected, FileNameSanitizer.ToTitle(name));
}

public sealed class ImageStoragePathsTests
{
    [Fact]
    public void Builds_Paths_From_Server_Generated_Identifiers()
    {
        var owner = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var image = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var fileName = ImageStoragePaths.CreateStoredFileName(image, ".jpg");
        var path = ImageStoragePaths.CreateRelativePath(owner, fileName);

        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee.jpg", fileName);
        Assert.Equal("11111111-2222-3333-4444-555555555555/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee.jpg", path);
        Assert.True(ImageStoragePaths.IsValidRelativePath(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../outside.jpg")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("11111111-2222-3333-4444-555555555555/../../etc/passwd")]
    [InlineData("11111111-2222-3333-4444-555555555555/..%2F..%2Fsecret.jpg")]
    [InlineData("11111111-2222-3333-4444-555555555555\\aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee.jpg")]
    [InlineData("11111111-2222-3333-4444-555555555555/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee.jpg\n")]
    [InlineData("11111111-2222-3333-4444-555555555555/AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE.jpg")]
    [InlineData("11111111-2222-3333-4444-555555555555/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee.jpg/x")]
    [InlineData("11111111-2222-3333-4444-555555555555/nested/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee.jpg")]
    public void Rejects_Anything_That_Is_Not_A_Generated_Path(string? path) =>
        Assert.False(ImageStoragePaths.IsValidRelativePath(path));

    [Fact]
    public void Rejects_Invalid_Inputs_When_Building()
    {
        Assert.Throws<ArgumentException>(() => ImageStoragePaths.CreateStoredFileName(Guid.Empty, ".jpg"));
        Assert.Throws<ArgumentException>(() => ImageStoragePaths.CreateStoredFileName(Guid.NewGuid(), "/../x"));
        Assert.Throws<ArgumentException>(() => ImageStoragePaths.CreateRelativePath(Guid.NewGuid(), "../evil.jpg"));
    }
}

public sealed class ImageUploadOptionsValidatorTests
{
    private static ImageUploadOptions ValidOptions() => new()
    {
        AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"],
        AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"],
    };

    [Fact]
    public void Accepts_The_Default_Configuration() =>
        Assert.True(new ImageUploadOptionsValidator().Validate(null, ValidOptions()).Succeeded);

    [Fact]
    public void Rejects_Extensions_Without_A_Signature_Check()
    {
        var options = ValidOptions();
        options.AllowedExtensions.Add(".gif");

        var result = new ImageUploadOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(".gif", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_An_Extension_Whose_Content_Type_Is_Not_Allowed()
    {
        var options = ValidOptions();
        options.AllowedContentTypes.Remove("image/png");

        Assert.True(new ImageUploadOptionsValidator().Validate(null, options).Failed);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(ImageUploadOptions.AbsoluteMaxFileSizeBytes + 1, 10)]
    public void Rejects_Invalid_Limits(long maxFileSize, int maxFiles)
    {
        var options = ValidOptions();
        options.MaxFileSizeBytes = maxFileSize;
        options.MaxFilesPerRequest = maxFiles;

        Assert.True(new ImageUploadOptionsValidator().Validate(null, options).Failed);
    }

    [Fact]
    public void Rejects_Empty_Allow_Lists() =>
        Assert.True(new ImageUploadOptionsValidator().Validate(null, new ImageUploadOptions()).Failed);
}
