using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.IO;

namespace LearnLink.Services;

public class LocalStorageService : IStorageService
{
    private readonly IWebHostEnvironment _env;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<LocalStorageService> _logger;
    private const string UploadFolder = "uploads";

    public LocalStorageService(IWebHostEnvironment env, IHttpContextAccessor httpContextAccessor, ILogger<LocalStorageService> logger)
    {
        _env = env;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;

        var path = Path.Combine(_env.WebRootPath, UploadFolder);
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    public async Task<StorageResult> UploadAsync(Stream stream, string fileName, string contentType)
    {
        try
        {
            var fileId = Guid.NewGuid().ToString();
            var extension = Path.GetExtension(fileName);
            var uniqueFileName = $"{fileId}{extension}";
            var filePath = Path.Combine(_env.WebRootPath, UploadFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await stream.CopyToAsync(fileStream);
            }

            var request = _httpContextAccessor.HttpContext?.Request;
            var baseUrl = $"{request?.Scheme}://{request?.Host}";
            return new StorageResult
            {
                Success = true,
                FileId = uniqueFileName,
                FileName = fileName,
                ContentType = contentType,
                FileSize = $"{(double)stream.Length / (1024 * 1024):F1} MB",
                WebViewLink = uniqueFileName,
                WebContentLink = uniqueFileName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file to local storage");
            return new StorageResult { Success = false, Message = ex.Message };
        }
    }

    public async Task<bool> DeleteAsync(string fileId)
    {
        try
        {
            var filePath = Path.Combine(_env.WebRootPath, UploadFolder, fileId);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file {FileId} from local storage", fileId);
            return false;
        }
    }

    public async Task<Stream?> DownloadAsync(string fileId)
    {
        try
        {
            var filePath = Path.Combine(_env.WebRootPath, UploadFolder, fileId);
            if (File.Exists(filePath))
            {
                return new FileStream(filePath, FileMode.Open, FileAccess.Read);
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file {FileId} from local storage", fileId);
            return null;
        }
    }

    public string? ExtractFileId(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;
        return Path.GetFileName(filePath);
    }

    public string GetPreviewUrl(string fileId, string fileFormat)
    {
        return $"/{UploadFolder}/{fileId}";
    }

    public string GetDirectDownloadUrl(string fileId)
    {
        return $"/{UploadFolder}/{fileId}";
    }
}
