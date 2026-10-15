using ImageStore.Api.Contracts;
using ImageStore.Api.Filters;
using ImageStore.Api.Security;
using ImageStore.Application.Common;
using ImageStore.Application.Images;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace ImageStore.Api.Controllers;

/// <summary>
/// The current user's images. Ownership is enforced by the application layer inside every
/// database query; an image that belongs to someone else is indistinguishable from one that
/// does not exist (404).
/// </summary>
[ApiController]
[Route("api/images")]
[Authorize(Policy = AuthorizationPolicies.AuthenticatedUser)]
public sealed class ImagesController(IImageService imageService) : ControllerBase
{
    private const string ProblemJson = "application/problem+json";

    /// <summary>Lists images with server-side pagination, search and sorting.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ImageDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    public Task<PagedResult<ImageDto>> List([FromQuery] ImageListQuery query, CancellationToken cancellationToken) =>
        imageService.ListAsync(query, cancellationToken);

    /// <summary>Returns the metadata of one image.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ImageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public Task<ImageDto> Get(Guid id, CancellationToken cancellationToken) =>
        imageService.GetAsync(id, cancellationToken);

    /// <summary>Returns the upload limits so clients can validate files before uploading.</summary>
    [HttpGet("upload-policy")]
    [ProducesResponseType<UploadPolicyDto>(StatusCodes.Status200OK)]
    public UploadPolicyDto GetUploadPolicy() => imageService.GetUploadPolicy();

    /// <summary>
    /// Uploads one or more images (multipart/form-data, field <c>files</c>). Content is verified by
    /// file signature; if any file is invalid, nothing is stored and the errors are reported per file
    /// (<c>files[i]</c>).
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ServiceFilter<ImageUploadLimitsFilter>]
    [ProducesResponseType<UploadImagesResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, ProblemJson)]
    public async Task<ActionResult<UploadImagesResponse>> Upload([FromForm] UploadImagesForm form, CancellationToken cancellationToken)
    {
        var files = form.Files
            .Select(file => new UploadFile(file.FileName, file.Length, file.ContentType, file.OpenReadStream))
            .ToList();

        var created = await imageService.UploadAsync(
            new UploadImagesCommand(files, form.Title, form.Description, form.Tags),
            cancellationToken);

        var response = new UploadImagesResponse(created);
        return created.Count == 1
            ? CreatedAtAction(nameof(Get), new { id = created[0].Id }, response)
            : StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>Updates the title, description and tags of an image.</summary>
    [HttpPut("{id:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType<ImageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, ProblemJson)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public Task<ImageDto> Update(Guid id, UpdateImageRequest request, CancellationToken cancellationToken) =>
        imageService.UpdateAsync(id, request, cancellationToken);

    /// <summary>Deletes an image and its stored file.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await imageService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Streams the image for display (<c>Content-Disposition: inline</c>). Supports ETag and range requests.</summary>
    [HttpGet("{id:guid}/content")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "image/jpeg", "image/png", "image/webp")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<IActionResult> GetContent(Guid id, CancellationToken cancellationToken)
    {
        var content = await imageService.GetContentAsync(id, cancellationToken);

        var disposition = new ContentDispositionHeaderValue("inline");
        disposition.SetHttpFileName(content.FileName);
        Response.Headers.ContentDisposition = disposition.ToString();
        SetPrivateCaching();

        return File(
            content.Content,
            content.ContentType,
            content.LastModifiedUtc,
            new EntityTagHeaderValue(content.EntityTag),
            enableRangeProcessing: true);
    }

    /// <summary>Downloads the image (<c>Content-Disposition: attachment</c> with the original file name).</summary>
    [HttpGet("{id:guid}/download")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "image/jpeg", "image/png", "image/webp")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, ProblemJson)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var content = await imageService.GetContentAsync(id, cancellationToken);
        SetPrivateCaching();

        return File(
            content.Content,
            content.ContentType,
            content.FileName,
            content.LastModifiedUtc,
            new EntityTagHeaderValue(content.EntityTag),
            enableRangeProcessing: true);
    }

    /// <summary>
    /// Private content: never stored by shared caches, and revalidated (cheaply, via ETag) on every
    /// use so access checks always run.
    /// </summary>
    private void SetPrivateCaching() => Response.Headers.CacheControl = "private, no-cache";
}
