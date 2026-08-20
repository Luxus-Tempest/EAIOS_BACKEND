using EAIOS.Api.Application.Common.Interfaces;

namespace EAIOS.Api.Infrastructure.Storage;

// ── Interface ─────────────────────────────────────────────────────────────────

public interface IStorageService
{
    Task<StorageUploadResult> UploadAsync(Stream content, string fileName, string contentType, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Ouvre le contenu d'un objet en lecture, ou <c>null</c> si la clé n'existe pas.
    /// Indispensable au streaming des téléchargements par l'API (le presigned URL ne
    /// convient pas quand la réponse doit transiter par le contrôle d'accès applicatif).
    /// L'appelant est responsable de disposer le flux retourné.
    /// </summary>
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct = default);

    Task<string> GetDownloadUrlAsync(string storageKey, TimeSpan? expiry = null, CancellationToken ct = default);
    Task<string> GetPresignedUploadUrlAsync(string fileName, string contentType, string tenantId, TimeSpan? expiry = null, CancellationToken ct = default);
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

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(storageKey);
        if (fullPath is null || !File.Exists(fullPath))
            return Task.FromResult<Stream?>(null);

        return Task.FromResult<Stream?>(
            new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));
    }

    public Task<string> GetDownloadUrlAsync(string storageKey, TimeSpan? expiry = null, CancellationToken ct = default) =>
        Task.FromResult($"/api/v1/resources/download/{Uri.EscapeDataString(storageKey)}");

    public Task<string> GetPresignedUploadUrlAsync(string fileName, string contentType, string tenantId, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var key = $"{tenantId}/{Guid.CreateVersion7():N}{ext}";
        return Task.FromResult($"/api/v1/uploads/direct?key={Uri.EscapeDataString(key)}");
    }

    public Task<string?> GetPreviewUrlAsync(string storageKey, CancellationToken ct = default) =>
        Task.FromResult<string?>($"/api/v1/resources/preview/{Uri.EscapeDataString(storageKey)}");

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(storageKey);
        if (fullPath is not null && File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(storageKey);
        return Task.FromResult(fullPath is not null && File.Exists(fullPath));
    }

    // ── Upload multipart ──────────────────────────────────────────────────────
    // Chaque part est écrite dans un répertoire de travail dédié à l'uploadId,
    // puis les parts sont concaténées dans l'ordre à la finalisation.

    public Task<MultipartSession> InitiateMultipartAsync(string fileName, long totalSizeBytes, string contentType, string tenantId, CancellationToken ct = default)
    {
        var uploadId = Guid.CreateVersion7().ToString("N");
        var ext      = Path.GetExtension(fileName).ToLowerInvariant();
        var key      = $"{tenantId}/{uploadId}{ext}";

        var stagingDir = StagingDir(uploadId);
        Directory.CreateDirectory(stagingDir);

        // Le manifeste conserve les métadonnées nécessaires à la finalisation.
        var manifest = new MultipartManifest(uploadId, key, fileName, contentType, tenantId);
        File.WriteAllText(Path.Combine(stagingDir, "manifest.json"),
            System.Text.Json.JsonSerializer.Serialize(manifest));

        logger.LogInformation("Upload multipart initié: {UploadId} -> {Key}", uploadId, key);
        return Task.FromResult(new MultipartSession(uploadId, key, DateTime.UtcNow.AddHours(24)));
    }

    public async Task UploadPartAsync(string uploadId, int partNumber, Stream data, CancellationToken ct = default)
    {
        if (partNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(partNumber), "Le numéro de part commence à 1.");

        var stagingDir = StagingDir(uploadId);
        if (!Directory.Exists(stagingDir))
            throw new KeyNotFoundException($"Session d'upload inconnue : {uploadId}");

        var partPath = Path.Combine(stagingDir, $"part-{partNumber:D6}.bin");
        await using var fs = File.Create(partPath);
        await data.CopyToAsync(fs, ct);
    }

    public async Task<StorageUploadResult> CompleteMultipartAsync(string uploadId, string tenantId, CancellationToken ct = default)
    {
        var stagingDir = StagingDir(uploadId);
        var manifestPath = Path.Combine(stagingDir, "manifest.json");
        if (!File.Exists(manifestPath))
            throw new KeyNotFoundException($"Session d'upload inconnue : {uploadId}");

        var manifest = System.Text.Json.JsonSerializer.Deserialize<MultipartManifest>(
            await File.ReadAllTextAsync(manifestPath, ct))
            ?? throw new InvalidOperationException("Manifeste d'upload illisible.");

        var parts = Directory.GetFiles(stagingDir, "part-*.bin").OrderBy(p => p, StringComparer.Ordinal).ToList();
        if (parts.Count == 0)
            throw new InvalidOperationException("Aucune part reçue pour cet upload.");

        var finalPath = Path.Combine(_basePath, manifest.StorageKey.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        await using (var output = File.Create(finalPath))
        await using (var cs = new System.Security.Cryptography.CryptoStream(
                         output, sha256, System.Security.Cryptography.CryptoStreamMode.Write))
        {
            foreach (var part in parts)
            {
                await using var input = File.OpenRead(part);
                await input.CopyToAsync(cs, ct);
            }
            cs.FlushFinalBlock();
        }

        var checksum = Convert.ToHexStringLower(sha256.Hash!);
        var size     = new FileInfo(finalPath).Length;

        Directory.Delete(stagingDir, recursive: true);

        logger.LogInformation("Upload multipart finalisé: {UploadId} -> {Key} ({Size} octets, {Parts} parts)",
            uploadId, manifest.StorageKey, size, parts.Count);

        return new StorageUploadResult(manifest.StorageKey, manifest.OriginalFileName, size, manifest.ContentType, checksum);
    }

    public Task AbortMultipartAsync(string uploadId, CancellationToken ct = default)
    {
        var stagingDir = StagingDir(uploadId);
        if (Directory.Exists(stagingDir))
        {
            Directory.Delete(stagingDir, recursive: true);
            logger.LogInformation("Upload multipart abandonné: {UploadId}", uploadId);
        }
        return Task.CompletedTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string StagingDir(string uploadId)
    {
        // uploadId provient du client : on n'autorise que de l'hexadécimal pour
        // qu'il ne puisse jamais s'échapper du répertoire de travail.
        if (string.IsNullOrWhiteSpace(uploadId) || !uploadId.All(Uri.IsHexDigit))
            throw new ArgumentException("Identifiant d'upload invalide.", nameof(uploadId));

        return Path.Combine(_basePath, ".multipart", uploadId);
    }

    /// <summary>
    /// Résout une clé de stockage en chemin absolu, en refusant tout chemin qui
    /// sortirait du répertoire de base (traversée par <c>..</c> ou chemin absolu).
    /// </summary>
    private string? ResolvePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey)) return null;

        var combined = Path.GetFullPath(
            Path.Combine(_basePath, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(_basePath);

        return combined.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? combined
            : null;
    }

    private sealed record MultipartManifest(
        string UploadId,
        string StorageKey,
        string OriginalFileName,
        string ContentType,
        string TenantId);
}
