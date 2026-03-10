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
    private readonly DriveService? _driveService;
    private readonly string _sharedFolderId;
    private readonly ILogger<GoogleDriveStorageService> _logger;

    public GoogleDriveStorageService(IOptions<GoogleDriveOptions> options, ILogger<GoogleDriveStorageService> logger)
    {
        _logger = logger;
        _sharedFolderId = options.Value.SharedFolderId ?? "";

        if (string.IsNullOrEmpty(options.Value.ServiceAccountJson))
        {
            _logger.LogWarning("GoogleDrive:ServiceAccountJson not configured. Google Drive uploads will fail.");
            return;
        }

        try
        {
#pragma warning disable CS0618 // Type or member is obsolete
            var credential = GoogleCredential.FromJson(options.Value.ServiceAccountJson)
                .CreateScoped(DriveService.Scope.Drive);
#pragma warning restore CS0618 // Type or member is obsolete

            _driveService = new DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "LearnLink"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Google Drive service. Ensure credentials are valid JSON.");
            // Do not throw, so the application continues to run
        }
    }

    public async Task<StorageResult> UploadAsync(Stream stream, string fileName, string contentType)
    {
        if (_driveService == null)
            return new StorageResult { Success = false, Message = "Google Drive is not configured correctly." };

        try
        {
            var fileMetadata = new DriveFile()
            {
                Name = fileName,
                Parents = new List<string> { _sharedFolderId }
            };

            var request = _driveService.Files.Create(fileMetadata, stream, contentType);
            request.SupportsAllDrives = true; // Fix for storage quota error
            request.Fields = "id, name, mimeType, size, webViewLink, webContentLink, createdTime";

            var progress = await request.UploadAsync();
            if (progress.Status != Google.Apis.Upload.UploadStatus.Completed)
            {
                var apiError = progress.Exception?.Message ?? "Unknown Google Drive API error";
                _logger.LogError(progress.Exception, "Upload incomplete: {Status}. Details: {Error}", progress.Status, apiError);
                return new StorageResult { Success = false, Message = $"Google Drive Error: {apiError}" };
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
                var permissionRequest = _driveService.Permissions.Create(permission, file.Id);
                permissionRequest.SupportsAllDrives = true;
                await permissionRequest.ExecuteAsync();
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
                WebContentLink = file.WebContentLink,
                ThumbnailUrl = $"https://drive.google.com/thumbnail?id={file.Id}&sz=w800"
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
        if (_driveService == null) return false;

        try
        {
            var deleteRequest = _driveService.Files.Delete(fileId);
            deleteRequest.SupportsAllDrives = true;
            await deleteRequest.ExecuteAsync();
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
        if (_driveService == null) return null;

        try
        {
            var stream = new MemoryStream();
            var downloadRequest = _driveService.Files.Get(fileId);
            downloadRequest.SupportsAllDrives = true;
            await downloadRequest.DownloadAsync(stream);
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
        // For Google Drive files, the native preview iframe is the ONLY reliable method that works consistently.
        // Google Docs Viewer (gview) often returns blank white pages.
        return $"https://drive.google.com/file/d/{fileId}/preview";
    }

    public string GetDirectDownloadUrl(string fileId)
    {
        return $"https://drive.google.com/uc?id={fileId}&export=download";
    }
}
