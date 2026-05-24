using System.IO.Compression;
using System.Text.Json;
using LearnLink.Data;
using LearnLink.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LearnLink.Services
{
    public interface IBackupService
    {
        Task<int> InitiateBackupAsync(string triggerUserId, List<string> selectedRepositories, List<int>? selectedResourceIds = null, List<string>? selectedUserIds = null, string backupType = "Manual");
        Task<int> InitiateRestoreAsync(int backupRecordId, string triggerUserId);
        Task<BackupMetricsDto> CalculateStorageMetricsAsync();
    }

    public class BackupMetricsDto
    {
        public double DatabaseSizeMb { get; set; }
        public double UploadsSizeMb { get; set; }
        public double TotalSizeMb => DatabaseSizeMb + UploadsSizeMb;
        public Dictionary<string, double> RepositorySizes { get; set; } = new();
        public Dictionary<string, int> RepositoryCounts { get; set; } = new();
    }

    public class BackupService : IBackupService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<BackupService> _logger;

        private static bool IsMissingTableException(SqlException ex)
            => ex.Message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase);

        public BackupService(IServiceScopeFactory scopeFactory, IWebHostEnvironment env, ILogger<BackupService> logger)
        {
            _scopeFactory = scopeFactory;
            _env = env;
            _logger = logger;
        }

        /// <summary>
        /// Creates a new BackupRecord, saves it to the DB, and spawns a background
        /// task that will actually collect the data and create the archive.
        /// </summary>
        public async Task<int> InitiateBackupAsync(string triggerUserId, List<string> selectedRepositories, List<int>? selectedResourceIds = null, List<string>? selectedUserIds = null, string backupType = "Manual")
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Calculate metrics with the SAME dbContext (avoids nested scope issues)
            var metrics = await CalculateStorageMetricsInternalAsync(dbContext);

            var backupRecord = new BackupRecord
            {
                BackupType = backupType,
                Status = "In Progress",
                CreatedAt = DateTime.UtcNow,
                TriggeredByUserId = triggerUserId,
                TotalSizeMb = metrics.TotalSizeMb,
                ProgressPercent = 0
            };

            dbContext.BackupRecords.Add(backupRecord);
            await dbContext.SaveChangesAsync();

            _logger.LogInformation("BackupRecord Id={BackupId} created successfully for user {UserId}", backupRecord.Id, triggerUserId);

            foreach (var repo in selectedRepositories)
            {
                dbContext.BackupItems.Add(new BackupItem
                {
                    BackupRecordId = backupRecord.Id,
                    RepositoryName = repo,
                    ItemCount = metrics.RepositoryCounts.GetValueOrDefault(repo, 0),
                    StorageSizeMb = metrics.RepositorySizes.GetValueOrDefault(repo, 0)
                });
            }
            await dbContext.SaveChangesAsync();

            // Start background process
            var resourceList = selectedResourceIds ?? new List<int>();
            var userList = selectedUserIds ?? new List<string>();
            _ = Task.Run(() => ExecuteBackupProcessAsync(backupRecord.Id, selectedRepositories, resourceList, userList));

            return backupRecord.Id;
        }

        private async Task ExecuteBackupProcessAsync(int backupRecordId, List<string> repositories, List<int> resourceIds, List<string> userIds)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var backupRecord = await dbContext.BackupRecords.FindAsync(backupRecordId);
                if (backupRecord == null) return;

                backupRecord.ProgressPercent = 10;
                await dbContext.SaveChangesAsync();

                // Create a backup directory
                string backupDir = Path.Combine(_env.ContentRootPath, "Backups");
                if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupFolderName = $"Backup_{timestamp}";
                string backupFolderPath = Path.Combine(backupDir, backupFolderName);
                Directory.CreateDirectory(backupFolderPath);

                // Step 1: Export DB Data for selected repos
                backupRecord.ProgressPercent = 30;
                await dbContext.SaveChangesAsync();

                var exportData = new Dictionary<string, object>();
                var includePublishedResources = repositories.Contains("Published Resources") || repositories.Contains("User Uploads");

                if (resourceIds != null && resourceIds.Any())
                {
                    exportData["SpecificResources"] = await dbContext.Resources
                        .IgnoreQueryFilters()
                        .Where(r => resourceIds.Contains(r.ResourceId))
                        .Select(r => new { r.ResourceId, r.Title, r.Description, r.Subject, r.GradeLevel, r.ResourceType, r.Quarter, r.FileFormat, r.FilePath, r.FileSize, r.Status, r.DateUploaded })
                        .ToListAsync();
                }
                else
                {
                    if (includePublishedResources)
                    {
                        exportData["PublishedResources"] = await dbContext.Resources
                            .IgnoreQueryFilters()
                            .Where(r => r.Status == "Published")
                            .Select(r => new { r.ResourceId, r.Title, r.Description, r.Subject, r.GradeLevel, r.ResourceType, r.Quarter, r.FileFormat, r.FilePath, r.FileSize, r.Status, r.DateUploaded })
                            .ToListAsync();
                    }
                }

                if (repositories.Contains("User Accounts"))
                {
                    if (userIds != null && userIds.Any())
                        exportData["Users"] = await dbContext.Users
                            .Where(u => userIds.Contains(u.Id))
                            .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.DateCreated, u.Status })
                            .ToListAsync();
                    else
                        exportData["Users"] = await dbContext.Users
                            .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.DateCreated, u.Status })
                            .ToListAsync();
                }

                var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(exportData, jsonOptions);
                await File.WriteAllTextAsync(Path.Combine(backupFolderPath, "database_dump.json"), jsonString);

                backupRecord.ProgressPercent = 60;
                await dbContext.SaveChangesAsync();

                // Step 2: Copy User Uploads if Published Resources is selected
                if (includePublishedResources)
                {
                    string uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
                    if (Directory.Exists(uploadsDir))
                    {
                        string backupUploadsDir = Path.Combine(backupFolderPath, "uploads");
                        CopyDirectory(uploadsDir, backupUploadsDir);
                    }
                }

                backupRecord.ProgressPercent = 85;
                await dbContext.SaveChangesAsync();

                // Step 3: Zip everything
                string zipPath = Path.Combine(backupDir, $"{backupFolderName}.zip");
                if (File.Exists(zipPath)) File.Delete(zipPath); // Avoid conflicts
                ZipFile.CreateFromDirectory(backupFolderPath, zipPath);

                // Cleanup unzipped folder
                Directory.Delete(backupFolderPath, true);

                var fileInfo = new FileInfo(zipPath);

                backupRecord.ArchiveFilePath = zipPath;
                backupRecord.SizeDescription = $"{(fileInfo.Length / 1024.0 / 1024.0):F2} MB";
                backupRecord.TotalSizeMb = fileInfo.Length / 1024.0 / 1024.0;
                backupRecord.Status = "Completed";
                backupRecord.CompletedAt = DateTime.UtcNow;
                backupRecord.ProgressPercent = 100;

                await dbContext.SaveChangesAsync();
                _logger.LogInformation("Backup Id={BackupId} completed successfully. Size: {Size}", backupRecordId, backupRecord.SizeDescription);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup Id={BackupId} failed", backupRecordId);
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var backupRecord = await dbContext.BackupRecords.FindAsync(backupRecordId);
                    if (backupRecord != null)
                    {
                        backupRecord.Status = "Failed";
                        backupRecord.Notes = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                        await dbContext.SaveChangesAsync();
                    }
                }
                catch (Exception innerEx)
                {
                    _logger.LogError(innerEx, "Failed to update backup record status for Id={BackupId}", backupRecordId);
                }
            }
        }

        public async Task<BackupMetricsDto> CalculateStorageMetricsAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await CalculateStorageMetricsInternalAsync(dbContext);
        }

        /// <summary>
        /// Internal implementation that accepts an existing DbContext to avoid creating nested scopes.
        /// </summary>
        private async Task<BackupMetricsDto> CalculateStorageMetricsInternalAsync(ApplicationDbContext dbContext)
        {
            var metrics = new BackupMetricsDto();

            async Task<int> SafeCountAsync(Func<Task<int>> query, string metricName)
            {
                try
                {
                    return await query();
                }
                catch (SqlException ex) when (IsMissingTableException(ex))
                {
                    _logger.LogWarning(ex, "Skipping metric '{MetricName}' because backing table does not exist.", metricName);
                    return 0;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipping metric '{MetricName}' due to error.", metricName);
                    return 0;
                }
            }

            // Calculate mock DB size based on row counts (approximate 2KB per row)
            int publishedCount = await SafeCountAsync(() => dbContext.Resources.IgnoreQueryFilters().CountAsync(r => r.Status == "Published"), "Published Resources");
            int userCount = await SafeCountAsync(() => dbContext.Users.CountAsync(), "User Accounts");

            metrics.RepositoryCounts["Published Resources"] = publishedCount;
            metrics.RepositoryCounts["User Accounts"] = userCount;

            metrics.RepositorySizes["Published Resources"] = publishedCount * 0.002;
            metrics.RepositorySizes["User Accounts"] = userCount * 0.005;

            metrics.DatabaseSizeMb = metrics.RepositorySizes.Values.Sum();

            // Calculate actual uploads size
            string uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
            double uploadsSizeMb = 0;
            int uploadsCount = 0;

            if (Directory.Exists(uploadsDir))
            {
                try
                {
                    var files = Directory.GetFiles(uploadsDir, "*.*", SearchOption.AllDirectories);
                    uploadsCount = files.Length;
                    uploadsSizeMb = files.Sum(f => new FileInfo(f).Length) / 1024.0 / 1024.0;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to calculate uploads size");
                }
            }

            metrics.RepositoryCounts["User Uploads"] = uploadsCount;
            metrics.RepositorySizes["User Uploads"] = uploadsSizeMb;
            metrics.UploadsSizeMb = uploadsSizeMb;

            return metrics;
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists) return;

            Directory.CreateDirectory(destinationDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, true);
            }

            foreach (DirectoryInfo subDir in dir.GetDirectories())
            {
                string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestinationDir);
            }
        }

        public async Task<int> InitiateRestoreAsync(int backupRecordId, string triggerUserId)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var restoreOp = new RestoreOperation
            {
                BackupRecordId = backupRecordId,
                RestoreType = "Manual",
                Status = "In Progress",
                RestoreDate = DateTime.UtcNow,
                RestoredByUserId = triggerUserId,
                Details = "Restore initiated"
            };

            dbContext.RestoreOperations.Add(restoreOp);
            await dbContext.SaveChangesAsync();

            // Start background process
            _ = Task.Run(() => ExecuteRestoreProcessAsync(restoreOp.Id, backupRecordId));

            return restoreOp.Id;
        }

        private async Task ExecuteRestoreProcessAsync(int restoreOpId, int backupRecordId)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var restoreOp = await dbContext.RestoreOperations.FindAsync(restoreOpId);
                var backupRecord = await dbContext.BackupRecords.FindAsync(backupRecordId);

                if (restoreOp == null || backupRecord == null || string.IsNullOrEmpty(backupRecord.ArchiveFilePath))
                    return;

                if (!File.Exists(backupRecord.ArchiveFilePath))
                {
                    restoreOp.Status = "Failed";
                    restoreOp.Details = "Backup archive file not found.";
                    await dbContext.SaveChangesAsync();
                    return;
                }

                // Create a temporary extraction directory
                string tempDir = Path.Combine(_env.ContentRootPath, "Backups", $"TempExtract_{Guid.NewGuid()}");
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                // Extract Zip
                ZipFile.ExtractToDirectory(backupRecord.ArchiveFilePath, tempDir);

                // Step 1: Restore Database Dump
                string dumpPath = Path.Combine(tempDir, "database_dump.json");
                if (File.Exists(dumpPath))
                {
                    string jsonString = await File.ReadAllTextAsync(dumpPath);
                    var dumpData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonString);

                    if (dumpData != null)
                    {
                        if (dumpData.ContainsKey("PublishedResources"))
                            await RestoreResources(dbContext, dumpData["PublishedResources"]);

                        if (dumpData.ContainsKey("SpecificResources"))
                            await RestoreResources(dbContext, dumpData["SpecificResources"]);
                    }
                }

                // Step 2: Restore Uploads
                string extractedUploadsDir = Path.Combine(tempDir, "uploads");
                if (Directory.Exists(extractedUploadsDir))
                {
                    string targetUploadsDir = Path.Combine(_env.WebRootPath, "uploads");
                    CopyDirectory(extractedUploadsDir, targetUploadsDir);
                }

                // Cleanup
                Directory.Delete(tempDir, true);

                restoreOp.Status = "Completed";
                restoreOp.Details = "Data restored successfully from backup.";
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restore failed for restoreOp={RestoreOpId}", restoreOpId);
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var restoreOp = await dbContext.RestoreOperations.FindAsync(restoreOpId);
                    if (restoreOp != null)
                    {
                        restoreOp.Status = "Failed";
                        restoreOp.Details = $"Restore failed: {ex.Message}";
                        await dbContext.SaveChangesAsync();
                    }
                }
                catch (Exception innerEx)
                {
                    _logger.LogError(innerEx, "Failed to update restore operation status for Id={RestoreOpId}", restoreOpId);
                }
            }
        }

        private async Task RestoreResources(ApplicationDbContext dbContext, JsonElement element)
        {
            try
            {
                var resources = JsonSerializer.Deserialize<List<Resource>>(element.GetRawText());
                if (resources != null)
                {
                    foreach (var res in resources)
                    {
                        var existing = await dbContext.Resources.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.ResourceId == res.ResourceId);
                        if (existing == null)
                        {
                            res.ResourceId = 0;
                            dbContext.Resources.Add(res);
                        }
                    }
                    await dbContext.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to restore resources from backup element");
            }
        }
    }
}
