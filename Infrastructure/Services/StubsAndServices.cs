using EAIOS.Api.Application.Common.Interfaces;

namespace EAIOS.Api.Infrastructure.Storage;

public sealed class LocalStorageService(IConfiguration config) : IStorageService
{
    private readonly string _basePath = config["Storage:LocalBasePath"] ?? "uploads";

    public async Task<StorageUploadResult> UploadAsync(Stream content, string fileName, string contentType, string organizationId, CancellationToken ct = default)
    {
        var targetDir = Path.Combine(_basePath, organizationId);
        Directory.CreateDirectory(targetDir);

        var fileExt = Path.GetExtension(fileName);
        var key = $"{organizationId}/{Guid.CreateVersion7():N}{fileExt}";
        var fullPath = Path.Combine(_basePath, key);

        using (var fileStream = File.Create(fullPath))
        {
            await content.CopyToAsync(fileStream, ct);
        }

        var fileInfo = new FileInfo(fullPath);
        return new StorageUploadResult(key, fileName, fileInfo.Length, contentType, null);
    }

    public Task<string> GetDownloadUrlAsync(string storageKey, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        return Task.FromResult($"/uploads/{storageKey}");
    }

    public Task<string?> GetPreviewUrlAsync(string storageKey, CancellationToken ct = default)
    {
        return Task.FromResult<string?>($"/uploads/{storageKey}");
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_basePath, storageKey);
        if (File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
    {
        var fullPath = Path.Combine(_basePath, storageKey);
        return Task.FromResult(File.Exists(fullPath));
    }

    public Task<MultipartSession> InitiateMultipartAsync(string fileName, long totalSizeBytes, string contentType, string organizationId, CancellationToken ct = default)
    {
        var uploadId = Guid.CreateVersion7().ToString("N");
        var key = $"{organizationId}/multipart/{uploadId}_{fileName}";
        return Task.FromResult(new MultipartSession(uploadId, key, DateTime.UtcNow.AddHours(24)));
    }

    public Task UploadChunkAsync(string uploadId, int chunkIndex, Stream data, CancellationToken ct = default) => Task.CompletedTask;
    public Task<StorageUploadResult> CompleteMultipartAsync(string uploadId, CancellationToken ct = default) =>
        Task.FromResult(new StorageUploadResult(uploadId, "file.tmp", 0, "application/octet-stream", null));
    public Task AbortMultipartAsync(string uploadId, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class StubLlmService : ILlmService
{
    public Task<LlmGenerateResult> GenerateAsync(string systemPrompt, string userInput, string model = "gpt-4o", float temperature = 0.7F, CancellationToken ct = default)
    {
        var reply = $"[EAIOS AI Stub Output for: '{userInput}']\nThis is a mock response from the LLM service stub.";
        return Task.FromResult(new LlmGenerateResult(reply, 50, 120, model, 0.002m));
    }

    public async IAsyncEnumerable<string> StreamAsync(string systemPrompt, string userInput, string model = "gpt-4o", float temperature = 0.7F, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return "This ";
        yield return "is ";
        yield return "a ";
        yield return "streamed ";
        yield return "response.";
        await Task.CompletedTask;
    }

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        var vector = new float[1536];
        Array.Fill(vector, 0.01f);
        return Task.FromResult(vector);
    }
}

public sealed class InMemoryNotificationService : INotificationService
{
    public Task SendAsync(Guid organizationId, Guid recipientId, string type, string title, string? body = null, string? actionUrl = null, string? actionLabel = null, string? dataJson = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task SendBulkAsync(Guid organizationId, IEnumerable<Guid> recipientIds, string type, string title, string? body = null, CancellationToken ct = default) => Task.CompletedTask;
}
