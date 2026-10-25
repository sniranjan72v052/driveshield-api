using DriveShield.Api.Application;

namespace DriveShield.Api.Infrastructure;

/// <summary>
/// Keeps photos on the local disk, in App_Data/uploads next to the API project (change with Storage:LocalPath).
/// For development and single-server use only: files are lost if the server is rebuilt. Use blob storage in production.
/// </summary>
public class LocalPhotoStore : IPhotoStore
{
    private readonly string _root;

    public LocalPhotoStore(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["Storage:LocalPath"];
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "App_Data", "uploads")
            : configured);
        Directory.CreateDirectory(_root);
    }

    /// <summary>Keys are made by the server (caseId/photoId), but this still refuses anything that escapes the folder.</summary>
    private string PathFor(string key)
    {
        var full = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Invalid photo key.", nameof(key));
        return full;
    }

    public async Task SaveAsync(string key, Stream content, CancellationToken ct)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, ct);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (!File.Exists(path)) return Task.FromResult<Stream?>(null);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}
