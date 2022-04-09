using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace po.DataAccess
{
    public sealed class PoStorage
    {
        private readonly BlobServiceClient blobServiceClient;
        private readonly ILogger<PoStorage> logger;

        public PoStorage(
            IOptions<Options.Storage> options,
            ILogger<PoStorage> logger)
        {
            this.blobServiceClient = new BlobServiceClient(options.Value.ConnectionString);
            this.logger = logger;
        }

        public async IAsyncEnumerable<Models.PoBlob> EnumerateAllBlobsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            uint countContainers = 0;
            uint countTotalBlobs = 0;
            await foreach (BlobContainerItem container in this.blobServiceClient.GetBlobContainersAsync(cancellationToken: cancellationToken))
            {
                countContainers++;
                uint countBlobs = 0;
                BlobContainerClient blobContainerClient = this.blobServiceClient.GetBlobContainerClient(container.Name);
                await foreach (BlobItem blob in blobContainerClient.GetBlobsAsync(cancellationToken: cancellationToken))
                {
                    countBlobs++;
                    yield return new Models.PoBlob
                    {
                        AccountName = this.blobServiceClient.AccountName,
                        ContainerName = container.Name,
                        Name = blob.Name,
                        Category = blob.Name.Split('/', StringSplitOptions.RemoveEmptyEntries).First(),
                        CreatedOn = blob.Properties.CreatedOn.Value,
                        LastModified = blob.Properties.LastModified.Value,
                        LastSeen = DateTimeOffset.UtcNow,
                        ContentLength = blob.Properties.ContentLength.Value,
                        ContentHash = blob.Properties.ContentHash?.Length > 0 ? BitConverter.ToString(blob.Properties.ContentHash).Replace("-", "").ToLower() : null
                    };
                }
                countTotalBlobs += countBlobs;
                this.logger.LogInformation($"Enumerated {countBlobs} blobs in {blobContainerClient.Uri}");
            }
            this.logger.LogInformation($"Enumerated {countTotalBlobs} blobs in {countContainers} in {this.blobServiceClient.Uri}");
        }
    }
}
