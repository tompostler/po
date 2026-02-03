using Microsoft.Extensions.Options;

namespace po.DataAccess
{
    public sealed class PoLocalStorage : IPoStorage
    {
        private readonly Uri baseUri;
        private readonly ILogger<PoLocalStorage> logger;

        public PoLocalStorage(
            IOptions<Options.Storage> options,
            ILogger<PoLocalStorage> logger)
        {
            this.baseUri = options.Value.BaseUri;
            this.logger = logger;
        }

        public IAsyncEnumerable<Models.PoBlob> EnumerateAllBlobsAsync(CancellationToken cancellationToken, bool goSlow = false)
            => throw new NotImplementedException();

        public IAsyncEnumerable<Models.PoBlob> EnumerateAllBlobsAsync(string containerName, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<bool> ContainerExistsAsync(string containerName)
            => throw new NotImplementedException();

        public Uri GetReadOnlyUri(Models.PoBlob blob)
            => throw new NotImplementedException();
    }
}
