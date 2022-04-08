using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace po.DataAccess
{
    public sealed class PoBlobs
    {
        private readonly BlobServiceClient blobServiceClient;
        private readonly ILogger<PoBlobs> logger;

        public PoBlobs(
            IOptions<Options.Storage> options,
            ILogger<PoBlobs> logger)
        {
            this.blobServiceClient = new BlobServiceClient(options.Value.ConnectionString);
            this.logger = logger;
        }
    }
}
