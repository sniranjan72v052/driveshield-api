namespace DriveShield.Api.Application;

/// <summary>
/// Where uploaded photo files are kept. The database only stores each photo's key.
/// Development uses the local disk (LocalPhotoStore); production should use Azure Blob Storage:
/// write an AzureBlobPhotoStore that implements this interface and register it in Program.cs instead.
/// </summary>
public interface IPhotoStore
{
    Task SaveAsync(string key, Stream content, CancellationToken ct);

    /// <summary>Returns null when the photo does not exist. The caller disposes the stream.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct);

    Task DeleteAsync(string key, CancellationToken ct);
}
