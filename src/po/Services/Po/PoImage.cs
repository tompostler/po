namespace po.Services.Po
{
    /// <summary>
    /// An unseen image picked for display, independent of the chat platform that will show it.
    /// </summary>
    public sealed record PoImage(Models.PoBlob Blob, string Title, string Description, Uri ImageUrl);
}
