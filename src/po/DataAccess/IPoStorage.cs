namespace po.DataAccess
{
    public interface IPoStorage
    {
        IAsyncEnumerable<Models.PoBlob> EnumerateAllBlobsAsync(CancellationToken cancellationToken, bool goSlow = false);

        IAsyncEnumerable<Models.PoBlob> EnumerateAllBlobsAsync(string containerName, CancellationToken cancellationToken);

        Task<bool> ContainerExistsAsync(string containerName);

        Uri GetReadOnlyUri(Models.PoBlob blob);
    }
}
