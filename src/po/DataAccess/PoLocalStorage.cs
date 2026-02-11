using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace po.DataAccess
{
    public sealed class PoLocalStorage : IPoStorage
    {
        private const string BasePath = "/var/opt/po";
        private const string AccountName = "local";

        private readonly string apiKey;
        private readonly Uri baseUri;
        private readonly ILogger<PoLocalStorage> logger;

        public PoLocalStorage(
            IOptions<Options.Api> apiOptions,
            IOptions<Options.Storage> storageOptions,
            ILogger<PoLocalStorage> logger)
        {
            this.apiKey = apiOptions.Value.ApiKey;
            this.baseUri = storageOptions.Value.BaseUri;
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

            DeleteEmptyDirectories(BasePath, this.logger);

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

            DeleteEmptyDirectories(containerPath, this.logger);

            this.logger.LogInformation($"Enumerated {countBlobs} blobs in {containerPath}");
        }

        public Task<bool> ContainerExistsAsync(string containerName)
            => Task.FromResult(Directory.Exists(Path.Combine(BasePath, containerName)));

        public Uri GetReadUriExpiresInOneDay(Models.PoBlob blob)
        {
            long expires = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();
            string signature = Utilities.SignedUrls.GenerateSignature(this.apiKey, blob.ContainerName, blob.Name, expires, this.logger);
            return new Uri(this.baseUri, $"blob/{blob.ContainerName}/{blob.Name}?expires={expires}&sig={signature}");
        }

        public async Task<Models.PoBlob> UploadBlobAsync(string containerName, string blobName, Stream content, CancellationToken cancellationToken)
        {
            string containerPath = Path.Combine(BasePath, containerName);
            _ = Directory.CreateDirectory(containerPath);

            string filePath = Path.Combine(containerPath, blobName.Replace('/', Path.DirectorySeparatorChar));
            string directoryPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                _ = Directory.CreateDirectory(directoryPath);
            }

            using (FileStream fileStream = File.Create(filePath))
            {
                await content.CopyToAsync(fileStream, cancellationToken);
            }

            var fileInfo = new FileInfo(filePath);

            return new Models.PoBlob
            {
                AccountName = AccountName,
                ContainerName = containerName,
                Name = blobName,
                Category = blobName.Split('/', StringSplitOptions.RemoveEmptyEntries).First(),
                CreatedOn = fileInfo.CreationTimeUtc,
                LastModified = fileInfo.LastWriteTimeUtc,
                LastSeen = DateTimeOffset.UtcNow,
                ContentLength = fileInfo.Length,
                ContentHash = await ComputeMd5HashAsync(filePath, cancellationToken)
            };
        }

        public Task<Stream> DownloadBlobIfExistsAsync(string containerName, string blobName, CancellationToken cancellationToken)
        {
            string filePath = Path.Combine(BasePath, containerName, blobName.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(filePath))
            {
                return Task.FromResult<Stream>(null);
            }
            return Task.FromResult<Stream>(File.OpenRead(filePath));
        }

        public Task<bool> DeleteBlobIfExistsAsync(string containerName, string blobName, CancellationToken cancellationToken)
        {
            string filePath = Path.Combine(BasePath, containerName, blobName.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(filePath))
            {
                return Task.FromResult(false);
            }
            File.Delete(filePath);
            return Task.FromResult(true);
        }

        private static void DeleteEmptyDirectories(string path, ILogger logger)
        {
            foreach (string directory in Directory.EnumerateDirectories(path))
            {
                DeleteEmptyDirectories(directory, logger);
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                    logger.LogInformation($"Deleted empty directory: {directory}");
                }
            }
        }

        private static async Task<string> ComputeMd5HashAsync(string filePath, CancellationToken cancellationToken)
        {
            using var md5 = MD5.Create();
            using FileStream stream = File.OpenRead(filePath);
            byte[] hash = await md5.ComputeHashAsync(stream, cancellationToken);
            return Convert.ToHexStringLower(hash);
        }
    }
}
