using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace po.DataAccess
{
    public sealed class PoLocalStorage : IPoStorage
    {
        private const string BasePath = "/var/opt/po";
        private const string AccountName = "local";

        private readonly Uri baseUri;
        private readonly ILogger<PoLocalStorage> logger;

        public PoLocalStorage(
            IOptions<Options.Storage> options,
            ILogger<PoLocalStorage> logger)
        {
            this.baseUri = options.Value.BaseUri;
            this.logger = logger;
        }

        public async IAsyncEnumerable<Models.PoBlob> EnumerateAllBlobsAsync([EnumeratorCancellation] CancellationToken cancellationToken, bool goSlow = false)
        {
            uint countContainers = 0;
            uint countTotalBlobs = 0;

            if (!Directory.Exists(BasePath))
            {
                this.logger.LogWarning($"Base path {BasePath} does not exist");
                yield break;
            }

            foreach (string containerPath in Directory.EnumerateDirectories(BasePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                countContainers++;
                string containerName = Path.GetFileName(containerPath);

                await foreach (Models.PoBlob blob in this.EnumerateAllBlobsAsync(containerName, cancellationToken))
                {
                    if (goSlow)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    }

                    countTotalBlobs++;
                    yield return blob;
                }
            }

            this.logger.LogInformation($"Enumerated {countTotalBlobs} blobs in {countContainers} containers in {BasePath}");
        }

        public async IAsyncEnumerable<Models.PoBlob> EnumerateAllBlobsAsync(string containerName, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            uint countBlobs = 0;
            string containerPath = Path.Combine(BasePath, containerName);

            if (!Directory.Exists(containerPath))
            {
                this.logger.LogWarning($"Container path {containerPath} does not exist");
                yield break;
            }

            foreach (string filePath in Directory.EnumerateFiles(containerPath, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileInfo = new FileInfo(filePath);
                string relativePath = Path.GetRelativePath(containerPath, filePath).Replace(Path.DirectorySeparatorChar, '/');

                countBlobs++;
                yield return new Models.PoBlob
                {
                    AccountName = AccountName,
                    ContainerName = containerName,
                    Name = relativePath,
                    Category = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries).First(),
                    CreatedOn = fileInfo.CreationTimeUtc,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    LastSeen = DateTimeOffset.UtcNow,
                    ContentLength = fileInfo.Length,
                    ContentHash = await ComputeMd5HashAsync(filePath, cancellationToken)
                };
            }

            this.logger.LogInformation($"Enumerated {countBlobs} blobs in {containerPath}");
        }

        public Task<bool> ContainerExistsAsync(string containerName)
            => Task.FromResult(Directory.Exists(Path.Combine(BasePath, containerName)));

        public Uri GetReadOnlyUri(Models.PoBlob blob)
            => new(this.baseUri, $"{blob.ContainerName}/{blob.Name}");

        private static async Task<string> ComputeMd5HashAsync(string filePath, CancellationToken cancellationToken)
        {
            using var md5 = MD5.Create();
            using FileStream stream = File.OpenRead(filePath);
            byte[] hash = await md5.ComputeHashAsync(stream, cancellationToken);
            return Convert.ToHexStringLower(hash);
        }
    }
}
