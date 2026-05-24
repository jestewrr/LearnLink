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

        public async Task<int> InitiateBackupAsync(string triggerUserId, List<string> selectedRepositories, List<int>? selectedResourceIds = null, List<string>? selectedUserIds = null, string backupType = "Manual")
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var metrics = await CalculateStorageMetricsAsync();
            
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

            foreach (var repo in selectedRepositories)
            {
                dbContext.BackupItems.Add(new BackupItem
                {
                    BackupRecordId = backupRecord.Id,
                    RepositoryName = repo,
                    ItemCount = metrics.RepositoryCounts.ContainsKey(repo) ? metrics.RepositoryCounts[repo] : 0,
                    StorageSizeMb = metrics.RepositorySizes.ContainsKey(repo) ? metrics.RepositorySizes[repo] : 0
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

                if (resourceIds != null && resourceIds.Any())
                {
                    exportData["SpecificResources"] = await dbContext.Resources.Where(r => resourceIds.Contains(r.ResourceId)).ToListAsync();
                }
                else
                {
                    if (repositories.Contains("Math Resources")) exportData["Math"] = await dbContext.Resources.Where(r => r.Subject == "Mathematics").ToListAsync();
                    if (repositories.Contains("Science Resources")) exportData["Science"] = await dbContext.Resources.Where(r => r.Subject == "Science").ToListAsync();
                    if (repositories.Contains("English Resources")) exportData["English"] = await dbContext.Resources.Where(r => r.Subject == "English").ToListAsync();
                }

                if (repositories.Contains("User Accounts"))
                {
                    if (userIds != null && userIds.Any())
                        exportData["Users"] = await dbContext.Users.Where(u => userIds.Contains(u.Id)).ToListAsync();
                    else
                        exportData["Users"] = await dbContext.Users.ToListAsync();
                }
                if (repositories.Contains("Audit Logs"))
                {
                    try
                    {
                        exportData["AuditLogs"] = await dbContext.AuditLogs.ToListAsync();
                    }
                    catch (SqlException ex) when (IsMissingTableException(ex))
                    {
                        _logger.LogWarning(ex, "Skipping audit logs in backup because AuditLogs table does not exist.");
                        exportData["AuditLogs"] = new List<AuditLog>();
                    }
                }
                if (repositories.Contains("Archived Resources")) exportData["ArchivedResources"] = await dbContext.ArchivedResources.ToListAsync();

                string jsonString = JsonSerializer.Serialize(exportData);
                await File.WriteAllTextAsync(Path.Combine(backupFolderPath, "database_dump.json"), jsonString);

                backupRecord.ProgressPercent = 60;
                await dbContext.SaveChangesAsync();

                // Step 2: Copy User Uploads if selected
                if (repositories.Contains("User Uploads"))
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup failed");
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var backupRecord = await dbContext.BackupRecords.FindAsync(backupRecordId);
                if (backupRecord != null)
                {
                    backupRecord.Status = "Failed";
                    backupRecord.Notes = ex.Message;
                    await dbContext.SaveChangesAsync();
                }
            }
        }

        public async Task<BackupMetricsDto> CalculateStorageMetricsAsync()
        {
            var metrics = new BackupMetricsDto();
            
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

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
            }

            // Calculate mock DB size based on row counts (approximate 2KB per row)
            int mathCount = await SafeCountAsync(() => dbContext.Resources.CountAsync(r => r.Subject == "Mathematics"), "Math Resources");
            int scienceCount = await SafeCountAsync(() => dbContext.Resources.CountAsync(r => r.Subject == "Science"), "Science Resources");
            int englishCount = await SafeCountAsync(() => dbContext.Resources.CountAsync(r => r.Subject == "English"), "English Resources");
            int userCount = await SafeCountAsync(() => dbContext.Users.CountAsync(), "User Accounts");
            int auditCount = await SafeCountAsync(() => dbContext.AuditLogs.CountAsync(), "Audit Logs");
            int archiveCount = await SafeCountAsync(() => dbContext.ArchivedResources.CountAsync(), "Archived Resources");

            metrics.RepositoryCounts["Math Resources"] = mathCount;
            metrics.RepositoryCounts["Science Resources"] = scienceCount;
            metrics.RepositoryCounts["English Resources"] = englishCount;
            metrics.RepositoryCounts["User Accounts"] = userCount;
            metrics.RepositoryCounts["Audit Logs"] = auditCount;
            metrics.RepositoryCounts["Archived Resources"] = archiveCount;

            metrics.RepositorySizes["Math Resources"] = mathCount * 0.002;
            metrics.RepositorySizes["Science Resources"] = scienceCount * 0.002;
            metrics.RepositorySizes["English Resources"] = englishCount * 0.002;
            metrics.RepositorySizes["User Accounts"] = userCount * 0.005;
            metrics.RepositorySizes["Audit Logs"] = auditCount * 0.001;
            metrics.RepositorySizes["Archived Resources"] = archiveCount * 0.003;

            metrics.DatabaseSizeMb = metrics.RepositorySizes.Values.Sum();

            // Calculate actual uploads size
            string uploadsDir = Path.Combine(_env.WebRootPath, "uploads");
            double uploadsSizeMb = 0;
            int uploadsCount = 0;

            if (Directory.Exists(uploadsDir))
            {
                var files = Directory.GetFiles(uploadsDir, "*.*", SearchOption.AllDirectories);
                uploadsCount = files.Length;
                uploadsSizeMb = files.Sum(f => new FileInfo(f).Length) / 1024.0 / 1024.0;
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
                        // Restore Math Resources
                        if (dumpData.ContainsKey("Math"))
                            await RestoreResources(dbContext, dumpData["Math"]);
                        
                        // Restore Science Resources
                        if (dumpData.ContainsKey("Science"))
                            await RestoreResources(dbContext, dumpData["Science"]);
                        
                        // Restore English Resources
                        if (dumpData.ContainsKey("English"))
                            await RestoreResources(dbContext, dumpData["English"]);
                        
                        // Restore Specific Resources
                        if (dumpData.ContainsKey("SpecificResources"))
                            await RestoreResources(dbContext, dumpData["SpecificResources"]);

                        // Note: User accounts and Audit Logs are skipped for soft restore 
                        // as they might break relations or overwrite current login state.
                        // For a real full restore, they should be carefully merged.
                        
                        // Restore Archived Resources
                        if (dumpData.ContainsKey("ArchivedResources"))
                        {
                            var archivedItems = JsonSerializer.Deserialize<List<ArchivedResource>>(dumpData["ArchivedResources"].GetRawText());
                            if (archivedItems != null)
                            {
                                foreach (var item in archivedItems)
                                {
                                    if (!await dbContext.ArchivedResources.AnyAsync(a => a.Id == item.Id))
                                    {
                                        item.Id = 0; // Let DB generate new ID or set IDENTITY_INSERT
                                        dbContext.ArchivedResources.Add(item);
                                    }
                                }
                                await dbContext.SaveChangesAsync();
                            }
                        }
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
                _logger.LogError(ex, "Restore failed");
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
        }

        private async Task RestoreResources(ApplicationDbContext dbContext, JsonElement element)
        {
            var resources = JsonSerializer.Deserialize<List<Resource>>(element.GetRawText());
            if (resources != null)
            {
                foreach (var res in resources)
                {
                    // Check if resource already exists
                    var existing = await dbContext.Resources.FindAsync(res.ResourceId);
                    if (existing == null)
                    {
                        // Add missing resource
                        // Keep ID 0 so identity handles it, or configure SET IDENTITY_INSERT
                        res.ResourceId = 0; 
                        dbContext.Resources.Add(res);
                    }
                }
                await dbContext.SaveChangesAsync();
            }
        }
    }
}
