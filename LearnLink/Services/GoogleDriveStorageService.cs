using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace LearnLink.Services;

public class GoogleDriveOptions
{
    public string? ServiceAccountJson { get; set; }
    public string? SharedFolderId { get; set; }
}

public class GoogleDriveStorageService : IStorageService
{
    private readonly DriveService _driveService;
    private readonly string _sharedFolderId;
    private readonly ILogger<GoogleDriveStorageService> _logger;

    public GoogleDriveStorageService(IOptions<GoogleDriveOptions> options, ILogger<GoogleDriveStorageService> logger)
    {
        _logger = logger;
        _sharedFolderId = options.Value.SharedFolderId ?? throw new ArgumentNullException("GoogleDrive:SharedFolderId not configured");

        if (string.IsNullOrEmpty(options.Value.ServiceAccountJson))
            throw new ArgumentNullException("GoogleDrive:ServiceAccountJson not configured");

        try
        {
            var credential = GoogleCredential.FromJson(options.Value.ServiceAccountJson)
                .CreateScoped(DriveService.Scope.Drive);

            _driveService = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "LearnLink"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Google Drive service");
            throw;
        }
    }

    public async Task<StorageResult> UploadAsync(Stream stream, string fileName, string contentType)
    {
        try
        {
            var fileMetadata = new DriveFile()
            {
                Name = fileName,
                Parents = new List<string> { _sharedFolderId }
            };

            var request = _driveService.Files.Create(fileMetadata, stream, contentType);
            request.Fields = "id, name, mimeType, size, webViewLink, webContentLink, createdTime";

            var progress = await request.UploadAsync();
            if (progress.Status != Google.Apis.Upload.UploadStatus.Completed)
            {
                _logger.LogError("Upload incomplete: {Status}", progress.Status);
                return new StorageResult { Success = false, Message = "Upload failed" };
            }

            var file = request.ResponseBody;
            var fileSizeMB = file.Size.HasValue ? $"{(double)file.Size / (1024 * 1024):F1} MB" : "Unknown";

            try
            {
                var permission = new Permission()
                {
                    Type = "anyone",
                    Role = "reader"
                };
                await _driveService.Permissions.Create(permission, file.Id).ExecuteAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set public permission on file {FileId}", file.Id);
            }

            return new StorageResult
            {
                Success = true,
                FileId = file.Id,
                FileName = file.Name,
                ContentType = file.MimeType,
                FileSize = fileSizeMB,
                WebViewLink = file.WebViewLink,
                WebContentLink = file.WebContentLink
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file to Google Drive");
            return new StorageResult { Success = false, Message = ex.Message };
        }
    }

    public async Task<bool> DeleteAsync(string fileId)
    {
        try
        {
            await _driveService.Files.Delete(fileId).ExecuteAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file {FileId} from Google Drive", fileId);
            return false;
        }
    }

    public async Task<Stream?> DownloadAsync(string fileId)
    {
        try
        {
            var stream = new MemoryStream();
            var request = _driveService.Files.Get(fileId);
            await request.DownloadAsync(stream);
            stream.Position = 0;
            return stream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file {FileId} from Google Drive", fileId);
            return null;
        }
    }

    public string? ExtractFileId(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;

        // Match /d/{fileId}/ pattern from webViewLink
        var match = Regex.Match(filePath, @"/d/([a-zA-Z0-9_-]+)");
        if (match.Success) return match.Groups[1].Value;

        // Match ?id={fileId} pattern from webContentLink
        match = Regex.Match(filePath, @"[?&]id=([a-zA-Z0-9_-]+)");
        if (match.Success) return match.Groups[1].Value;

        return null;
    }

    public string GetPreviewUrl(string fileId, string fileFormat)
    {
        var fmt = fileFormat?.TrimStart('.').Trim().ToUpperInvariant() ?? "";
        if (fmt == "PDF")
        {
            return $"https://drive.google.com/file/d/{fileId}/preview";
        }
        // Office formats: use Google Docs Viewer
        return $"https://docs.google.com/gview?url=https://drive.google.com/uc?id={fileId}%26export=download&embedded=true";
    }

    public string GetDirectDownloadUrl(string fileId)
    {
        return $"https://drive.google.com/uc?id={fileId}&export=download";
    }
}
