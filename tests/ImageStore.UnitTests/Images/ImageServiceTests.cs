using ImageStore.Application.Common;
using ImageStore.Application.Images;
using ImageStore.Application.Images.Storage;
using ImageStore.Application.Images.Validation;
using ImageStore.Domain.Images;
using ImageStore.Infrastructure.Storage;
using ImageStore.UnitTests.Storage;
using ImageStore.UnitTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ImageStore.UnitTests.Images;

public sealed class ImageServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();
    private readonly LocalImageFileStorage _storage;
    private readonly FakeImageRepository _repository = new();
    private readonly FakeCurrentUser _currentUser = new(Guid.NewGuid());
    private readonly FakeTimeProvider _time = new(Now);
    private readonly ImageUploadOptions _options = new()
    {
        MaxFileSizeBytes = 64 * 1024,
        MaxFilesPerRequest = 3,
        AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"],
        AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"],
    };

    public ImageServiceTests() =>
        _storage = LocalImageFileStorageTests.CreateStorage(Path.Combine(_temp.Path, "uploads"), _temp.Path);

    public void Dispose() => _temp.Dispose();

    private Guid UserId => _currentUser.UserId!.Value;

    private ImageService CreateService(IImageFileStorage? storage = null) =>
        new(_repository, storage ?? _storage, _currentUser, Options.Create(_options), _time, NullLogger<ImageService>.Instance);

    private static UploadFile File(string fileName, byte[] content, string? declaredContentType = null, long? declaredLength = null) =>
        new(fileName, declaredLength ?? content.Length, declaredContentType, () => new MemoryStream(content));

    private static UploadFile Fixture(string fixture, string? fileName = null, string? declaredContentType = null) =>
        File(fileName ?? fixture, TestFiles.Read(fixture), declaredContentType);

    private string[] StoredFiles() =>
        Directory.GetFiles(_storage.RootPath, "*", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(_storage.StagingPath, StringComparison.Ordinal))
            .ToArray();

    [Fact]
    public async Task Upload_Stores_File_And_Metadata_Derived_By_The_Server()
    {
        var service = CreateService();

        var created = await service.UploadAsync(
            new UploadImagesCommand([Fixture("sample.png", "../../My Holiday.png", declaredContentType: "text/plain")], null, " Beach ", ["Summer", "summer", "Sea"]),
            CancellationToken.None);

        var dto = Assert.Single(created);
        var image = Assert.Single(_repository.Images);
        Assert.Equal(UserId, image.OwnerUserId);
        Assert.Equal("My Holiday", dto.Title);
        Assert.Equal("Beach", dto.Description);
        Assert.Equal("My Holiday.png", dto.OriginalFileName);
        Assert.Equal("image/png", dto.ContentType); // From the signature, not the declared "text/plain".
        Assert.Equal((20, 10), (dto.Width, dto.Height));
        Assert.Equal(["Sea", "Summer"], dto.Tags);
        Assert.Equal(Now.UtcDateTime, dto.CreatedAtUtc);
        Assert.Equal($"{UserId:D}/{image.Id:D}.png", image.RelativeStoragePath);
        Assert.Equal(TestFiles.Read("sample.png"), await System.IO.File.ReadAllBytesAsync(Path.Combine(_storage.RootPath, image.RelativeStoragePath)));
        Assert.Empty(Directory.GetFiles(_storage.StagingPath));
    }

    [Fact]
    public async Task Upload_Of_Several_Formats_Detects_Each_One()
    {
        var created = await CreateService().UploadAsync(
            new UploadImagesCommand([Fixture("sample.jpg"), Fixture("sample-lossless.webp"), Fixture("sample-alpha.webp")], "Batch", null, null),
            CancellationToken.None);

        Assert.Equal(["image/jpeg", "image/webp", "image/webp"], created.Select(image => image.ContentType));
        Assert.All(created, image => Assert.Equal("Batch", image.Title));
        Assert.Equal(3, StoredFiles().Length);
    }

    [Fact]
    public async Task Upload_With_Any_Invalid_File_Stores_Nothing_And_Reports_Per_File_Errors()
    {
        var command = new UploadImagesCommand(
            [Fixture("sample.png"), File("evil.png", "<?php echo 'pwned'; ?>"u8.ToArray()), Fixture("sample.png", "photo.jpg")],
            null,
            null,
            null);

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() => CreateService().UploadAsync(command, CancellationToken.None));

        Assert.Equal(["files[1]", "files[2]"], exception.Errors.Keys.Order());
        Assert.Contains("does not match", exception.Errors["files[2]"][0], StringComparison.Ordinal);
        Assert.Empty(_repository.Images);
        Assert.Empty(StoredFiles());
        Assert.Empty(Directory.GetFiles(_storage.StagingPath));
    }

    [Fact]
    public async Task Upload_Enforces_The_Size_Limit_On_Received_Bytes_Not_The_Declared_Length()
    {
        var oversized = TestFiles.CreatePng(400, 400).Concat(new byte[70 * 1024]).ToArray();
        var lyingFile = File("big.png", oversized, declaredLength: 100);

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            CreateService().UploadAsync(new UploadImagesCommand([lyingFile], null, null, null), CancellationToken.None));

        Assert.Contains("maximum size", exception.Errors["files[0]"][0], StringComparison.Ordinal);
        Assert.Empty(StoredFiles());
        Assert.Empty(Directory.GetFiles(_storage.StagingPath));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("image.gif")]
    [InlineData("noextension")]
    public async Task Upload_Rejects_Disallowed_Extensions(string fileName)
    {
        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            CreateService().UploadAsync(new UploadImagesCommand([Fixture("sample.png", fileName)], null, null, null), CancellationToken.None));

        Assert.True(exception.Errors.ContainsKey("files[0]"));
    }

    [Fact]
    public async Task Upload_Rejects_Empty_Requests_Too_Many_Files_And_Invalid_Metadata()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<RequestValidationException>(() => service.UploadAsync(new UploadImagesCommand([], null, null, null), CancellationToken.None));
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.UploadAsync(new UploadImagesCommand(Enumerable.Repeat(Fixture("sample.png"), 4).ToList(), null, null, null), CancellationToken.None));
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.UploadAsync(new UploadImagesCommand([Fixture("sample.png")], new string('x', ImageRules.TitleMaxLength + 1), null, null), CancellationToken.None));
        Assert.Empty(StoredFiles());
    }

    [Fact]
    public async Task Upload_Removes_Committed_Files_When_The_Database_Write_Fails()
    {
        _repository.SaveFailure = new InvalidOperationException("Simulated database outage");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().UploadAsync(new UploadImagesCommand([Fixture("sample.png"), Fixture("sample.jpg")], null, null, null), CancellationToken.None));

        Assert.Empty(_repository.Images);
        Assert.Empty(StoredFiles());
        Assert.Empty(Directory.GetFiles(_storage.StagingPath));
    }

    [Fact]
    public async Task Upload_Keeps_Files_When_The_Database_Reports_An_Error_But_Committed_Anyway()
    {
        _repository.SaveFailure = new TimeoutException("Commit acknowledgement lost");
        _repository.AnyExistOverride = true;

        await Assert.ThrowsAsync<TimeoutException>(() =>
            CreateService().UploadAsync(new UploadImagesCommand([Fixture("sample.png")], null, null, null), CancellationToken.None));

        Assert.Single(StoredFiles());
    }

    [Fact]
    public async Task Upload_Keeps_Files_When_The_Outcome_Cannot_Be_Verified()
    {
        _repository.SaveFailure = new InvalidOperationException("Database down");
        _repository.AnyExistFailure = new InvalidOperationException("Still down");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService().UploadAsync(new UploadImagesCommand([Fixture("sample.png")], null, null, null), CancellationToken.None));

        // Orphaned (harmless, reported by maintenance) rather than possibly breaking a committed record.
        Assert.Single(StoredFiles());
    }

    [Fact]
    public async Task Operations_Require_An_Authenticated_User()
    {
        _currentUser.UserId = null;
        var service = CreateService();

        await Assert.ThrowsAsync<UnauthenticatedException>(() => service.ListAsync(new ImageListQuery(), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthenticatedException>(() =>
            service.UploadAsync(new UploadImagesCommand([Fixture("sample.png")], null, null, null), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthenticatedException>(() => service.DeleteAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Update_Changes_Metadata_And_Removes_Tags_No_Longer_Used()
    {
        var service = CreateService();
        var created = await service.UploadAsync(new UploadImagesCommand([Fixture("sample.png")], "Old", null, ["keep", "drop"]), CancellationToken.None);
        _time.Now = Now.AddMinutes(5);

        var updated = await service.UpdateAsync(created[0].Id, new UpdateImageRequest(" New title ", "Described", ["keep", "added"]), CancellationToken.None);

        Assert.Equal("New title", updated.Title);
        Assert.Equal("Described", updated.Description);
        Assert.Equal(["added", "keep"], updated.Tags);
        Assert.Equal(Now.AddMinutes(5).UtcDateTime, updated.UpdatedAtUtc);
        Assert.DoesNotContain(_repository.Tags, tag => tag.Name == "drop");
    }

    [Fact]
    public async Task Update_Validates_The_Request()
    {
        var service = CreateService();
        var created = await service.UploadAsync(new UploadImagesCommand([Fixture("sample.png")], null, null, null), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.UpdateAsync(created[0].Id, new UpdateImageRequest("  ", new string('d', ImageRules.DescriptionMaxLength + 1), [""]), CancellationToken.None));

        Assert.Equal(["description", "tags", "title"], exception.Errors.Keys.Order());
    }

    [Fact]
    public async Task Another_Users_Image_Behaves_As_If_It_Did_Not_Exist()
    {
        var service = CreateService();
        var created = await service.UploadAsync(new UploadImagesCommand([Fixture("sample.png")], "Private", null, null), CancellationToken.None);
        var ownerId = UserId;

        _currentUser.UserId = Guid.NewGuid();
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(created[0].Id, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetContentAsync(created[0].Id, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAsync(created[0].Id, new UpdateImageRequest("Hacked", null, null), CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(created[0].Id, CancellationToken.None));
        Assert.Equal(0, (await service.ListAsync(new ImageListQuery(), CancellationToken.None)).TotalCount);

        var image = Assert.Single(_repository.Images);
        Assert.Equal(("Private", ownerId), (image.Title, image.OwnerUserId));
        Assert.Single(StoredFiles());
    }

    [Fact]
    public async Task Delete_Removes_The_Record_Then_The_File()
    {
        var service = CreateService();
        var created = await service.UploadAsync(new UploadImagesCommand([Fixture("sample.png")], null, null, ["solo"]), CancellationToken.None);

        await service.DeleteAsync(created[0].Id, CancellationToken.None);

        Assert.Empty(_repository.Images);
        Assert.Empty(_repository.Tags); // Orphaned tags are removed with the image.
        Assert.Empty(StoredFiles());
    }

    [Fact]
    public async Task Delete_Succeeds_When_The_File_Is_Already_Missing()
    {
        var service = CreateService();
        var created = await service.UploadAsync(new UploadImagesCommand([Fixture("sample.png")], null, null, null), CancellationToken.None);
        System.IO.File.Delete(Path.Combine(_storage.RootPath, _repository.Images[0].RelativeStoragePath));

        await service.DeleteAsync(created[0].Id, CancellationToken.None);

        Assert.Empty(_repository.Images);
    }

    [Fact]
    public async Task Delete_Keeps_The_File_When_The_Database_Delete_Fails()
    {
        var service = CreateService();
        var created = await service.UploadAsync(new UploadImagesCommand([Fixture("sample.png")], null, null, null), CancellationToken.None);
        _repository.SaveFailure = new InvalidOperationException("Simulated database outage");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(created[0].Id, CancellationToken.None));

        Assert.Single(_repository.Images);
        Assert.Single(StoredFiles()); // Still consistent: record and file both present.
    }

    [Fact]
    public async Task GetContent_Streams_The_Stored_Bytes()
    {
        var service = CreateService();
        var created = await service.UploadAsync(new UploadImagesCommand([Fixture("sample.jpg")], null, null, null), CancellationToken.None);

        var content = await service.GetContentAsync(created[0].Id, CancellationToken.None);
        await using var stream = content.Content;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        Assert.Equal("image/jpeg", content.ContentType);
        Assert.Equal("sample.jpg", content.FileName);
        Assert.Equal(TestFiles.Read("sample.jpg"), buffer.ToArray());
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(null, 0)]
    [InlineData(null, 101)]
    public async Task List_Validates_Paging(int? page, int? pageSize)
    {
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            CreateService().ListAsync(new ImageListQuery { Page = page, PageSize = pageSize }, CancellationToken.None));
    }
}
