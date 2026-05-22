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
        Task<int> InitiateBackupAsync(string triggerUserId, List<string> selectedRepositories, string backupType = "Manual");
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

        public async Task<int> InitiateBackupAsync(string triggerUserId, List<string> selectedRepositories, string backupType = "Manual")
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
            _ = Task.Run(() => ExecuteBackupProcessAsync(backupRecord.Id, selectedRepositories));

            return backupRecord.Id;
        }

        private async Task ExecuteBackupProcessAsync(int backupRecordId, List<string> repositories)
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

                if (repositories.Contains("Math Resources")) exportData["Math"] = await dbContext.Resources.Where(r => r.Subject == "Mathematics").ToListAsync();
                if (repositories.Contains("Science Resources")) exportData["Science"] = await dbContext.Resources.Where(r => r.Subject == "Science").ToListAsync();
                if (repositories.Contains("English Resources")) exportData["English"] = await dbContext.Resources.Where(r => r.Subject == "English").ToListAsync();
                if (repositories.Contains("User Accounts")) exportData["Users"] = await dbContext.Users.ToListAsync();
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
    }
}
