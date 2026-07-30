using EAIOS.Api.Application.Common.Interfaces;

namespace EAIOS.Api.Infrastructure.Storage;

// ── Interface ─────────────────────────────────────────────────────────────────

public interface IStorageService
{
    Task<StorageUploadResult> UploadAsync(Stream content, string fileName, string contentType, string tenantId, CancellationToken ct = default);
    Task<string> GetDownloadUrlAsync(string storageKey, TimeSpan? expiry = null, CancellationToken ct = default);
    Task<string?> GetPreviewUrlAsync(string storageKey, CancellationToken ct = default);
    Task DeleteAsync(string storageKey, CancellationToken ct = default);
    Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default);
    Task<MultipartSession> InitiateMultipartAsync(string fileName, long totalSizeBytes, string contentType, string tenantId, CancellationToken ct = default);
    Task UploadPartAsync(string uploadId, int partNumber, Stream data, CancellationToken ct = default);
    Task<StorageUploadResult> CompleteMultipartAsync(string uploadId, string tenantId, CancellationToken ct = default);
    Task AbortMultipartAsync(string uploadId, CancellationToken ct = default);
}

public sealed record StorageUploadResult(
    string StorageKey,
    string OriginalFileName,
    long   FileSizeBytes,
    string MimeType,
    string? Checksum);

public sealed record MultipartSession(
    string   UploadId,
    string   StorageKey,
    DateTime ExpiresAt,
    int      ChunkSizeBytes = 5_242_880); // 5 MiB

// ── Local (dev) implementation ────────────────────────────────────────────────

public sealed class LocalStorageService(IConfiguration config, ILogger<LocalStorageService> logger)
    : IStorageService
{
    private readonly string _basePath = config["Storage:LocalBasePath"] ?? Path.Combine(AppContext.BaseDirectory, "uploads");

    public async Task<StorageUploadResult> UploadAsync(Stream content, string fileName, string contentType, string tenantId, CancellationToken ct = default)
    {
        var tenantDir = Path.Combine(_basePath, tenantId);
        Directory.CreateDirectory(tenantDir);

        var ext     = Path.GetExtension(fileName).ToLowerInvariant();
        var key     = $"{tenantId}/{Guid.CreateVersion7():N}{ext}";
        var fullPath = Path.Combine(_basePath, key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        await using var fileStream = File.Create(fullPath);
        await using var cs = new System.Security.Cryptography.CryptoStream(fileStream, sha256, System.Security.Cryptography.CryptoStreamMode.Write);
        await content.CopyToAsync(cs, ct);
        cs.FlushFinalBlock();

        var checksum = Convert.ToHexStringLower(sha256.Hash!);
        var size     = new FileInfo(fullPath).Length;

        logger.LogInformation("Stored file: {Key} ({Size} bytes)", key, size);
        return new StorageUploadResult(key, fileName, size, contentType, checksum);
    }

    public Task<string> GetDownloadUrlAsync(string storageKey, TimeSpan? expiry = null, CancellationToken ct = default) =>
        Task.FromResult($"/api/v1/resources/download/{Uri.EscapeDataString(storageKey)}");

    public Task<string?> GetPreviewUrlAsync(string storageKey, CancellationToken ct = default) =>
        Task.FromResult<string?>($"/api/v1/resources/preview/{Uri.EscapeDataString(storageKey)}");

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_basePath, storageKey.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_basePath, storageKey.Replace('/', Path.DirectorySeparatorChar));
        return Task.FromResult(File.Exists(fullPath));
    }

    public Task<MultipartSession> InitiateMultipartAsync(string fileName, long totalSizeBytes, string contentType, string tenantId, CancellationToken ct = default)
    {
        var uploadId = Guid.CreateVersion7().ToString("N");
        var ext      = Path.GetExtension(fileName).ToLowerInvariant();
        var key      = $"{tenantId}/multipart/{uploadId}{ext}";
        return Task.FromResult(new MultipartSession(uploadId, key, DateTime.UtcNow.AddHours(24)));
    }

    public Task UploadPartAsync(string uploadId, int partNumber, Stream data, CancellationToken ct = default) =>
        Task.CompletedTask; // Simplifié pour dev

    public Task<StorageUploadResult> CompleteMultipartAsync(string uploadId, string tenantId, CancellationToken ct = default) =>
        Task.FromResult(new StorageUploadResult($"{tenantId}/multipart/{uploadId}", "upload", 0, "application/octet-stream", null));

    public Task AbortMultipartAsync(string uploadId, CancellationToken ct = default) => Task.CompletedTask;
}
