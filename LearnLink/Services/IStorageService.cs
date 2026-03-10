namespace LearnLink.Services;

public class StorageResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? FileId { get; set; }
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public string? FileSize { get; set; }
    public string? WebViewLink { get; set; }
    public string? WebContentLink { get; set; }
    public string? ThumbnailUrl { get; set; }
}

public interface IStorageService
{
    Task<StorageResult> UploadAsync(Stream stream, string fileName, string contentType);
    Task<bool> DeleteAsync(string fileId);
    Task<Stream?> DownloadAsync(string fileId);
    string? ExtractFileId(string filePath);
    string GetPreviewUrl(string fileId, string fileFormat);
    string GetDirectDownloadUrl(string fileId);
}
