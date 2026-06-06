namespace BannerShop.Core;

/// <summary>
/// Abstraction over persistent file storage for all banner-builder flows.
/// The default implementation is <c>LocalDiskFileStore</c> (BANNERSH-25).
/// A cloud swap (Azure Blob / S3) requires only a new class implementing this interface.
/// </summary>
public interface IFileStore
{
    /// <summary>
    /// Persist <paramref name="content"/> at <c>{subPath}/{fileName}</c> under the storage root
    /// and return a <see cref="StoredFile"/> describing where it lives.
    /// </summary>
    /// <param name="content">The byte stream to store. Must be readable.</param>
    /// <param name="contentType">MIME type (e.g. "image/jpeg").</param>
    /// <param name="subPath">
    /// Relative sub-directory under the root, e.g. <c>banner-builder/42</c> or
    /// <c>design-requests/7</c>. Must not contain <c>..</c> or be rooted.
    /// </param>
    /// <param name="fileName">Leaf file name including extension, e.g. <c>abc123.jpg</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<StoredFile> SaveAsync(
        Stream content,
        string contentType,
        string subPath,
        string fileName,
        CancellationToken ct = default);

    /// <summary>Open a stored file for reading by its <see cref="StoredFile.StoragePath"/>.</summary>
    Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct = default);

    /// <summary>
    /// Delete a stored file.  Returns <c>true</c> if the file existed and was deleted,
    /// <c>false</c> if it was already absent.
    /// </summary>
    Task<bool> DeleteAsync(string storagePath, CancellationToken ct = default);

    /// <summary>
    /// Derive the public URL from which a stored file is served via StaticFiles middleware.
    /// Banner-builder uploads use unguessable GUIDs so any bearer of the URL may access it.
    /// Design-request previews should be served via an authorized controller instead.
    /// </summary>
    string GetPublicUrl(string storagePath);
}

/// <summary>Metadata returned by <see cref="IFileStore.SaveAsync"/>.</summary>
public record StoredFile(
    /// <summary>Relative path within the store (suitable for DB persistence).</summary>
    string StoragePath,
    /// <summary>Public URL via StaticFiles (or controller proxy for protected files).</summary>
    string PublicUrl,
    /// <summary>Byte count of the stored file.</summary>
    long SizeBytes);
