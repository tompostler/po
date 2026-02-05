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

        [HttpPost("{containerName}/{*blobName}")]
        public async Task<IActionResult> Upload(
            string containerName,
            string blobName,
            CancellationToken cancellationToken)
        {
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
    }
}
