using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using po.Attributes;
using po.DataAccess;

namespace po.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [ApiKeyOrSignedUrlAuthorization]
    public sealed class BlobController : ControllerBase
    {
        private static readonly FileExtensionContentTypeProvider ContentTypes = new();

        private readonly IPoStorage storage;

        public BlobController(IPoStorage storage)
        {
            this.storage = storage;
        }

        private static readonly Regex ValidContainerName = new(@"^[a-zA-Z0-9]+$", RegexOptions.Compiled);
        private static readonly Regex InvalidBlobName = new(@"(^/|//|/\.\./|/\.\.$|^\.\./|^\.\.$)", RegexOptions.Compiled);
        private static readonly Regex ValidBlobNameChars = new(@"^[a-zA-Z0-9/._\- ]+$", RegexOptions.Compiled);

        private BadRequestObjectResult ValidateNames(string containerName, string blobName = null)
        {
            if (!ValidContainerName.IsMatch(containerName))
            {
                return this.BadRequest($"Container name '{containerName}' contains invalid characters. Only alphanumeric characters are allowed.");
            }

            if (blobName != null)
            {
                if (!ValidBlobNameChars.IsMatch(blobName) || InvalidBlobName.IsMatch(blobName))
                {
                    return this.BadRequest($"Blob name '{blobName}' is invalid. Must be a relative path with alphanumeric characters, forward slashes, and single dots not adjacent to slashes.");
                }
            }

            return null;
        }

        [HttpPost("{containerName}/{*blobName}")]
        public async Task<IActionResult> Upload(
            string containerName,
            string blobName,
            CancellationToken cancellationToken)
        {
            BadRequestObjectResult validationError = this.ValidateNames(containerName, blobName);
            if (validationError != null)
            {
                return validationError;
            }

            Models.PoBlob result = await this.storage.UploadBlobAsync(
                containerName,
                blobName,
                this.Request.Body,
                cancellationToken);

            return this.Ok(result);
        }

        [HttpGet("{containerName}")]
        public async Task<IActionResult> List(
            string containerName,
            CancellationToken cancellationToken)
        {
            BadRequestObjectResult validationError = this.ValidateNames(containerName);
            if (validationError != null)
            {
                return validationError;
            }

            if (!await this.storage.ContainerExistsAsync(containerName))
            {
                return this.NotFound();
            }

            var blobs = new List<Models.PoBlob>();
            await foreach (Models.PoBlob blob in this.storage.EnumerateAllBlobsAsync(containerName, cancellationToken))
            {
                blobs.Add(blob);
            }

            return this.Ok(blobs);
        }

        /// <remarks>
        /// Any origin may read a blob, because the signed URL is already the credential and an <c>img</c> tag can load it from anywhere.
        /// Mattermost's full-size image viewer fetches the image with script, which needs that CORS allowance, unlike the inline preview.
        /// The header is sent on every response, not only ones with an <c>Origin</c>, because the viewer reuses the copy the preview cached, and Cloudflare lets clients cache images for hours.
        /// The blob is served inline with its real content type, rather than as a download, so clients that respect the headers display it.
        /// </remarks>
        [HttpGet("{containerName}/{*blobName}")]
        public async Task<IActionResult> Download(
            string containerName,
            string blobName,
            CancellationToken cancellationToken)
        {
            BadRequestObjectResult validationError = this.ValidateNames(containerName, blobName);
            if (validationError != null)
            {
                return validationError;
            }

            Stream stream = await this.storage.DownloadBlobIfExistsAsync(
                containerName,
                blobName,
                cancellationToken);

            if (stream == null)
            {
                return this.NotFound();
            }

            this.Response.Headers.AccessControlAllowOrigin = "*";
            if (!ContentTypes.TryGetContentType(blobName, out string contentType))
            {
                contentType = "application/octet-stream";
            }

            return this.File(stream, contentType, enableRangeProcessing: true);
        }

        [HttpDelete("{containerName}/{*blobName}")]
        public async Task<IActionResult> Delete(
            string containerName,
            string blobName,
            CancellationToken cancellationToken)
        {
            BadRequestObjectResult validationError = this.ValidateNames(containerName, blobName);
            if (validationError != null)
            {
                return validationError;
            }

            bool deleted = await this.storage.DeleteBlobIfExistsAsync(
                containerName,
                blobName,
                cancellationToken);

            if (!deleted)
            {
                return this.NotFound();
            }

            return this.NoContent();
        }
    }
}
