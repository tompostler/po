using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using po.Attributes;
using po.DataAccess;

namespace po.Controllers
{
    [ApiController]
    [Route("[controller]")]
    [ApiKeyOrSignedUrlAuthorization]
    public sealed class BlobController : ControllerBase
    {
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

            return this.File(stream, "application/octet-stream", Path.GetFileName(blobName));
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
