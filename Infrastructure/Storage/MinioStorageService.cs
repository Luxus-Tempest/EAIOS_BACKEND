using Amazon.S3;
using Amazon.S3.Model;

namespace EAIOS.Api.Infrastructure.Storage;

/// <summary>
/// Implémentation du service de stockage utilisant MinIO / AWS S3 SDK.
/// </summary>
public sealed class MinioStorageService(
    IAmazonS3 s3Client,
    IConfiguration config,
    ILogger<MinioStorageService> logger) : IStorageService
{
    private readonly string _bucketName = config["Storage:S3:BucketName"] ?? "eaios-uploads";

    public async Task<StorageUploadResult> UploadAsync(Stream content, string fileName, string contentType, string tenantId, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var key = $"{tenantId}/{Guid.CreateVersion7():N}{ext}";

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        ms.Position = 0;
        var hashBytes = sha256.ComputeHash(ms);
        var checksum = Convert.ToHexStringLower(hashBytes);
        ms.Position = 0;

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = ms,
            ContentType = contentType,
            AutoCloseStream = false
        };

        await s3Client.PutObjectAsync(request, ct);
        logger.LogInformation("Stocké dans MinIO S3: {Key} ({Size} octets)", key, ms.Length);

        return new StorageUploadResult(key, fileName, ms.Length, contentType, checksum);
    }

    public async Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        try
        {
            var response = await s3Client.GetObjectAsync(_bucketName, storageKey, ct);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task<string> GetDownloadUrlAsync(string storageKey, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = storageKey,
            Expires = DateTime.UtcNow.Add(expiry ?? TimeSpan.FromHours(1)),
            Verb = HttpVerb.GET
        };

        var url = s3Client.GetPreSignedURL(request);
        return Task.FromResult(url);
    }

    public Task<string> GetPresignedUploadUrlAsync(string fileName, string contentType, string tenantId, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var key = $"{tenantId}/{Guid.CreateVersion7():N}{ext}";

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = key,
            Expires = DateTime.UtcNow.Add(expiry ?? TimeSpan.FromMinutes(15)),
            Verb = HttpVerb.PUT,
            ContentType = contentType
        };

        var url = s3Client.GetPreSignedURL(request);
        return Task.FromResult(url);
    }

    public Task<string?> GetPreviewUrlAsync(string storageKey, CancellationToken ct = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = storageKey,
            Expires = DateTime.UtcNow.AddHours(1),
            Verb = HttpVerb.GET
        };
        request.ResponseHeaderOverrides.ContentDisposition = "inline";

        var url = s3Client.GetPreSignedURL(request);
        return Task.FromResult<string?>(url);
    }

    public async Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var request = new DeleteObjectRequest
        {
            BucketName = _bucketName,
            Key = storageKey
        };
        await s3Client.DeleteObjectAsync(request, ct);
        logger.LogInformation("Fichier supprimé de MinIO S3: {Key}", storageKey);
    }

    public async Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
    {
        try
        {
            await s3Client.GetObjectMetadataAsync(_bucketName, storageKey, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    // ── Upload multipart S3 ───────────────────────────────────────────────────
    // S3 exige de rappeler (key, uploadId) et la liste des ETags à la finalisation.
    // On mémorise donc la session côté serveur, indexée par uploadId.

    private sealed class MultipartState
    {
        public required string Key { get; init; }
        public required string OriginalFileName { get; init; }
        public required string ContentType { get; init; }
        public List<PartETag> Parts { get; } = [];
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, MultipartState> Sessions = new();

    public async Task<MultipartSession> InitiateMultipartAsync(string fileName, long totalSizeBytes, string contentType, string tenantId, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var key = $"{tenantId}/{Guid.CreateVersion7():N}{ext}";

        var request = new InitiateMultipartUploadRequest
        {
            BucketName  = _bucketName,
            Key         = key,
            ContentType = contentType
        };

        var response = await s3Client.InitiateMultipartUploadAsync(request, ct);

        Sessions[response.UploadId] = new MultipartState
        {
            Key              = key,
            OriginalFileName = fileName,
            ContentType      = contentType
        };

        logger.LogInformation("Upload multipart S3 initié: {UploadId} -> {Key}", response.UploadId, key);
        return new MultipartSession(response.UploadId, key, DateTime.UtcNow.AddHours(24));
    }

    public async Task UploadPartAsync(string uploadId, int partNumber, Stream data, CancellationToken ct = default)
    {
        if (!Sessions.TryGetValue(uploadId, out var state))
            throw new KeyNotFoundException($"Session d'upload inconnue : {uploadId}");

        var response = await s3Client.UploadPartAsync(new UploadPartRequest
        {
            BucketName  = _bucketName,
            Key         = state.Key,
            UploadId    = uploadId,
            PartNumber  = partNumber,
            InputStream = data
        }, ct);

        lock (state.Parts)
        {
            // Un renvoi de la même part remplace la précédente plutôt que de la dupliquer.
            state.Parts.RemoveAll(p => p.PartNumber == partNumber);
            state.Parts.Add(new PartETag(partNumber, response.ETag));
        }
    }

    public async Task<StorageUploadResult> CompleteMultipartAsync(string uploadId, string tenantId, CancellationToken ct = default)
    {
        if (!Sessions.TryGetValue(uploadId, out var state))
            throw new KeyNotFoundException($"Session d'upload inconnue : {uploadId}");

        List<PartETag> parts;
        lock (state.Parts)
            parts = state.Parts.OrderBy(p => p.PartNumber).ToList();

        if (parts.Count == 0)
            throw new InvalidOperationException("Aucune part reçue pour cet upload.");

        await s3Client.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
        {
            BucketName = _bucketName,
            Key        = state.Key,
            UploadId   = uploadId,
            PartETags  = parts
        }, ct);

        Sessions.TryRemove(uploadId, out _);

        var metadata = await s3Client.GetObjectMetadataAsync(_bucketName, state.Key, ct);

        logger.LogInformation("Upload multipart S3 finalisé: {UploadId} -> {Key} ({Size} octets, {Parts} parts)",
            uploadId, state.Key, metadata.ContentLength, parts.Count);

        return new StorageUploadResult(
            state.Key,
            state.OriginalFileName,
            metadata.ContentLength,
            state.ContentType,
            metadata.ETag?.Trim('"'));
    }

    public async Task AbortMultipartAsync(string uploadId, CancellationToken ct = default)
    {
        if (!Sessions.TryRemove(uploadId, out var state))
            return;

        await s3Client.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
        {
            BucketName = _bucketName,
            Key        = state.Key,
            UploadId   = uploadId
        }, ct);

        logger.LogInformation("Upload multipart S3 abandonné: {UploadId}", uploadId);
    }
}
