using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

namespace po.DataAccess
{
    public sealed class PoBlobStorage : IPoStorage
    {
        private readonly BlobServiceClient blobServiceClient;
        private readonly ILogger<PoBlobStorage> logger;

        public PoBlobStorage(
            IOptions<Options.Storage> options,
            ILogger<PoBlobStorage> logger)
        {
            this.blobServiceClient = new BlobServiceClient(options.Value.ConnectionString);
            this.logger = logger;
        }

        public async IAsyncEnumerable<Models.PoBlob> EnumerateAllBlobsAsync([EnumeratorCancellation] CancellationToken cancellationToken, bool goSlow = false)
        {
            uint countContainers = 0;
            uint countTotalBlobs = 0;
            await foreach (BlobContainerItem container in this.blobServiceClient.GetBlobContainersAsync(cancellationToken: cancellationToken))
            {
                countContainers++;
                await foreach (Models.PoBlob blob in this.EnumerateAllBlobsAsync(container.Name, cancellationToken))
                {
                    if (goSlow)
                    {
                        // Slow down searching for all the blobs to make it easier on blobs and sql (when doing the background job scrape)
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    }

                    countTotalBlobs++;
                    yield return blob;
                }
            }
            this.logger.LogInformation($"Enumerated {countTotalBlobs} blobs in {countContainers} in {this.blobServiceClient.Uri}");
        }

        public async IAsyncEnumerable<Models.PoBlob> EnumerateAllBlobsAsync(string containerName, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            uint countBlobs = 0;
            BlobContainerClient blobContainerClient = this.blobServiceClient.GetBlobContainerClient(containerName);
            await foreach (BlobItem blob in blobContainerClient.GetBlobsAsync(cancellationToken: cancellationToken))
            {
                countBlobs++;
                yield return new Models.PoBlob
                {
                    AccountName = this.blobServiceClient.AccountName,
                    ContainerName = blobContainerClient.Name,
                    Name = blob.Name,
                    Category = blob.Name.Split('/', StringSplitOptions.RemoveEmptyEntries).First(),
                    CreatedOn = blob.Properties.CreatedOn.Value,
                    LastModified = blob.Properties.LastModified.Value,
                    LastSeen = DateTimeOffset.UtcNow,
                    ContentLength = blob.Properties.ContentLength.Value,
                    ContentHash = blob.Properties.ContentHash?.Length > 0 ? BitConverter.ToString(blob.Properties.ContentHash).Replace("-", "").ToLower() : null
                };
            }
            this.logger.LogInformation($"Enumerated {countBlobs} blobs in {blobContainerClient.Uri}");
        }

        public async Task<bool> ContainerExistsAsync(string containerName)
            => await this.blobServiceClient.GetBlobContainerClient(containerName).ExistsAsync();

        public Uri GetReadOnlyUri(Models.PoBlob blob)
            => this.blobServiceClient
                .GetBlobContainerClient(blob.ContainerName)
                .GetBlobClient(blob.Name)
                .GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddDays(1));
    }
}
