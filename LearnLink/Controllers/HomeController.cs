using System.Diagnostics;
using System.Security.Claims;
using LearnLink.Data;
using LearnLink.Models;
using LearnLink.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Resource = LearnLink.Models.Resource;

namespace LearnLink.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _environment;
        private readonly ISchoolContext _schoolContext;
        private readonly IRecommendationService _recommendationService;
        private readonly IEmailService _emailService;
        private readonly IStorageService _storage;
        private readonly bool _googleAuthEnabled;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;
        private static readonly SemaphoreSlim _googleBooksSemaphore = new SemaphoreSlim(1, 1);
        private static DateTime _lastGoogleBooksRequest = DateTime.MinValue;

        public HomeController(
            ApplicationDbContext context,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment environment,
            ISchoolContext schoolContext,
            GoogleAuthFlag googleAuth,
            IRecommendationService recommendationService,
            IEmailService emailService,
            IStorageService storage,
            IMemoryCache cache,
            IConfiguration configuration)
        {
            _context = context;
            _signInManager = signInManager;
            _userManager = userManager;
            _environment = environment;
            _schoolContext = schoolContext;
            _googleAuthEnabled = googleAuth.IsEnabled;
            _recommendationService = recommendationService;
            _emailService = emailService;
            _storage = storage;
            _cache = cache;
            _configuration = configuration;
        }

        // ==================== Helpers ====================

        private async Task<ApplicationUser?> GetCurrentUserAsync()
            => await _userManager.GetUserAsync(User);

        private IActionResult RedirectToUploadForm(int? resourceId = null)
        {
            if (resourceId.HasValue && resourceId.Value > 0)
            {
                return RedirectToAction("Upload", new { id = resourceId.Value });
            }

            return RedirectToAction("Upload");
        }

        /// <summary>
        /// Returns the effective school ID for the current user session.
        /// SuperAdmin sees all if no school is switched; otherwise returns the switched/user school.
        /// </summary>
        private int? GetEffectiveSchoolId()
            => _schoolContext.CurrentSchoolId;

        /// <summary>
        /// Verifies that the target user belongs to the same school as the current user.
        /// SuperAdmin bypasses this check when viewing all schools.
        /// </summary>
        private bool IsSameSchool(ApplicationUser targetUser)
        {
            if (_schoolContext.IsPlatformAdmin && _schoolContext.CurrentSchoolId == null)
                return true; // Platform admin viewing all schools
            return targetUser.SchoolId == _schoolContext.CurrentSchoolId;
        }

        /// <summary>
        /// Loads the dynamic school settings (subjects, grades, etc.) for the current school context.
        /// Falls back to defaults if no settings are found.
        /// </summary>
        private List<string> ParseSettingOrDefault(string? json, List<string> defaultList)
        {
            if (string.IsNullOrWhiteSpace(json)) return defaultList;
            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(json);
                return list != null && list.Any() ? list : defaultList;
            }
            catch
            {
                return defaultList;
            }
        }

        private async Task LoadSchoolSettingsToViewBag()
        {
            var schoolId = GetEffectiveSchoolId();
            SchoolSettings? settings = null;

            if (schoolId.HasValue)
            {
                settings = await _context.SchoolSettings
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(s => s.SchoolId == schoolId.Value);
            }

            ViewBag.SchoolSubjects = ParseSettingOrDefault(settings?.Subjects, 
                new List<string> { "Mathematics", "Science", "English", "Filipino", "Araling Panlipunan", "MAPEH", "TLE", "Values Education" });

            ViewBag.SchoolGradeLevels = ParseSettingOrDefault(settings?.GradeLevels, 
                new List<string> { "Grade 7", "Grade 8", "Grade 9", "Grade 10" });

            ViewBag.SchoolResourceTypes = ParseSettingOrDefault(settings?.ResourceTypes, 
                new List<string> { "Reviewer/Study Guide", "Lesson Plan", "Activity Sheet", "Assessment/Quiz", "Presentation", "Video Tutorial", "Reading Material", "Reference Document" });

            ViewBag.SchoolQuarters = ParseSettingOrDefault(settings?.Quarters, 
                new List<string> { "1st Quarter", "2nd Quarter", "3rd Quarter", "4th Quarter", "All Quarters" });

            // School context info for layout
            ViewBag.CurrentSchoolId = schoolId;
            ViewBag.CurrentSchoolName = _schoolContext.CurrentSchoolName;
            ViewBag.IsPlatformAdmin = _schoolContext.IsPlatformAdmin;
        }

        private static string GetIconClass(string format) => format?.ToUpper() switch
        {
            "PDF" => "bi-file-earmark-pdf",
            "DOCX" or "DOC" => "bi-file-earmark-word",
            "PPTX" or "PPT" => "bi-file-earmark-ppt",
            "XLSX" or "XLS" => "bi-file-earmark-excel",
            "LINK" => "bi-link-45deg",
            _ => "bi-file-earmark"
        };
        private static string GetIconColor(string format) => format?.ToUpper() switch
        {
            "PDF" => "text-danger",
            "DOCX" or "DOC" => "text-primary",
            "PPTX" or "PPT" => "text-warning",
            "XLSX" or "XLS" => "text-success",
            "LINK" => "text-primary",
            _ => "text-muted"
        };
        private static string GetIconBg(string format) => format?.ToUpper() switch
        {
            "PDF" => "#fee2e2",
            "DOCX" or "DOC" => "#dbeafe",
            "PPTX" or "PPT" => "#fef3c7",
            "XLSX" or "XLS" => "#dcfce7",
            "LINK" => "#dbeafe",
            _ => "#e2e8f0"
        };

        private static bool IsLinkFileFormat(string? fileFormat)
            => string.Equals(fileFormat?.TrimStart('.'), "LINK", StringComparison.OrdinalIgnoreCase);

        private static string NormalizeExternalResourceUrl(string? rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                return string.Empty;
            }

            var candidate = rawUrl.Trim();

            if (candidate.StartsWith("/books?id=", StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith("books?id=", StringComparison.OrdinalIgnoreCase))
            {
                candidate = $"https://books.google.com/{candidate.TrimStart('/')}";
            }
            else if (candidate.StartsWith("openlibrary.org/", StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith("archive.org/", StringComparison.OrdinalIgnoreCase))
            {
                candidate = $"https://{candidate}";
            }
            else if (candidate.StartsWith("/works/", StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith("/books/", StringComparison.OrdinalIgnoreCase))
            {
                candidate = $"https://openlibrary.org{candidate}";
            }
            else if (candidate.StartsWith("details/", StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith("embed/", StringComparison.OrdinalIgnoreCase))
            {
                candidate = $"https://archive.org/{candidate}";
            }

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            {
                return candidate;
            }

            var host = uri.Host.ToLowerInvariant();
            if (host.Contains("books.google."))
            {
                var googleBookId = ExtractQueryParameter(uri.Query, "id");
                if (!string.IsNullOrWhiteSpace(googleBookId))
                {
                    return $"https://books.google.com/books?id={Uri.EscapeDataString(googleBookId)}";
                }
            }

            if (host.EndsWith("archive.org", StringComparison.OrdinalIgnoreCase))
            {
                var identifier = ExtractArchiveIdentifier(uri.AbsolutePath);
                if (!string.IsNullOrWhiteSpace(identifier))
                {
                    return $"https://archive.org/details/{Uri.EscapeDataString(identifier)}";
                }
            }

            if (host.EndsWith("openlibrary.org", StringComparison.OrdinalIgnoreCase))
            {
                return $"https://openlibrary.org{uri.PathAndQuery}";
            }

            return uri.ToString();
        }

        private static string? BuildExternalPreviewUrl(string? externalUrl)
        {
            var normalizedUrl = NormalizeExternalResourceUrl(externalUrl);
            if (string.IsNullOrWhiteSpace(normalizedUrl) || !Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
            {
                return null;
            }

            var host = uri.Host.ToLowerInvariant();
            if (host.Contains("books.google."))
            {
                var googleBookId = ExtractQueryParameter(uri.Query, "id");
                return !string.IsNullOrWhiteSpace(googleBookId)
                    ? $"https://books.google.com/books?id={Uri.EscapeDataString(googleBookId)}&output=embed"
                    : null;
            }

            if (host.EndsWith("archive.org", StringComparison.OrdinalIgnoreCase))
            {
                var identifier = ExtractArchiveIdentifier(uri.AbsolutePath);
                return !string.IsNullOrWhiteSpace(identifier)
                    ? $"https://archive.org/embed/{Uri.EscapeDataString(identifier)}"
                    : null;
            }

            return null;
        }

        private static string GetExternalSourceLabel(string? externalUrl)
        {
            var normalizedUrl = NormalizeExternalResourceUrl(externalUrl);
            if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
            {
                return "External source";
            }

            var host = uri.Host.ToLowerInvariant();
            if (host.Contains("books.google.")) return "Google Books";
            if (host.EndsWith("archive.org", StringComparison.OrdinalIgnoreCase)) return "Internet Archive";
            if (host.EndsWith("openlibrary.org", StringComparison.OrdinalIgnoreCase)) return "Open Library";
            return uri.Host;
        }

        private static string? ExtractArchiveIdentifier(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2
                && (segments[0].Equals("details", StringComparison.OrdinalIgnoreCase)
                    || segments[0].Equals("embed", StringComparison.OrdinalIgnoreCase)))
            {
                return segments[1];
            }

            return null;
        }

        private static string? GetFirstArrayString(System.Text.Json.JsonElement element)
        {
            if (element.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in element.EnumerateArray())
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static readonly TimeSpan PendingReviewWindow = TimeSpan.FromDays(3);

        private static DateTime GetPendingReviewDeadline(Resource resource) => resource.DateUploaded.Add(PendingReviewWindow);

        private async Task AutoRejectExpiredPendingResourcesAsync()
        {
            var now = DateTime.Now;
            var staleResources = await _context.Resources
                .Where(r => r.Status == "Pending" && r.PendingReviewPreviewedAt == null && r.DateUploaded <= now.Subtract(PendingReviewWindow))
                .ToListAsync();

            if (staleResources.Count == 0)
            {
                return;
            }

            foreach (var resource in staleResources)
            {
                resource.Status = "Rejected";
                resource.RejectionReason = "Automatically rejected because no admin or manager previewed it within 3 days of submission.";

                _context.Notifications.Add(new Notification
                {
                    UserId = resource.UserId,
                    Title = "Resource Auto-Rejected",
                    Message = $"Your resource \"{resource.Title}\" was automatically rejected because it was not previewed within 3 days of submission.",
                    Type = "Rejected",
                    Icon = "bi-hourglass-split",
                    IconBg = "#fee2e2",
                    ResourceId = resource.ResourceId,
                    Link = $"/Home/ResourceDetail/{resource.ResourceId}"
                });
            }

            await _context.SaveChangesAsync();
        }

        private static ResourceViewModel MapResource(Resource r)
        {
            return new ResourceViewModel
            {
                Id = r.ResourceId,
                Title = r.Title,
                Description = r.Description,
                Subject = r.Subject,
                GradeLevel = r.GradeLevel,
                ResourceType = r.ResourceType,
                FileFormat = r.FileFormat,
                FileSize = r.FileSize,
                FilePath = r.FilePath ?? "",
                Status = r.Status,
                ViewCount = r.ViewCount,
                DownloadCount = r.DownloadCount,
                Rating = r.Rating,
                RatingCount = r.RatingCount,
                Uploader = r.User?.FullName ?? "Unknown",
                UploaderInitials = r.User?.Initials ?? "?",
                UploaderColor = r.User?.AvatarColor ?? "",
                Quarter = r.Quarter,
                PendingReviewPreviewedAt = r.PendingReviewPreviewedAt,
                IconClass = GetIconClass(r.FileFormat),
                IconColor = GetIconColor(r.FileFormat),
                IconBg = GetIconBg(r.FileFormat),
                CreatedAt = r.DateUploaded
            };
        }

        private LessonViewModel MapLesson(LessonLearned l, bool isLiked = false)
        {
            return new LessonViewModel
            {
                Id = l.LessonId,
                Title = l.Title,
                Content = l.Content,
                Category = l.Category,
                Author = l.User?.FullName ?? "Unknown",
                UserId = l.User?.Id ?? "",
                AuthorInitials = l.User?.Initials ?? "?",
                AuthorColor = l.User?.AvatarColor ?? "",
                AuthorRole = "Contributor",
                CreatedAt = l.DateSubmitted,
                Tags = string.IsNullOrEmpty(l.Tags) ? new List<string>() : l.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList(),
                LikeCount = l.LikeCount,
                CommentCount = l.CommentCount,
                IsLiked = isLiked,
                ResourceId = l.ResourceId,
                ResourceTitle = l.Resource?.Title ?? "Unknown Resource",
                ResourceUploaderId = l.Resource?.UserId ?? "",
                Comments = (l.Comments ?? new List<LessonComment>())
                    .OrderBy(c => c.DatePosted)
                    .Select(c => new LessonCommentViewModel
                    {
                        Id = c.LessonCommentId,
                        Content = c.Content,
                        Author = c.User?.FullName ?? "Unknown",
                        UserId = c.UserId,
                        AuthorInitials = c.User?.Initials ?? "?",
                        AuthorColor = c.User?.AvatarColor ?? "",
                        AuthorRole = "",
                        CreatedAt = c.DatePosted
                    }).ToList()
            };
        }

        private async Task TryEnsureLessonCommentsSchemaAsync()
        {
            try
            {
                await _context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('LessonComments') AND type in ('U'))
                    BEGIN
                        CREATE TABLE [LessonComments] (
                            [LessonCommentId] int NOT NULL IDENTITY(1,1),
                            [LessonId] int NOT NULL,
                            [UserId] nvarchar(450) NOT NULL,
                            [Content] nvarchar(2000) NOT NULL,
                            [DatePosted] datetime2 NOT NULL,
                            CONSTRAINT [PK_LessonComments] PRIMARY KEY ([LessonCommentId]),
                            CONSTRAINT [FK_LessonComments_LessonsLearned_LessonId] FOREIGN KEY ([LessonId]) REFERENCES [LessonsLearned] ([LessonId]) ON DELETE CASCADE,
                            CONSTRAINT [FK_LessonComments_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id])
                        );
                        CREATE INDEX [IX_LessonComments_LessonId] ON [LessonComments] ([LessonId]);
                        CREATE INDEX [IX_LessonComments_UserId] ON [LessonComments] ([UserId]);
                    END
                ");
            }
            catch
            {
                // Fall back to comment-free lesson rendering if the database cannot be updated from this request.
            }
        }

        private static bool IsMissingLessonCommentsTable(SqlException exception)
        {
            return exception.Number == 208 && exception.Message.Contains("LessonComments", StringComparison.OrdinalIgnoreCase);
        }

        private DiscussionViewModel MapDiscussion(Discussion d, bool isLiked = false)
        {
            return new DiscussionViewModel
            {
                Id = d.DiscussionId,
                Title = d.Title,
                Content = d.Content,
                Author = d.User?.FullName ?? "Unknown",
                UserId = d.User?.Id ?? "",
                AuthorInitials = d.User?.Initials ?? "?",
                AuthorColor = d.User?.AvatarColor ?? "",
                AuthorRole = "User",
                Type = d.Type,
                Category = d.Category,
                Tags = string.IsNullOrEmpty(d.Tags) ? new List<string>() : d.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList(),
                ReplyCount = d.Posts?.Count ?? 0,
                ViewCount = d.ViewCount,
                LikeCount = d.LikeCount,
                CreatedAt = d.DateCreated,
                Status = d.Status,
                IsLiked = isLiked
            };
        }

        private ReplyViewModel MapReply(DiscussionPost p, bool isLiked = false)
        {
            return new ReplyViewModel
            {
                Id = p.PostId,
                Content = p.Content,
                Author = p.User?.FullName ?? "Unknown",
                UserId = p.User?.Id ?? "",
                AuthorInitials = p.User?.Initials ?? "?",
                AuthorColor = p.User?.AvatarColor ?? "",
                AuthorRole = "User",
                AuthorTitle = p.User?.GradeOrPosition ?? "",
                LikeCount = p.LikeCount,
                IsBestAnswer = p.IsBestAnswer,
                IsLiked = isLiked,
                CreatedAt = p.DatePosted
            };
        }

        private IQueryable<Resource> BuildAccessiblePublishedResourceQuery(ApplicationUser? currentUser)
        {
            var query = _context.Resources
                .Include(r => r.User)
                .Where(r => r.Status == "Published");

            query = query.Where(r => r.AccessDuration != "Custom" || r.AccessExpiresAt == null || r.AccessExpiresAt > DateTime.Now);

            if (currentUser == null)
                return query.Where(r => r.AccessLevel == "Public");

            if (User.IsInRole("SuperAdmin") || User.IsInRole("Manager"))
                return query;

            var userId = currentUser.Id;
            var grantedResourceIds = _context.ResourceAccessGrants
                .Where(g => g.UserId == userId)
                .Select(g => g.ResourceId);

            return query.Where(r =>
                r.UserId == userId ||
                r.AccessLevel == "Public" ||
                r.AccessLevel == "Registered" ||
                (r.AccessLevel == "Restricted" && grantedResourceIds.Contains(r.ResourceId)));
        }

        private async Task PopulateResourceMetadataAsync(IList<ResourceViewModel> resources)
        {
            if (resources.Count == 0)
                return;

            var resourceIds = resources.Select(r => r.Id).Distinct().ToList();

            var tagsByResource = await _context.ResourceTags
                .Where(rt => resourceIds.Contains(rt.ResourceId))
                .Include(rt => rt.Tag)
                .GroupBy(rt => rt.ResourceId)
                .ToDictionaryAsync(
                    group => group.Key,
                    group => group
                        .Select(rt => rt.Tag!.TagName)
                        .Where(tagName => !string.IsNullOrWhiteSpace(tagName))
                        .Distinct()
                        .OrderBy(tagName => tagName)
                        .ToList());

            var categoriesByResource = await _context.ResourceCategoryMaps
                .Where(map => resourceIds.Contains(map.ResourceId))
                .Include(map => map.Category)
                .GroupBy(map => map.ResourceId)
                .ToDictionaryAsync(
                    group => group.Key,
                    group => group
                        .Select(map => map.Category!.CategoryName)
                        .Where(categoryName => !string.IsNullOrWhiteSpace(categoryName))
                        .Distinct()
                        .OrderBy(categoryName => categoryName)
                        .ToList());

            foreach (var resource in resources)
            {
                resource.Tags = tagsByResource.TryGetValue(resource.Id, out var tags) ? tags : new List<string>();
                resource.Categories = categoriesByResource.TryGetValue(resource.Id, out var categories) ? categories : new List<string>();
            }
        }

        private static string GetRoleBadge(string role) => role switch
        {
            "SuperAdmin" => "ll-badge-primary",
            "Manager" => "ll-badge-danger",
            "Contributor" => "ll-badge-warning",
            "Student" => "ll-badge-info",
            _ => "ll-badge-info"
        };

        private static string GetStatusBadge(string status) => status switch
        {
            "Active" => "ll-badge-success",
            "Suspended" => "ll-badge-danger",
            "Pending" => "ll-badge-warning",
            _ => "ll-badge-info"
        };

        private async Task LogActivity(string userId, string activityType, string targetTitle, int? resourceId = null)
        {
            _context.UserActivityLogs.Add(new UserActivityLog
            {
                UserId = userId,
                ActivityType = activityType,
                TargetTitle = targetTitle,
                ResourceId = resourceId,
                ActivityDate = DateTime.Now
            });
            await _context.SaveChangesAsync();
        }

        // ===== Approve / Reject (SuperAdmin, Manager) =====

        [Authorize(Roles = "SuperAdmin,Manager")]
        public async Task<IActionResult> ApproveResource(int id)
        {
            await AutoRejectExpiredPendingResourcesAsync();

            var resource = await _context.Resources.FindAsync(id);
            if (resource != null)
            {
                resource.Status = "Published";
                resource.RejectionReason = null; // Clear any prior reason
                await _context.SaveChangesAsync();

                var currentUser = await GetCurrentUserAsync();
                if (currentUser != null)
                    await LogActivity(currentUser.Id, "Approve", resource.Title, resource.ResourceId);

                // Create notification for the resource uploader
                _context.Notifications.Add(new Notification
                {
                    UserId = resource.UserId,
                    Title = "Resource Approved",
                    Message = $"Your resource \"{resource.Title}\" has been approved and is now published!",
                    Type = "Approved",
                    Icon = "bi-check-circle-fill",
                    IconBg = "#d1fae5",
                    ResourceId = resource.ResourceId,
                    Link = $"/Home/ResourceDetail/{resource.ResourceId}"
                });
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Resource \"{resource.Title}\" has been approved and published.";
                TempData["SuccessTitle"] = "Approval Successful";
            }
            return RedirectToAction("Dashboard");
        }

        [Authorize(Roles = "SuperAdmin,Manager")]
        [HttpPost]
        public async Task<IActionResult> RejectResource(int id, string? reason)
        {
            await AutoRejectExpiredPendingResourcesAsync();

            var resource = await _context.Resources.FindAsync(id);
            if (resource != null)
            {
                resource.Status = "Rejected";
                resource.RejectionReason = reason;
                await _context.SaveChangesAsync();

                var currentUser = await GetCurrentUserAsync();
                if (currentUser != null)
                    await LogActivity(currentUser.Id, "Reject", resource.Title, resource.ResourceId);

                // Create notification for the resource uploader
                var reasonText = !string.IsNullOrWhiteSpace(reason)
                    ? $" Reason: {reason}"
                    : "";
                _context.Notifications.Add(new Notification
                {
                    UserId = resource.UserId,
                    Title = "Resource Rejected",
                    Message = $"Your resource \"{resource.Title}\" has been rejected.{reasonText}",
                    Type = "Rejected",
                    Icon = "bi-x-circle-fill",
                    IconBg = "#fee2e2",
                    ResourceId = resource.ResourceId,
                    Link = $"/Home/ResourceDetail/{resource.ResourceId}"
                });
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Resource \"{resource.Title}\" has been rejected.";
                TempData["SuccessTitle"] = "Resource Rejected";
            }
            return RedirectToAction("Dashboard");
        }

        // ==================== Notifications API ====================

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetNotifications()
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Json(new { notifications = Array.Empty<object>(), unreadCount = 0 });

            var notifications = (await _context.Notifications
                .Where(n => n.UserId == currentUser.Id)
                .OrderByDescending(n => n.CreatedAt)
                .Take(20)
                .ToListAsync())
                .Select(n => new
                {
                    id = n.NotificationId,
                    title = n.Title,
                    message = n.Message,
                    type = n.Type,
                    icon = n.Icon,
                    iconBg = n.IconBg,
                    link = n.Link,
                    isRead = n.IsRead,
                    createdAt = n.CreatedAt,
                    timeAgo = GetTimeAgo(n.CreatedAt)
                })
                .ToList();

            var unreadCount = await _context.Notifications
                .CountAsync(n => n.UserId == currentUser.Id && !n.IsRead);

            return Json(new { notifications, unreadCount });
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> MarkNotificationRead(int id)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Unauthorized();

            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.NotificationId == id && n.UserId == currentUser.Id);

            if (notification != null)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync();
            }
            return Ok();
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> MarkAllNotificationsRead()
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Unauthorized();

            var unread = await _context.Notifications
                .Where(n => n.UserId == currentUser.Id && !n.IsRead)
                .ToListAsync();

            foreach (var n in unread) n.IsRead = true;
            await _context.SaveChangesAsync();
            return Ok();
        }

        // ==================== Public Pages ====================

        public IActionResult Index() => View("Landing");

        public IActionResult Privacy() => View();

        // ==================== Auth Pages ====================

        [HttpGet]
        public IActionResult Login()
        {
            if (_signInManager.IsSignedIn(User))
                return RedirectToAction("Dashboard");
            ViewBag.GoogleAuthEnabled = _googleAuthEnabled;
            ViewBag.RememberMe = false;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string email, string password, bool rememberMe)
        {
            ViewBag.GoogleAuthEnabled = _googleAuthEnabled;
            ViewBag.Email = email;
            ViewBag.RememberMe = rememberMe;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                ViewBag.Error = "Please enter email and password.";
                return View();
            }

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                ViewBag.Error = "Invalid email or password.";
                return View();
            }

            // Check if user is suspended
            if (user.Status == "Suspended")
            {
                ViewBag.Error = "Your account has been suspended. Please contact the administrator.";
                return View();
            }

            // Check if user's school is active (skip for platform admins with no school)
            if (user.SchoolId.HasValue)
            {
                var school = await _context.Schools.FindAsync(user.SchoolId.Value);
                if (school != null && !school.IsActive)
                {
                    ViewBag.Error = "Your school account is currently inactive. Please contact the platform administrator.";
                    return View();
                }
            }

            var result = await _signInManager.PasswordSignInAsync(user, password, rememberMe, false);
            if (result.Succeeded)
            {
                // Reload the user entity because PasswordSignInAsync may have updated the database
                // and incremented the ConcurrencyStamp, which causes a DbUpdateConcurrencyException
                // if we try to update the stale tracked entity.
                await _context.Entry(user).ReloadAsync();

                // Mark user as active on login
                user.Status = "Active";
                await _userManager.UpdateAsync(user);

                await LogActivity(user.Id, "Login", "System Login");
                return await _userManager.IsInRoleAsync(user, "Student")
                    ? RedirectToAction("Repository")
                    : RedirectToAction("Dashboard");
            }

            ViewBag.Error = "Invalid email or password.";
            return View();
        }

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            ViewBag.GoogleAuthEnabled = _googleAuthEnabled;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            ViewBag.GoogleAuthEnabled = _googleAuthEnabled;
            ViewBag.Email = email;

            if (string.IsNullOrWhiteSpace(email))
            {
                ViewBag.Error = "Please enter your email address.";
                return View();
            }

            if (!_emailService.IsConfigured)
            {
                ViewBag.Error = "Password reset email is not configured yet. Add SMTP settings first.";
                return View();
            }

            var user = await _userManager.FindByEmailAsync(email.Trim());
            if (user != null)
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
                var resetUrl = Url.Action(
                    "ResetPassword",
                    "Home",
                    new { email = user.Email, token = encodedToken },
                    protocol: Request.Scheme);

                if (!string.IsNullOrWhiteSpace(resetUrl))
                {
                    var safeName = string.IsNullOrWhiteSpace(user.FirstName) ? "there" : user.FirstName;
                    var body = $@"
                        <div style=""font-family:Segoe UI,Arial,sans-serif;color:#1e293b;line-height:1.6"">
                            <h2 style=""margin-bottom:12px;"">Reset your LearnLink password</h2>
                            <p>Hello {safeName},</p>
                            <p>We received a request to reset your password. Click the button below to choose a new one.</p>
                            <p style=""margin:24px 0;"">
                                <a href=""{resetUrl}"" style=""background:#3B7DD8;color:#fff;text-decoration:none;padding:12px 20px;border-radius:10px;display:inline-block;font-weight:600;"">Reset Password</a>
                            </p>
                            <p>If you did not request this, you can safely ignore this email.</p>
                            <p style=""font-size:13px;color:#64748b;"">If the button does not work, copy and paste this link into your browser:<br>{resetUrl}</p>
                        </div>";

                    await _emailService.SendAsync(user.Email!, "Reset your LearnLink password", body);
                }
            }

            TempData["SuccessMessage"] = "If an account with that email exists, a password reset link has been sent.";
            return RedirectToAction("ForgotPassword");
        }

        [HttpGet]
        public IActionResult ResetPassword(string email, string token)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            {
                TempData["ErrorMessage"] = "Invalid or expired password reset link.";
                return RedirectToAction("ForgotPassword");
            }

            ViewBag.Email = email;
            ViewBag.Token = token;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> ResetPassword(string email, string token, string password, string confirmPassword)
        {
            ViewBag.Email = email;
            ViewBag.Token = token;

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "All fields are required.";
                return View();
            }

            if (password != confirmPassword)
            {
                ViewBag.Error = "Passwords do not match.";
                return View();
            }

            string decodedToken;
            try
            {
                decodedToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
            }
            catch
            {
                ViewBag.Error = "Invalid or expired password reset link.";
                return View();
            }

            var user = await _userManager.FindByEmailAsync(email.Trim());
            if (user == null)
            {
                TempData["SuccessMessage"] = "Password reset successfully. You can now sign in.";
                return RedirectToAction("Login");
            }

            var result = await _userManager.ResetPasswordAsync(user, decodedToken, password);
            if (result.Succeeded)
            {
                TempData["SuccessMessage"] = "Password reset successfully. You can now sign in.";
                return RedirectToAction("Login");
            }

            ViewBag.Error = string.Join(" ", result.Errors.Select(e => e.Description));
            return View();
        }

        public async Task<IActionResult> Register()
        {
            // Load available schools for the dropdown/info
            var schools = await _context.Schools.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
            ViewBag.Schools = schools;
            ViewBag.GoogleAuthEnabled = _googleAuthEnabled;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(string firstName, string middleName, string lastName, string email, string password, string confirmPassword, int schoolId, string gradeOrPosition)
        {
            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName) ||
                string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "All fields are required.";
                ViewBag.Schools = await _context.Schools.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
                return View();
            }

            if (password != confirmPassword)
            {
                ViewBag.Error = "Passwords do not match.";
                ViewBag.Schools = await _context.Schools.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
                return View();
            }

            if (schoolId <= 0)
            {
                ViewBag.Error = "Please select your school.";
                ViewBag.Schools = await _context.Schools.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
                return View();
            }

            // Validate school exists and is active
            var school = await _context.Schools.FirstOrDefaultAsync(s => s.SchoolId == schoolId && s.IsActive);
            if (school == null)
            {
                ViewBag.Error = "Invalid school selected. Please try again.";
                ViewBag.Schools = await _context.Schools.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
                return View();
            }

            var initials = (firstName.Substring(0, 1) + lastName.Substring(0, 1)).ToUpper();

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FirstName = firstName.Trim(),
                MiddleName = string.IsNullOrWhiteSpace(middleName) ? null : middleName.Trim(),
                LastName = lastName.Trim(),
                Initials = initials,
                GradeOrPosition = gradeOrPosition?.Trim() ?? "",
                AvatarColor = "background: linear-gradient(135deg, #6366f1, #4f46e5)", 
                Status = "Active",
                DateCreated = DateTime.Now,
                SchoolId = school.SchoolId
            };

            var result = await _userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                // Every user registers as a Student (Registered User) by default
                await _userManager.AddToRoleAsync(user, "Student");
                await LogActivity(user.Id, "Register", "New account created");
                
                // Sign out old session (if admin testing), then sign in new student
                if (_signInManager.IsSignedIn(User))
                {
                    await _signInManager.SignOutAsync();
                }
                await _signInManager.SignInAsync(user, isPersistent: false);
                
                TempData["SuccessMessage"] = "Account created successfully! Welcome to LearnLink.";
                return RedirectToAction("Repository");
            }

            ViewBag.Error = string.Join(" ", result.Errors.Select(e => e.Description));
            return View();
        }

        // ==================== Google Authentication ====================

        /// <summary>
        /// Initiates Google Sign-In flow. The returnUrl controls where the user
        /// lands after completing the profile (Login vs Register).
        /// </summary>
        [HttpPost]
        public IActionResult GoogleLogin(string returnUrl = "/Home/Register")
        {
            var redirectUrl = Url.Action("GoogleCallback", "Home", new { returnUrl });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties("Google", redirectUrl);
            return new ChallengeResult("Google", properties);
        }

        /// <summary>
        /// Google redirects here after the user consents. We check if the Google
        /// account already has a linked user:
        ///  - YES → sign them in directly
        ///  - NO  → redirect to CompleteProfile so they fill in missing details
        /// </summary>
        public async Task<IActionResult> GoogleCallback(string? returnUrl)
        {
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                TempData["ErrorMessage"] = "Google sign-in failed. Please try again.";
                return RedirectToAction("Login");
            }

            // Try to sign in with existing external login link
            var signInResult = await _signInManager.ExternalLoginSignInAsync(
                info.LoginProvider, info.ProviderKey, isPersistent: false);

            if (signInResult.Succeeded)
            {
                // Existing user — sign in and redirect
                var existingUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
                if (existingUser != null)
                {
                    await _context.Entry(existingUser).ReloadAsync();
                    existingUser.Status = "Active";
                    await _userManager.UpdateAsync(existingUser);
                    await LogActivity(existingUser.Id, "Login", "Google Sign-In");

                    return await _userManager.IsInRoleAsync(existingUser, "Student")
                        ? RedirectToAction("Repository")
                        : RedirectToAction("Dashboard");
                }
            }

            // New Google user — check if email already exists
            var email = info.Principal.FindFirstValue(System.Security.Claims.ClaimTypes.Email) ?? "";
            var googleFirstName = info.Principal.FindFirstValue(System.Security.Claims.ClaimTypes.GivenName) ?? "";
            var googleLastName = info.Principal.FindFirstValue(System.Security.Claims.ClaimTypes.Surname) ?? "";

            // If a user with this email already exists, require password confirmation before linking
            var userByEmail = await _userManager.FindByEmailAsync(email);
            if (userByEmail != null)
            {
                // Check if suspended
                if (userByEmail.Status == "Suspended")
                {
                    TempData["ErrorMessage"] = "Your account has been suspended. Please contact the administrator.";
                    return RedirectToAction("Login");
                }

                // Store Google info and redirect to password confirmation page
                TempData["LinkGoogleEmail"] = email;
                TempData["LinkGoogleProviderKey"] = info.ProviderKey;
                TempData["LinkGoogleLoginProvider"] = info.LoginProvider;
                return RedirectToAction("ConfirmLinkGoogle");
            }

            // Brand-new user — redirect to CompleteProfile to fill in remaining info
            // Store Google info in TempData so CompleteProfile can pre-fill
            TempData["GoogleEmail"] = email;
            TempData["GoogleFirstName"] = googleFirstName;
            TempData["GoogleLastName"] = googleLastName;
            TempData["GoogleProviderKey"] = info.ProviderKey;
            TempData["GoogleLoginProvider"] = info.LoginProvider;

            return RedirectToAction("CompleteProfile");
        }

        // ==================== Google Account Linking (Password Confirmation) ====================

        /// <summary>
        /// Shows a password confirmation form when a Google sign-in matches an
        /// existing email that was registered manually.
        /// </summary>
        public IActionResult ConfirmLinkGoogle()
        {
            if (TempData.Peek("LinkGoogleEmail") == null)
                return RedirectToAction("Login");

            ViewBag.LinkEmail = TempData.Peek("LinkGoogleEmail");
            return View();
        }

        /// <summary>
        /// Verifies the user's existing password, then links the Google login
        /// to their account and signs them in.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> ConfirmLinkGoogle(string password)
        {
            var email = TempData.Peek("LinkGoogleEmail")?.ToString();
            var providerKey = TempData.Peek("LinkGoogleProviderKey")?.ToString();
            var loginProvider = TempData.Peek("LinkGoogleLoginProvider")?.ToString();

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(providerKey) || string.IsNullOrEmpty(loginProvider))
            {
                TempData["ErrorMessage"] = "Session expired. Please try signing in with Google again.";
                return RedirectToAction("Login");
            }

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                TempData["ErrorMessage"] = "Account not found.";
                return RedirectToAction("Login");
            }

            if (!await _userManager.HasPasswordAsync(user))
            {
                ViewBag.Error = "This account does not have a local password yet. Use Forgot password to create one, then try linking again.";
                ViewBag.LinkEmail = email;
                return View();
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "Please enter your password.";
                ViewBag.LinkEmail = email;
                return View();
            }

            // Verify password
            var passwordMatches = await _userManager.CheckPasswordAsync(user, password);
            if (!passwordMatches)
            {
                ViewBag.Error = "Incorrect password. Please try again.";
                ViewBag.LinkEmail = email;
                return View();
            }

            var existingLogins = await _userManager.GetLoginsAsync(user);
            if (existingLogins.Any(login => login.LoginProvider == loginProvider && login.ProviderKey == providerKey))
            {
                await _signInManager.SignInAsync(user, isPersistent: false);
                TempData.Remove("LinkGoogleEmail");
                TempData.Remove("LinkGoogleProviderKey");
                TempData.Remove("LinkGoogleLoginProvider");
                TempData["SuccessMessage"] = "Your Google account is already linked.";
                return await _userManager.IsInRoleAsync(user, "Student")
                    ? RedirectToAction("Repository")
                    : RedirectToAction("Dashboard");
            }

            // Link Google login and sign in
            var loginInfo = new UserLoginInfo(loginProvider, providerKey, "Google");
            var addLoginResult = await _userManager.AddLoginAsync(user, loginInfo);
            if (!addLoginResult.Succeeded)
            {
                ViewBag.Error = string.Join(" ", addLoginResult.Errors.Select(e => e.Description));
                ViewBag.LinkEmail = email;
                return View();
            }

            await _signInManager.SignInAsync(user, isPersistent: false);

            user.Status = "Active";
            await _userManager.UpdateAsync(user);
            await LogActivity(user.Id, "Login", "Google Sign-In (linked)");

            TempData.Remove("LinkGoogleEmail");
            TempData.Remove("LinkGoogleProviderKey");
            TempData.Remove("LinkGoogleLoginProvider");

            TempData["SuccessMessage"] = "Google account linked successfully!";
            return await _userManager.IsInRoleAsync(user, "Student")
                ? RedirectToAction("Repository")
                : RedirectToAction("Dashboard");
        }

        /// <summary>
        /// Shows the "Complete your profile" form after Google Sign-In for a new user.
        /// </summary>
        public async Task<IActionResult> CompleteProfile()
        {
            // If no Google data present, redirect to Register
            if (TempData.Peek("GoogleEmail") == null)
                return RedirectToAction("Register");

            ViewBag.GoogleEmail = TempData.Peek("GoogleEmail");
            ViewBag.GoogleFirstName = TempData.Peek("GoogleFirstName");
            ViewBag.GoogleLastName = TempData.Peek("GoogleLastName");
            ViewBag.Schools = await _context.Schools.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
            return View();
        }

        /// <summary>
        /// Handles the CompleteProfile form submission: creates the user account,
        /// links the Google login, assigns Student role, and signs them in.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CompleteProfile(
            string firstName, string middleName, string lastName,
            string email, string password, string confirmPassword,
            int schoolId, string gradeOrPosition)
        {
            var googleProviderKey = TempData["GoogleProviderKey"]?.ToString();
            var googleLoginProvider = TempData["GoogleLoginProvider"]?.ToString();

            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName) ||
                string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "All fields are required.";
                ViewBag.GoogleEmail = email;
                ViewBag.GoogleFirstName = firstName;
                ViewBag.GoogleLastName = lastName;
                ViewBag.Schools = await _context.Schools.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
                // Re-store Google tokens for next POST
                TempData["GoogleProviderKey"] = googleProviderKey;
                TempData["GoogleLoginProvider"] = googleLoginProvider;
                return View();
            }

            if (password != confirmPassword)
            {
                ViewBag.Error = "Passwords do not match.";
                ViewBag.GoogleEmail = email;
                ViewBag.GoogleFirstName = firstName;
                ViewBag.GoogleLastName = lastName;
                ViewBag.Schools = await _context.Schools.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
                TempData["GoogleProviderKey"] = googleProviderKey;
                TempData["GoogleLoginProvider"] = googleLoginProvider;
                return View();
            }

            if (schoolId <= 0)
            {
                ViewBag.Error = "Please select your school.";
                ViewBag.GoogleEmail = email;
                ViewBag.GoogleFirstName = firstName;
                ViewBag.GoogleLastName = lastName;
                ViewBag.Schools = await _context.Schools.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
                TempData["GoogleProviderKey"] = googleProviderKey;
                TempData["GoogleLoginProvider"] = googleLoginProvider;
                return View();
            }

            var school = await _context.Schools.FirstOrDefaultAsync(s => s.SchoolId == schoolId && s.IsActive);
            if (school == null)
            {
                ViewBag.Error = "Invalid school selected. Please try again.";
                ViewBag.GoogleEmail = email;
                ViewBag.GoogleFirstName = firstName;
                ViewBag.GoogleLastName = lastName;
                ViewBag.Schools = await _context.Schools.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
                TempData["GoogleProviderKey"] = googleProviderKey;
                TempData["GoogleLoginProvider"] = googleLoginProvider;
                return View();
            }

            var initials = (firstName.Substring(0, 1) + lastName.Substring(0, 1)).ToUpper();

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = firstName.Trim(),
                MiddleName = string.IsNullOrWhiteSpace(middleName) ? null : middleName.Trim(),
                LastName = lastName.Trim(),
                Initials = initials,
                GradeOrPosition = gradeOrPosition?.Trim() ?? "",
                AvatarColor = "background: linear-gradient(135deg, #6366f1, #4f46e5)",
                Status = "Active",
                DateCreated = DateTime.Now,
                SchoolId = school.SchoolId
            };

            var result = await _userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                // Link Google login if provider info is available
                if (!string.IsNullOrEmpty(googleProviderKey) && !string.IsNullOrEmpty(googleLoginProvider))
                {
                    var loginInfo = new Microsoft.AspNetCore.Identity.UserLoginInfo(
                        googleLoginProvider, googleProviderKey, "Google");
                    await _userManager.AddLoginAsync(user, loginInfo);
                }

                await _userManager.AddToRoleAsync(user, "Student");
                await LogActivity(user.Id, "Register", "Google Sign-Up");

                if (_signInManager.IsSignedIn(User))
                    await _signInManager.SignOutAsync();

                await _signInManager.SignInAsync(user, isPersistent: false);
                TempData["SuccessMessage"] = "Account created successfully! Welcome to LearnLink.";
                return RedirectToAction("Repository");
            }

            ViewBag.Error = string.Join(" ", result.Errors.Select(e => e.Description));
            ViewBag.GoogleEmail = email;
            ViewBag.GoogleFirstName = firstName;
            ViewBag.GoogleLastName = lastName;
            ViewBag.Schools = await _context.Schools.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
            return View();
        }

        public async Task<IActionResult> Logout()
        {
            // Mark user as inactive on logout
            var currentUser = await GetCurrentUserAsync();
            if (currentUser != null)
            {
                currentUser.Status = "Inactive";
                await _userManager.UpdateAsync(currentUser);
            }

            await _signInManager.SignOutAsync();
            return RedirectToAction("Login");
        }

        public IActionResult AccessDenied() => View();

        // ==================== Profile ====================

        [Authorize]
        public async Task<IActionResult> Profile()
        {
            ViewData["ActivePage"] = "Profile";
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            var roles = await _userManager.GetRolesAsync(currentUser);
            var userRole = roles.FirstOrDefault() ?? "Student";

            // Personal info
            ViewBag.ProfileUser = new UserViewModel
            {
                Name = currentUser.FullName,
                Email = currentUser.Email ?? "",
                Initials = currentUser.Initials,
                AvatarColor = currentUser.AvatarColor,
                Role = userRole,
                RoleBadgeClass = GetRoleBadge(userRole),
                GradeOrPosition = currentUser.GradeOrPosition,
                Status = currentUser.Status,
                StatusBadgeClass = GetStatusBadge(currentUser.Status),
                JoinedAt = currentUser.DateCreated,
                FirstName = currentUser.FirstName,
                LastName = currentUser.LastName,
                MiddleName = currentUser.MiddleName
            };

            // My Lessons Learned
            var myLessons = await _context.LessonsLearned
                .Include(l => l.User)
                .Include(l => l.Resource)
                .Where(l => l.UserId == currentUser.Id)
                .OrderByDescending(l => l.DateSubmitted)
                .ToListAsync();
            ViewBag.MyLessons = myLessons.Select(l => MapLesson(l)).ToList();

            // Discussions I created
            var myDiscussions = await _context.Discussions
                .Include(d => d.User)
                .Include(d => d.Posts)
                .Where(d => d.UserId == currentUser.Id)
                .OrderByDescending(d => d.DateCreated)
                .ToListAsync();
            ViewBag.MyDiscussions = myDiscussions.Select(d => MapDiscussion(d)).ToList();

            // Discussions I joined (replied to, but didn't create)
            var joinedDiscussionIds = await _context.DiscussionPosts
                .Where(dp => dp.UserId == currentUser.Id)
                .Select(dp => dp.DiscussionId)
                .Distinct()
                .ToListAsync();

            var joinedDiscussions = await _context.Discussions
                .Include(d => d.User)
                .Include(d => d.Posts)
                .Where(d => joinedDiscussionIds.Contains(d.DiscussionId) && d.UserId != currentUser.Id)
                .OrderByDescending(d => d.DateCreated)
                .ToListAsync();
            ViewBag.JoinedDiscussions = joinedDiscussions.Select(d => MapDiscussion(d)).ToList();

            // Resources I liked
            var likedResourceIds = await _context.Likes
                .Where(l => l.UserId == currentUser.Id && l.TargetType == "Resource")
                .Select(l => l.TargetId)
                .ToListAsync();

            var likedResources = await _context.Resources
                .Include(r => r.User)
                .Where(r => likedResourceIds.Contains(r.ResourceId))
                .OrderByDescending(r => r.DateUploaded)
                .ToListAsync();
            ViewBag.LikedResources = likedResources.Select(MapResource).ToList();

            return View();
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> ToggleStatus()
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            currentUser.Status = currentUser.Status == "Active" ? "Inactive" : "Active";
            await _userManager.UpdateAsync(currentUser);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Your status has been changed to {currentUser.Status}.";
            return RedirectToAction("Profile");
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> UpdateProfile(string firstName, string lastName, string? middleName, string? gradeOrPosition)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            {
                TempData["ErrorMessage"] = "First name and last name are required.";
                return RedirectToAction("Profile");
            }

            currentUser.FirstName = firstName.Trim();
            currentUser.LastName = lastName.Trim();
            currentUser.MiddleName = string.IsNullOrWhiteSpace(middleName) ? null : middleName.Trim();
            currentUser.GradeOrPosition = gradeOrPosition?.Trim() ?? "";
            currentUser.Initials = (currentUser.FirstName[..1] + currentUser.LastName[..1]).ToUpper();

            await _userManager.UpdateAsync(currentUser);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Profile updated successfully.";
            return RedirectToAction("Profile");
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            {
                TempData["ErrorMessage"] = "New password must be at least 6 characters.";
                return RedirectToAction("Profile");
            }

            if (newPassword != confirmPassword)
            {
                TempData["ErrorMessage"] = "New password and confirmation do not match.";
                return RedirectToAction("Profile");
            }

            var result = await _userManager.ChangePasswordAsync(currentUser, currentPassword, newPassword);
            if (!result.Succeeded)
            {
                TempData["ErrorMessage"] = string.Join(" ", result.Errors.Select(e => e.Description));
                return RedirectToAction("Profile");
            }

            await _signInManager.RefreshSignInAsync(currentUser);
            TempData["SuccessMessage"] = "Password changed successfully.";
            return RedirectToAction("Profile");
        }

        // ==================== Main Navigation — Authenticated ====================

        [Authorize]
        public async Task<IActionResult> Dashboard()
        {
            if (User.IsInRole("Student"))
                return RedirectToAction("StudentDashboard");
            if (User.IsInRole("Contributor"))
                return RedirectToAction("Repository");

            await AutoRejectExpiredPendingResourcesAsync();

            await LoadSchoolSettingsToViewBag();
            var schoolId = GetEffectiveSchoolId();

            // Resources are auto-filtered by global query filter
            var resources = await _context.Resources.Include(r => r.User).ToListAsync();

            // Users need manual filtering since UserManager doesn't use global filters
            var allUsers = await _userManager.Users.ToListAsync();
            var users = schoolId.HasValue
                ? allUsers.Where(u => u.SchoolId == schoolId.Value).ToList()
                : allUsers;

            // Yesterday snapshots for KPI comparison
            var yesterday = DateTime.Now.Date; // start of today = end of yesterday
            var yesterdayResources = resources.Where(r => r.DateUploaded < yesterday).ToList();
            var yesterdayUsers = users.Where(u => u.DateCreated < yesterday).ToList();
            var yesterdayDiscussionCount = await _context.Discussions.CountAsync(d => d.DateCreated < yesterday);

            ViewBag.Stats = new DashboardStatsViewModel
            {
                TotalResources = resources.Count,
                ActiveUsers = users.Count(u => u.Status == "Active"),
                TotalDownloads = resources.Sum(r => r.DownloadCount),
                ActiveDiscussions = await _context.Discussions.CountAsync(),
                YesterdayResources = yesterdayResources.Count,
                YesterdayActiveUsers = yesterdayUsers.Count(u => u.Status == "Active"),
                YesterdayDownloads = yesterdayResources.Sum(r => r.DownloadCount),
                YesterdayDiscussions = yesterdayDiscussionCount
            };
            ViewBag.RecentResources = resources.Where(r => r.Status == "Published").OrderByDescending(r => r.DateUploaded).Take(5).Select(MapResource).ToList();
            ViewBag.PendingApprovals = resources.Where(r => r.Status == "Pending").OrderBy(r => r.DateUploaded).Select(MapResource).ToList();

            // Recent activity
            var activities = await _context.UserActivityLogs
                .Include(a => a.User)
                .OrderByDescending(a => a.ActivityDate)
                .Take(10)
                .ToListAsync();

            ViewBag.RecentActivity = activities.Select(a => new ActivityViewModel
            {
                User = a.User?.FullName ?? "Unknown",
                UserInitials = a.User?.Initials ?? "?",
                UserColor = a.User?.AvatarColor ?? "",
                Action = a.ActivityType.ToLower() switch
                {
                    "upload" => "uploaded",
                    "comment" => "commented on",
                    "download" => "downloaded",
                    "approve" => "approved",
                    "discussion" => "started discussion",
                    "login" => "logged in",
                    "register" => "registered",
                    _ => a.ActivityType
                },
                Target = a.TargetTitle,
                TimeAgo = GetTimeAgo(a.ActivityDate),
                IconClass = a.ActivityType.ToLower() switch
                {
                    "upload" => "bi-cloud-arrow-up",
                    "comment" => "bi-chat-dots",
                    "download" => "bi-download",
                    "approve" => "bi-check-circle",
                    "discussion" => "bi-chat-square-text",
                    "login" => "bi-box-arrow-in-right",
                    _ => "bi-activity"
                },
                IconColor = a.ActivityType.ToLower() switch
                {
                    "upload" => "text-primary",
                    "comment" => "text-success",
                    "download" => "text-info",
                    "approve" => "text-warning",
                    "discussion" => "text-danger",
                    _ => "text-muted"
                }
            }).ToList();

            return View();
        }

        [Authorize(Roles = "SuperAdmin,Manager")]
        [HttpPost]
        public async Task<IActionResult> ImportGoogleBooks(string subject, string grade)
        {
            if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(grade))
            {
                TempData["ErrorMessage"] = "Subject and Grade Level are required to import books.";
                return RedirectToAction("Settings");
            }

            try
            {
                var searchTerms = $"{subject} {grade} junior high school textbook education";
                var query = Uri.EscapeDataString(searchTerms);
                string cacheKey = $"BookImport_{query}";

                string? json = null;
                string source = "cache";

                // 1. Check if the response is cached
                if (_cache.TryGetValue(cacheKey, out string? cachedJson) && !string.IsNullOrEmpty(cachedJson))
                {
                    json = cachedJson;
                }
                else
                {
                    await _googleBooksSemaphore.WaitAsync();
                    try
                    {
                        var timeSinceLast = DateTime.UtcNow - _lastGoogleBooksRequest;
                        if (timeSinceLast.TotalSeconds < 1.0)
                            await Task.Delay(TimeSpan.FromSeconds(1.0 - timeSinceLast.TotalSeconds));

                        using var client = new HttpClient();
                        client.Timeout = TimeSpan.FromSeconds(15);
                        client.DefaultRequestHeaders.Add("User-Agent", "LearnLink/1.0");

                        // --- Try Google Books API first ---
                        var apiKey = _configuration["GoogleBooks:ApiKey"];
                        var googleUrl = $"https://www.googleapis.com/books/v1/volumes?q={query}&maxResults=10&printType=books&langRestrict=en&filter=free-ebooks";
                        if (!string.IsNullOrEmpty(apiKey))
                            googleUrl += $"&key={apiKey}";

                        bool googleSuccess = false;
                        try
                        {
                            var response = await client.GetAsync(googleUrl);
                            _lastGoogleBooksRequest = DateTime.UtcNow;

                            if (response.IsSuccessStatusCode)
                            {
                                json = await response.Content.ReadAsStringAsync();
                                source = "google";
                                googleSuccess = true;
                            }
                        }
                        catch { /* Google Books failed, will try fallback */ }

                        // --- Fallback: Open Library API (free, no key required) ---
                        if (!googleSuccess)
                        {
                            var openLibUrl = $"https://openlibrary.org/search.json?q={query}&limit=10&has_fulltext=true&ebook_access=public&fields=key,title,author_name,first_sentence,subject,cover_i,number_of_pages_median,first_publish_year,ebook_access,public_scan_b,ia";
                            var olResponse = await client.GetAsync(openLibUrl);

                            if (olResponse.IsSuccessStatusCode)
                            {
                                json = await olResponse.Content.ReadAsStringAsync();
                                source = "openlibrary";
                            }
                            else
                            {
                                TempData["ErrorMessage"] = "Unable to fetch books from any source. Please try again later.";
                                return RedirectToAction("Settings");
                            }
                        }

                        // Cache successful response for 24 hours
                        if (!string.IsNullOrEmpty(json))
                            _cache.Set(cacheKey, json, TimeSpan.FromHours(24));
                    }
                    finally
                    {
                        _googleBooksSemaphore.Release();
                    }
                }

                if (string.IsNullOrEmpty(json))
                {
                    TempData["ErrorMessage"] = "No response received from book APIs.";
                    return RedirectToAction("Settings");
                }

                // Detect source from cached data if needed
                if (source == "cache")
                {
                    var peek = System.Text.Json.JsonDocument.Parse(json);
                    source = peek.RootElement.TryGetProperty("docs", out _) ? "openlibrary" : "google";
                }

                var currentUser = await GetCurrentUserAsync();
                if (currentUser == null)
                    return Json(new { success = false, message = "Not authenticated." });

                int importCount = 0;

                if (source == "google")
                {
                    importCount = await ImportFromGoogleBooksJson(json, subject, grade, currentUser);
                }
                else
                {
                    importCount = await ImportFromOpenLibraryJson(json, subject, grade, currentUser);
                }

                if (importCount > 0)
                {
                    await _context.SaveChangesAsync();
                    var sourceLabel = source == "google" ? "Google Books" : "Open Library";
                    TempData["SuccessMessage"] = $"Successfully imported {importCount} books for {subject} ({grade}) from {sourceLabel}!";
                }
                else
                {
                    TempData["WarningMessage"] = "No new books were imported. They might already exist or no results were found for this query.";
                }
            }
            catch (TaskCanceledException)
            {
                TempData["ErrorMessage"] = "The request timed out. Please try again.";
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException != null ? ex.InnerException.Message : "";
                TempData["ErrorMessage"] = $"An error occurred while importing books: {ex.Message} {innerMsg}";
            }

            return RedirectToAction("Settings");
        }

        private async Task<int> ImportFromGoogleBooksJson(string json, string subject, string grade, ApplicationUser currentUser)
        {
            var data = System.Text.Json.JsonDocument.Parse(json);
            if (!data.RootElement.TryGetProperty("items", out var items))
                return 0;

            int count = 0;
            foreach (var item in items.EnumerateArray())
            {
                if (!IsGoogleBookFreelyAccessible(item))
                    continue;

                var volumeInfo = item.GetProperty("volumeInfo");
                var title = volumeInfo.TryGetProperty("title", out var t) ? t.GetString() : "Unknown Title";
                if (title != null && title.Length > 95) title = title.Substring(0, 95) + "...";

                if (await _context.Resources.AnyAsync(r => r.Title == title && r.ResourceType == "Book" && r.SchoolId == currentUser.SchoolId))
                    continue;

                var desc = volumeInfo.TryGetProperty("description", out var d) ? d.GetString() : "No description available.";
                if (desc != null && desc.Length > 490) desc = desc.Substring(0, 490) + "...";

                var previewLink = BuildGoogleBooksLink(item, volumeInfo);
                if (string.IsNullOrEmpty(previewLink)) continue;

                _context.Resources.Add(new Resource
                {
                    Title = title ?? "Unknown Book",
                    Description = desc ?? "",
                    Subject = subject,
                    GradeLevel = grade,
                    ResourceType = "Book",
                    FileFormat = "Link",
                    FilePath = previewLink,
                    UserId = currentUser.Id,
                    SchoolId = currentUser.SchoolId,
                    IsSharedCrossSchool = true,
                    Status = "Published",
                    AccessLevel = "Registered",
                    AllowDownloads = false
                });
                count++;
            }
            return count;
        }

        private static string BuildGoogleBooksLink(System.Text.Json.JsonElement item, System.Text.Json.JsonElement volumeInfo)
        {
            if (item.TryGetProperty("id", out var idElement))
            {
                var volumeId = idElement.GetString();
                if (!string.IsNullOrWhiteSpace(volumeId))
                {
                    return $"https://books.google.com/books?id={Uri.EscapeDataString(volumeId)}";
                }
            }

            var rawLink = volumeInfo.TryGetProperty("infoLink", out var infoLinkElement)
                ? infoLinkElement.GetString()
                : volumeInfo.TryGetProperty("previewLink", out var previewLinkElement)
                    ? previewLinkElement.GetString()
                    : null;

            return NormalizeExternalBookLink(rawLink, 500);
        }

        private static bool IsGoogleBookFreelyAccessible(System.Text.Json.JsonElement item)
        {
            if (!item.TryGetProperty("accessInfo", out var accessInfo))
            {
                return false;
            }

            var viewability = accessInfo.TryGetProperty("viewability", out var viewabilityElement)
                ? viewabilityElement.GetString()
                : null;

            var isEmbeddable = accessInfo.TryGetProperty("embeddable", out var embeddableElement)
                && embeddableElement.ValueKind == System.Text.Json.JsonValueKind.True;

            var isPublicDomain = accessInfo.TryGetProperty("publicDomain", out var publicDomainElement)
                && publicDomainElement.ValueKind == System.Text.Json.JsonValueKind.True;

            var pdfAvailable = accessInfo.TryGetProperty("pdf", out var pdfElement)
                && pdfElement.TryGetProperty("isAvailable", out var pdfAvailableElement)
                && pdfAvailableElement.ValueKind == System.Text.Json.JsonValueKind.True;

            var epubAvailable = accessInfo.TryGetProperty("epub", out var epubElement)
                && epubElement.TryGetProperty("isAvailable", out var epubAvailableElement)
                && epubAvailableElement.ValueKind == System.Text.Json.JsonValueKind.True;

            return isEmbeddable
                && (isPublicDomain
                    || string.Equals(viewability, "ALL_PAGES", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(viewability, "FULL_PUBLIC_DOMAIN", StringComparison.OrdinalIgnoreCase)
                    || pdfAvailable
                    || epubAvailable);
        }

        private static string NormalizeExternalBookLink(string? rawLink, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(rawLink))
            {
                return string.Empty;
            }

            if (Uri.TryCreate(rawLink, UriKind.Absolute, out var uri))
            {
                var host = uri.Host.ToLowerInvariant();
                if (host.Contains("books.google."))
                {
                    var googleBookId = ExtractQueryParameter(uri.Query, "id");
                    if (!string.IsNullOrWhiteSpace(googleBookId))
                    {
                        return $"https://books.google.com/books?id={Uri.EscapeDataString(googleBookId)}";
                    }
                }
            }

            return rawLink.Length > maxLength ? rawLink.Substring(0, maxLength) : rawLink;
        }

        private static string? ExtractQueryParameter(string query, string key)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            foreach (var segment in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = segment.Split('=', 2);
                if (parts.Length == 2 && parts[0].Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(parts[1]);
                }
            }

            return null;
        }

        private async Task<int> ImportFromOpenLibraryJson(string json, string subject, string grade, ApplicationUser currentUser)
        {
            var data = System.Text.Json.JsonDocument.Parse(json);
            if (!data.RootElement.TryGetProperty("docs", out var docs))
                return 0;

            int count = 0;
            foreach (var doc in docs.EnumerateArray())
            {
                if (!IsOpenLibraryFreelyAccessible(doc))
                    continue;

                var title = doc.TryGetProperty("title", out var t) ? t.GetString() : "Unknown Title";
                if (title != null && title.Length > 95) title = title.Substring(0, 95) + "...";

                if (await _context.Resources.AnyAsync(r => r.Title == title && r.ResourceType == "Book" && r.SchoolId == currentUser.SchoolId))
                    continue;

                // Build description from available fields
                var desc = "";
                if (doc.TryGetProperty("author_name", out var authors) && authors.GetArrayLength() > 0)
                {
                    var authorList = new List<string>();
                    foreach (var a in authors.EnumerateArray())
                        authorList.Add(a.GetString() ?? "");
                    desc = $"By {string.Join(", ", authorList)}. ";
                }
                if (doc.TryGetProperty("first_sentence", out var sentences) && sentences.ValueKind == System.Text.Json.JsonValueKind.Array && sentences.GetArrayLength() > 0)
                {
                    desc += sentences[0].GetString() ?? "";
                }
                if (string.IsNullOrWhiteSpace(desc)) desc = "No description available.";
                if (desc.Length > 490) desc = desc.Substring(0, 490) + "...";

                var link = BuildOpenLibraryLink(doc);
                if (string.IsNullOrEmpty(link)) continue;

                _context.Resources.Add(new Resource
                {
                    Title = title ?? "Unknown Book",
                    Description = desc,
                    Subject = subject,
                    GradeLevel = grade,
                    ResourceType = "Book",
                    FileFormat = "Link",
                    FilePath = link,
                    UserId = currentUser.Id,
                    SchoolId = currentUser.SchoolId,
                    IsSharedCrossSchool = true,
                    Status = "Published",
                    AccessLevel = "Registered",
                    AllowDownloads = false
                });
                count++;
            }
            return count;
        }

        private static bool IsOpenLibraryFreelyAccessible(System.Text.Json.JsonElement doc)
        {
            var ebookAccess = doc.TryGetProperty("ebook_access", out var ebookAccessElement)
                ? ebookAccessElement.GetString()
                : null;

            var isPublic = string.Equals(ebookAccess, "public", StringComparison.OrdinalIgnoreCase);
            var publicScan = doc.TryGetProperty("public_scan_b", out var publicScanElement)
                && publicScanElement.ValueKind == System.Text.Json.JsonValueKind.True;

            var hasArchiveIdentifier = doc.TryGetProperty("ia", out var iaElement)
                && !string.IsNullOrWhiteSpace(GetFirstArrayString(iaElement));

            return (isPublic || publicScan) && hasArchiveIdentifier;
        }

        private static string BuildOpenLibraryLink(System.Text.Json.JsonElement doc)
        {
            if (doc.TryGetProperty("ia", out var iaElement))
            {
                var archiveIdentifier = GetFirstArrayString(iaElement);
                if (!string.IsNullOrWhiteSpace(archiveIdentifier))
                {
                    return $"https://archive.org/details/{Uri.EscapeDataString(archiveIdentifier)}";
                }
            }

            var key = doc.TryGetProperty("key", out var keyElement) ? keyElement.GetString() : null;
            return string.IsNullOrWhiteSpace(key) ? string.Empty : NormalizeExternalResourceUrl($"https://openlibrary.org{key}");
        }

        private static string GetTimeAgo(DateTime dt)
        {
            var diff = DateTime.Now - dt;
            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} minutes ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} hours ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} days ago";
            return dt.ToString("MMM dd, yyyy");
        }

        // ==================== Student Analytics Dashboard ====================

        [Authorize(Roles = "Student")]
        public async Task<IActionResult> StudentDashboard()
        {
            await LoadSchoolSettingsToViewBag();
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");
            var userId = currentUser.Id;

            // Reading history
            var readingHistory = await _context.ReadingHistories
                .Include(rh => rh.Resource).ThenInclude(r => r!.User)
                .Where(rh => rh.UserId == userId)
                .ToListAsync();

            var resourcesRead = readingHistory.Count;
            var resourcesCompleted = readingHistory.Count(rh => rh.ProgressStatus == "Completed");
            var bookmarks = readingHistory.Count(rh => rh.IsBookmarked);
            var completionPercent = resourcesRead > 0 ? (int)Math.Round((double)resourcesCompleted / resourcesRead * 100) : 0;

            // Lessons submitted
            var lessonsSubmitted = await _context.LessonsLearned.CountAsync(l => l.UserId == userId);

            // Discussions participated (authored or replied)
            var discussionAuthored = await _context.Discussions.CountAsync(d => d.UserId == userId);
            var discussionReplied = await _context.DiscussionPosts.Where(dp => dp.UserId == userId).Select(dp => dp.DiscussionId).Distinct().CountAsync();
            var discussionsParticipated = discussionAuthored + discussionReplied;

            // Subject progress
            var subjectColors = new[] { "#4361ee", "#7209b7", "#f72585", "#4cc9f0", "#06d6a0", "#ffd166", "#ef476f", "#118ab2" };
            var subjectProgress = readingHistory
                .Where(rh => rh.Resource != null)
                .GroupBy(rh => rh.Resource!.Subject)
                .Select((g, i) => new SubjectProgressItem
                {
                    Subject = g.Key,
                    Total = g.Count(),
                    Completed = g.Count(rh => rh.ProgressStatus == "Completed"),
                    Color = subjectColors[i % subjectColors.Length]
                })
                .OrderByDescending(s => s.Percent)
                .ToList();

            // Recent reading
            var recentReading = readingHistory
                .OrderByDescending(rh => rh.LastAccessed)
                .Take(5)
                .Select(rh => new ReadingHistoryViewModel
                {
                    ResourceId = rh.ResourceId,
                    ResourceTitle = rh.Resource?.Title ?? "",
                    ResourceAuthor = rh.Resource?.User?.FullName ?? "Unknown",
                    ResourceSubject = rh.Resource?.Subject ?? "",
                    ResourceGrade = rh.Resource?.GradeLevel ?? "",
                    ResourceFormat = rh.Resource?.FileFormat ?? "",
                    Title = rh.Resource?.Title ?? "",
                    Subject = rh.Resource?.Subject ?? "",
                    Author = rh.Resource?.User?.FullName ?? "Unknown",
                    FileFormat = rh.Resource?.FileFormat ?? "",
                    IconClass = GetIconClass(rh.Resource?.FileFormat ?? ""),
                    IconColor = GetIconColor(rh.Resource?.FileFormat ?? ""),
                    IconBg = GetIconBg(rh.Resource?.FileFormat ?? ""),
                    Progress = rh.ProgressPercent,
                    ProgressPercent = rh.ProgressPercent,
                    LastPosition = rh.LastPosition ?? "",
                    ViewedAt = rh.LastAccessed,
                    LastAccessedDate = rh.LastAccessed,
                    CompletedDate = rh.CompletedDate,
                    IsCompleted = rh.ProgressStatus == "Completed",
                    IsBookmarked = rh.IsBookmarked,
                    Status = rh.ProgressStatus
                })
                .ToList();

            // Recommended resources — KNN personalized recommendations
            var readResourceIds = readingHistory.Select(rh => rh.ResourceId).ToHashSet();
            var recommended = new List<Resource>();
            try
            {
                var recIds = await _recommendationService.GetPersonalizedRecommendationsAsync(userId, 8, GetEffectiveSchoolId());
                if (recIds.Any())
                {
                    var recResources = await _context.Resources
                        .Include(r => r.User)
                        .Where(r => recIds.Contains(r.ResourceId) && r.Status == "Published" && !readResourceIds.Contains(r.ResourceId))
                        .ToListAsync();
                    // Preserve KNN ranking order
                    recommended = recIds
                        .Select(rid => recResources.FirstOrDefault(r => r.ResourceId == rid))
                        .Where(r => r != null)
                        .Cast<Resource>()
                        .ToList();
                }
            }
            catch { /* fallback below */ }

            // Fallback: popular resources if KNN returns nothing
            if (!recommended.Any())
            {
                recommended = await _context.Resources
                    .Include(r => r.User)
                    .Where(r => r.Status == "Published" && !readResourceIds.Contains(r.ResourceId))
                    .OrderByDescending(r => r.DownloadCount + r.ViewCount)
                    .Take(6)
                    .ToListAsync();
            }

            // Streaks (consecutive days with reading activity)
            var activityDates = readingHistory
                .Select(rh => rh.LastAccessed.Date)
                .Distinct()
                .OrderByDescending(d => d)
                .ToList();

            int currentStreak = 0, bestStreak = 0, streak = 0;
            for (int i = 0; i < activityDates.Count; i++)
            {
                if (i == 0)
                {
                    // Count today/yesterday as start
                    if ((DateTime.Now.Date - activityDates[i]).TotalDays <= 1)
                        streak = 1;
                    else
                        break;
                }
                else if ((activityDates[i - 1] - activityDates[i]).TotalDays == 1)
                {
                    streak++;
                }
                else
                {
                    break;
                }
            }
            currentStreak = streak;
            // Best streak
            streak = 1;
            bestStreak = activityDates.Count > 0 ? 1 : 0;
            for (int i = 1; i < activityDates.Count; i++)
            {
                if ((activityDates[i - 1] - activityDates[i]).TotalDays == 1)
                {
                    streak++;
                    bestStreak = Math.Max(bestStreak, streak);
                }
                else
                {
                    streak = 1;
                }
            }

            // Recent activity log
            var activities = await _context.UserActivityLogs
                .Include(a => a.User)
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.ActivityDate)
                .Take(10)
                .ToListAsync();

            var recentActivity = activities.Select(a => new ActivityViewModel
            {
                User = a.User?.FullName ?? "Unknown",
                UserInitials = a.User?.Initials ?? "?",
                UserColor = a.User?.AvatarColor ?? "",
                Action = a.ActivityType.ToLower() switch
                {
                    "upload" => "uploaded",
                    "comment" => "commented on",
                    "download" => "downloaded",
                    "view" => "viewed",
                    "discussion" => "started discussion",
                    _ => a.ActivityType
                },
                Target = a.TargetTitle,
                TimeAgo = GetTimeAgo(a.ActivityDate),
                IconClass = a.ActivityType.ToLower() switch
                {
                    "upload" => "bi-cloud-arrow-up",
                    "comment" => "bi-chat-dots",
                    "download" => "bi-download",
                    "view" => "bi-eye",
                    "discussion" => "bi-chat-square-text",
                    _ => "bi-activity"
                },
                IconColor = a.ActivityType.ToLower() switch
                {
                    "upload" => "text-primary",
                    "comment" => "text-success",
                    "download" => "text-info",
                    "view" => "text-secondary",
                    "discussion" => "text-danger",
                    _ => "text-muted"
                }
            }).ToList();

            // Calculate Weekly Resources Completed
            var startOfWeek = DateTime.Now.Date.AddDays(-(int)DateTime.Now.DayOfWeek);
            var weeklyResourcesCompleted = readingHistory
                .Count(rh => rh.ProgressStatus == "Completed" && 
                             rh.CompletedDate.HasValue && 
                             rh.CompletedDate.Value.Date >= startOfWeek);

            // Fetch Top 4 Bookmarked Resources for Quick Access
            var recentBookmarks = await _context.ReadingHistories
                .Include(rh => rh.Resource)
                .Include(rh => rh.Resource!.User)
                .Where(rh => rh.UserId == userId && rh.IsBookmarked && rh.Resource!.Status == "Published")
                .OrderByDescending(rh => rh.LastAccessed)
                .Take(4)
                .Select(rh => MapResource(rh.Resource!))
                .ToListAsync();

            // Fetch Top 4 Trending Resources (Based on ViewCount + DownloadCount)
            var schoolId = GetEffectiveSchoolId();
            var trendingResources = await _context.Resources
                .Include(r => r.User)
                .Where(r => r.Status == "Published" && (r.IsSharedCrossSchool || r.SchoolId == schoolId))
                .OrderByDescending(r => r.ViewCount + r.DownloadCount)
                .Take(4)
                .Select(r => MapResource(r))
                .ToListAsync();

            var model = new StudentDashboardViewModel
            {
                ResourcesRead = resourcesRead,
                ResourcesCompleted = resourcesCompleted,
                LessonsSubmitted = lessonsSubmitted,
                DiscussionsParticipated = discussionsParticipated,
                CompletionPercent = completionPercent,
                Bookmarks = bookmarks,
                SubjectProgress = subjectProgress,
                RecentReading = recentReading,
                RecommendedResources = recommended.Select(MapResource).ToList(),
                TrendingResources = trendingResources,
                RecentBookmarks = recentBookmarks,
                RecentActivity = recentActivity,
                CurrentStreak = currentStreak,
                BestStreak = bestStreak,
                WeeklyResourcesCompleted = weeklyResourcesCompleted,
                WeeklyResourceGoal = 3
            };

            return View(model);
        }

        [Authorize]
        public async Task<IActionResult> Repository(string? search, string? subject, string? grade, string? type, string? sort, int page = 1, int pageSize = 12)
        {
            await LoadSchoolSettingsToViewBag();
            var currentUser = await GetCurrentUserAsync();
            var baseQuery = BuildAccessiblePublishedResourceQuery(currentUser);

            bool isFiltered = !string.IsNullOrWhiteSpace(search) || !string.IsNullOrWhiteSpace(subject) ||
                              !string.IsNullOrWhiteSpace(grade) || !string.IsNullOrWhiteSpace(type);

            // ── Browse Mode (no filters): Group by subject for carousels ──
            if (!isFiltered)
            {
                // KNN personalized recommendations row
                var knnRecommended = new List<ResourceViewModel>();
                var currentUserForRec = await GetCurrentUserAsync();
                if (currentUserForRec != null)
                {
                    try
                    {
                        var recIds = await _recommendationService.GetPersonalizedRecommendationsAsync(currentUserForRec.Id, 12, GetEffectiveSchoolId());
                        if (recIds.Any())
                        {
                            var recResources = await baseQuery
                                .Where(r => recIds.Contains(r.ResourceId))
                                .ToListAsync();
                            knnRecommended = recIds
                                .Select(rid => recResources.FirstOrDefault(r => r.ResourceId == rid))
                                .Where(r => r != null)
                                .Select(r => MapResource(r!))
                                .ToList();
                        }
                    }
                    catch { /* fallback to empty */ }
                }
                ViewBag.KnnRecommendations = knnRecommended;

                var allSubjects = new List<string> { "Mathematics", "Science", "English", "Filipino", "Araling Panlipunan", "History", "MAPEH", "TLE", "Values Education" };
                var subjectGroups = new List<dynamic>();
                foreach (var subj in allSubjects)
                {
                    var subjectResources = await baseQuery
                        .Where(r => r.Subject == subj)
                        .OrderByDescending(r => r.ViewCount + r.DownloadCount)
                        .Take(12)
                        .ToListAsync();
                    if (subjectResources.Any())
                    {
                        subjectGroups.Add(new { Subject = subj, Resources = subjectResources.Select(MapResource).ToList() });
                    }
                }
                ViewBag.BrowseMode = true;
                ViewBag.SubjectGroups = subjectGroups;

                // Also provide recently added (all subjects) for a top carousel
                var recentResources = await baseQuery
                    .OrderByDescending(r => r.DateUploaded)
                    .Take(12)
                    .ToListAsync();
                ViewBag.RecentResources = recentResources.Select(MapResource).ToList();
            }
            else
            {
                ViewBag.BrowseMode = false;
            }

            // ── Filtered/Paginated query (always computed for filter mode or as fallback) ──
            var query = baseQuery;

            // Server-side filters
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(r => r.Title.Contains(search) || r.Description.Contains(search) || r.Subject.Contains(search));
            if (!string.IsNullOrWhiteSpace(subject))
                query = query.Where(r => r.Subject == subject);
            if (!string.IsNullOrWhiteSpace(grade))
                query = query.Where(r => r.GradeLevel == grade);
            if (!string.IsNullOrWhiteSpace(type))
                query = query.Where(r => r.FileFormat == type);

            // Sort
            query = sort switch
            {
                "popular" => query.OrderByDescending(r => r.ViewCount),
                "downloads" => query.OrderByDescending(r => r.DownloadCount),
                "rating" => query.OrderByDescending(r => r.Rating),
                "title" => query.OrderBy(r => r.Title),
                _ => query.OrderByDescending(r => r.DateUploaded)
            };

            var totalCount = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            ViewBag.Resources = items.Select(MapResource).ToList();
            ViewBag.CurrentSearch = search ?? "";
            ViewBag.CurrentSubject = subject ?? "";
            ViewBag.CurrentGrade = grade ?? "";
            ViewBag.CurrentType = type ?? "";
            ViewBag.CurrentSort = sort ?? "recent";
            ViewBag.TotalResourceCount = await baseQuery.CountAsync();
            ViewBag.Pagination = new {
                PageIndex = page,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                TotalCount = totalCount,
                PageSize = pageSize,
                HasPreviousPage = page > 1,
                HasNextPage = page < (int)Math.Ceiling(totalCount / (double)pageSize),
                StartPage = Math.Max(1, page - 2),
                EndPage = Math.Min((int)Math.Ceiling(totalCount / (double)pageSize), page + 2),
                BaseUrl = Url.Action("Repository", "Home"),
                QueryParams = BuildQueryString(search, subject, grade, type, sort)
            };
            return View();
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> Tag(string tag)
        {
            await LoadSchoolSettingsToViewBag();

            if (string.IsNullOrWhiteSpace(tag))
                return RedirectToAction("Repository");

            var currentUser = await GetCurrentUserAsync();
            var normalizedTag = tag.Trim();
            var normalizedTagLower = normalizedTag.ToLowerInvariant();

            // --- Resources matching by tag or subject ---
            var matchingTagResourceIds = _context.ResourceTags
                .Where(rt => rt.Tag != null && rt.Tag.TagName.ToLower() == normalizedTagLower)
                .Select(rt => rt.ResourceId);

            var resources = await BuildAccessiblePublishedResourceQuery(currentUser)
                .Where(r => r.Subject.ToLower() == normalizedTagLower || matchingTagResourceIds.Contains(r.ResourceId))
                .OrderByDescending(r => r.DateUploaded)
                .ToListAsync();

            var mappedResources = resources.Select(MapResource).ToList();
            await PopulateResourceMetadataAsync(mappedResources);

            // --- Discussions matching by tags or title ---
            var discussions = await _context.Discussions
                .Include(d => d.User)
                .Include(d => d.Posts)
                .Where(d => d.Tags.ToLower().Contains(normalizedTagLower)
                    || d.Title.ToLower().Contains(normalizedTagLower)
                    || d.Category.ToLower() == normalizedTagLower)
                .OrderByDescending(d => d.DateCreated)
                .Take(20)
                .ToListAsync();

            var likedDiscIds = new HashSet<int>();
            if (currentUser != null)
            {
                likedDiscIds = (await _context.Likes
                    .Where(l => l.UserId == currentUser.Id && l.TargetType == "Discussion")
                    .Select(l => l.TargetId)
                    .ToListAsync()).ToHashSet();
            }
            ViewBag.TagDiscussions = discussions.Select(d => MapDiscussion(d, likedDiscIds.Contains(d.DiscussionId))).ToList();

            // --- Lessons matching by tags, title, or category ---
            var lessons = await _context.LessonsLearned
                .Include(l => l.User)
                .Include(l => l.Resource)
                .Where(l => l.Tags.ToLower().Contains(normalizedTagLower)
                    || l.Title.ToLower().Contains(normalizedTagLower)
                    || l.Category.ToLower() == normalizedTagLower)
                .OrderByDescending(l => l.DateSubmitted)
                .Take(20)
                .ToListAsync();

            var likedLessonIds = new HashSet<int>();
            if (currentUser != null)
            {
                likedLessonIds = (await _context.Likes
                    .Where(l => l.UserId == currentUser.Id && l.TargetType == "Lesson")
                    .Select(l => l.TargetId)
                    .ToListAsync()).ToHashSet();
            }
            ViewBag.TagLessons = lessons.Select(l => MapLesson(l, likedLessonIds.Contains(l.LessonId))).ToList();

            ViewBag.Tag = normalizedTag;
            ViewBag.ResultCount = mappedResources.Count + discussions.Count + lessons.Count;
            ViewBag.Resources = mappedResources;

            return View();
        }

        private string BuildQueryString(string? search, string? subject, string? grade, string? type, string? sort)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(search)) parts.Add($"search={Uri.EscapeDataString(search)}");
            if (!string.IsNullOrWhiteSpace(subject)) parts.Add($"subject={Uri.EscapeDataString(subject)}");
            if (!string.IsNullOrWhiteSpace(grade)) parts.Add($"grade={Uri.EscapeDataString(grade)}");
            if (!string.IsNullOrWhiteSpace(type)) parts.Add($"type={Uri.EscapeDataString(type)}");
            if (!string.IsNullOrWhiteSpace(sort)) parts.Add($"sort={Uri.EscapeDataString(sort)}");
            return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
        }

        [HttpPost]
        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        public async Task<IActionResult> UploadResource(IFormFile? file, string title, string subject, string gradeLevel, string resourceType, string quarter, string description, string tags, string submitType)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            var isDraft = string.Equals(submitType, "Draft", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(title))
            {
                TempData["ErrorMessage"] = "Please provide a resource title.";
                return RedirectToAction("Upload");
            }

            if (!isDraft)
            {
                if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(gradeLevel) || string.IsNullOrWhiteSpace(resourceType) || string.IsNullOrWhiteSpace(description))
                {
                    TempData["ErrorMessage"] = "Please fill in all required fields before submitting.";
                    return RedirectToAction("Upload");
                }

                if (file == null || file.Length == 0)
                {
                    TempData["ErrorMessage"] = "Please select a file to upload.";
                    return RedirectToAction("Upload");
                }
            }

            string externalFileId = string.Empty;
            string externalUrl = string.Empty;
            string fileFormat = string.Empty;
            string fileSize = string.Empty;

            // Upload to Google Drive if file provided
            if (file != null && file.Length > 0)
            {
                using (var stream = file.OpenReadStream())
                {
                    var result = await _storage.UploadAsync(stream, file.FileName, file.ContentType);
                    if (!result.Success)
                    {
                        TempData["ErrorMessage"] = $"File upload failed: {result.Message}";
                        return RedirectToAction("Upload");
                    }

                    externalFileId = result.FileId ?? "";
                    externalUrl = result.WebViewLink ?? result.WebContentLink ?? "";
                    fileFormat = Path.GetExtension(file.FileName).TrimStart('.').ToUpperInvariant();
                    fileSize = result.FileSize ?? "";
                    

                }
            }

            var resource = new Resource
            {
                Title = title,
                Description = description ?? string.Empty,
                Subject = subject ?? string.Empty,
                GradeLevel = gradeLevel ?? string.Empty,
                ResourceType = resourceType ?? string.Empty,
                Quarter = quarter ?? string.Empty,
                FilePath = externalUrl, // Store the external URL
                FileFormat = fileFormat,
                FileSize = fileSize,
                Status = isDraft ? "Draft" : "Pending",
                PendingReviewPreviewedAt = null,
                UserId = currentUser.Id,
                SchoolId = currentUser.SchoolId,
                DateUploaded = DateTime.Now,
            };

            _context.Resources.Add(resource);
            await _context.SaveChangesAsync();

            await LogActivity(currentUser.Id, "Upload", resource.Title, resource.ResourceId);

            TempData["SuccessMessage"] = "Your resource has been uploaded successfully.";
            TempData["SuccessTitle"] = "Upload Complete";
            return RedirectToAction("MyUploads");
        }

        [Authorize]
        public async Task<IActionResult> ResourceDetail(int id)
        {
            var resource = await _context.Resources.Include(r => r.User).FirstOrDefaultAsync(r => r.ResourceId == id);
            if (resource == null)
                return RedirectToAction("Repository");

            var currentUser = await GetCurrentUserAsync();

            // ---- Access Control Enforcement ----
            bool isOwner = currentUser != null && resource.UserId == currentUser.Id;
            bool isAdmin = User.IsInRole("SuperAdmin") || User.IsInRole("Manager");

            if (!isOwner && !isAdmin)
            {
                // Check access duration expiry
                if (resource.AccessDuration == "Custom" && resource.AccessExpiresAt.HasValue && resource.AccessExpiresAt.Value < DateTime.Now)
                {
                    TempData["ErrorMessage"] = "This resource is no longer accessible. The access period has expired.";
                    return RedirectToAction("Repository");
                }

                // Enforce access level
                if (resource.AccessLevel == "Restricted")
                {
                    if (currentUser == null)
                    {
                        return RedirectToAction("Login");
                    }
                    var hasGrant = await _context.ResourceAccessGrants
                        .AnyAsync(g => g.ResourceId == id && g.UserId == currentUser.Id);
                    if (!hasGrant)
                    {
                        TempData["ErrorMessage"] = "You do not have permission to access this resource. It is restricted to selected users only.";
                        return RedirectToAction("Repository");
                    }
                }
                else if (resource.AccessLevel == "Registered")
                {
                    if (currentUser == null)
                    {
                        return RedirectToAction("Login");
                    }
                }
                // "Public" — allow everyone (handled by [Authorize], but also see PublicResourceDetail below)
            }

            // Increment view count
            resource.ViewCount++;
            if (currentUser != null)
            {
                var viewLog = new UserActivityLog
                {
                    UserId = currentUser.Id,
                    ResourceId = resource.ResourceId,
                    ActivityType = "View",
                    TargetTitle = resource.Title,
                    ActivityDate = DateTime.Now
                };
                _context.UserActivityLogs.Add(viewLog);

                // Check for reading history to mark "In Progress"
                var history = await _context.ReadingHistories.FirstOrDefaultAsync(h => h.UserId == currentUser.Id && h.ResourceId == resource.ResourceId);
                if (history != null) {
                    history.LastAccessed = DateTime.Now;
                    // Bump progress to at least 10% when viewing
                    if (history.ProgressPercent < 10)
                        history.ProgressPercent = 10;
                    if (history.ProgressStatus != "Completed")
                        history.ProgressStatus = "In Progress";
                } else {
                    _context.ReadingHistories.Add(new ReadingHistory {
                        UserId = currentUser.Id,
                        ResourceId = resource.ResourceId,
                        LastAccessed = DateTime.Now,
                        ProgressStatus = "In Progress",
                        ProgressPercent = 10
                    });
                }
            }
            await _context.SaveChangesAsync();

            var vm = MapResource(resource);
            if (currentUser != null)
            {
                vm.IsLiked = await _context.Likes.AnyAsync(l => l.UserId == currentUser.Id && l.TargetType == "Resource" && l.TargetId == resource.ResourceId);
                
                var history = await _context.ReadingHistories.FirstOrDefaultAsync(h => h.UserId == currentUser.Id && h.ResourceId == resource.ResourceId);
                vm.IsSaved = history?.IsBookmarked ?? false;
                ViewBag.CurrentProgress = history?.ProgressPercent ?? 10;
                ViewBag.IsCompleted = history?.ProgressStatus == "Completed";
            }

            // Map LikeCount
            vm.LikeCount = await _context.Likes.CountAsync(l => l.TargetType == "Resource" && l.TargetId == resource.ResourceId);

            // Load tags and categories for this resource
            vm.Tags = await _context.ResourceTags
                .Where(rt => rt.ResourceId == resource.ResourceId)
                .Include(rt => rt.Tag)
                .Select(rt => rt.Tag!.TagName)
                .ToListAsync();

            vm.Categories = await _context.ResourceCategoryMaps
                .Where(m => m.ResourceId == resource.ResourceId)
                .Include(m => m.Category)
                .Select(m => m.Category!.CategoryName)
                .ToListAsync();

            ViewBag.Resource = vm;
            ViewBag.CurrentUserId = currentUser?.Id;

            // File preview/download URL
            string fileUrl = string.Empty;
            bool canPreview = false;
            ViewBag.ExternalSourceUrl = string.Empty;
            ViewBag.ExternalPreviewUrl = string.Empty;
            ViewBag.ExternalSourceLabel = string.Empty;

            if (IsLinkFileFormat(resource.FileFormat))
            {
                var externalUrl = NormalizeExternalResourceUrl(resource.FilePath);
                if (!string.IsNullOrEmpty(externalUrl))
                {
                    fileUrl = externalUrl;
                    canPreview = true;
                    ViewBag.ExternalSourceUrl = externalUrl;
                    ViewBag.ExternalPreviewUrl = BuildExternalPreviewUrl(externalUrl) ?? string.Empty;
                    ViewBag.ExternalSourceLabel = GetExternalSourceLabel(externalUrl);
                }
            }
            else if (!string.IsNullOrEmpty(resource.FilePath))
            {
                if (resource.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    // Google Drive URL — extract file ID for proper embed/preview URLs
                    var driveFileId = _storage.ExtractFileId(resource.FilePath);
                    if (!string.IsNullOrEmpty(driveFileId))
                    {
                        var fmt = resource.FileFormat?.TrimStart('.').Trim().ToUpperInvariant() ?? "";
                        fileUrl = _storage.GetDirectDownloadUrl(driveFileId);
                        ViewBag.DriveFileId = driveFileId;
                        ViewBag.DrivePreviewUrl = _storage.GetPreviewUrl(driveFileId, fmt);
                    }
                    else
                    {
                        fileUrl = resource.FilePath;
                    }
                    canPreview = true;
                }
                else
                {
                    // Local file
                    fileUrl = resource.FilePath.StartsWith("/uploads/") ? resource.FilePath : $"/uploads/{resource.FilePath}";
                    canPreview = true;
                }
            }

            ViewBag.CloudUrl = fileUrl;
            ViewBag.FileUrl = fileUrl;
            ViewBag.CanPreview = canPreview;

            // Pass resource owner's policy flags to the view
            ViewBag.AllowDownloads = resource.AllowDownloads;
            ViewBag.AllowComments = resource.AllowComments;
            ViewBag.AllowRatings = resource.AllowRatings;
            ViewBag.EnableVersionHistory = resource.EnableVersionHistory;

            // Load comments
            if (resource.AllowComments)
            {
                try
                {
                    ViewBag.Comments = await _context.ResourceComments
                        .Where(c => c.ResourceId == resource.ResourceId && c.ParentCommentId == null)
                        .Include(c => c.User)
                        .Include(c => c.Replies).ThenInclude(r => r.User)
                        .OrderByDescending(c => c.DatePosted)
                        .ToListAsync();
                }
                catch (Exception)
                {
                    ViewBag.Comments = new List<ResourceComment>();
                }
            }

            if (resource.EnableVersionHistory)
            {
                var versionList = await _context.ResourceVersions
                    .Where(v => v.ResourceId == resource.ResourceId)
                    .OrderByDescending(v => v.DateUpdated)
                    .ToListAsync();
                if (versionList.Any())
                {
                    ViewBag.ResourceVersions = versionList;
                }
            }

            var relatedResources = new List<Resource>();
            try
            {
                // KNN-powered similar resources
                var similarIds = await _recommendationService.GetSimilarResourcesAsync(id, 6, GetEffectiveSchoolId());
                if (similarIds.Any())
                {
                    relatedResources = await _context.Resources
                        .Include(r => r.User)
                        .Where(r => similarIds.Contains(r.ResourceId) && r.Status == "Published")
                        .ToListAsync();
                    // Preserve KNN ranking order
                    relatedResources = similarIds
                        .Select(sid => relatedResources.FirstOrDefault(r => r.ResourceId == sid))
                        .Where(r => r != null)
                        .Cast<Resource>()
                        .ToList();
                }
            }
            catch { /* fallback below */ }

            // Fallback: same-subject resources if KNN returns nothing
            if (!relatedResources.Any())
            {
                relatedResources = await _context.Resources
                    .Include(r => r.User)
                    .Where(r => r.ResourceId != id && r.Subject == resource.Subject && r.Status == "Published")
                    .OrderByDescending(r => r.ViewCount)
                    .Take(6)
                    .ToListAsync();
            }

            ViewBag.RelatedResources = relatedResources.Select(MapResource).ToList();
            return View();
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> ResourcePreviewApi(int id)
        {
            var resource = await _context.Resources.Include(r => r.User).FirstOrDefaultAsync(r => r.ResourceId == id);
            if (resource == null) return NotFound();

            var currentUser = await GetCurrentUserAsync();

            if (resource.Status == "Pending"
                && resource.PendingReviewPreviewedAt == null
                && (User.IsInRole("SuperAdmin") || User.IsInRole("Manager")))
            {
                resource.PendingReviewPreviewedAt = DateTime.Now;
                await _context.SaveChangesAsync();
            }

            var vm = MapResource(resource);
            if (currentUser != null)
            {
                vm.IsLiked = await _context.Likes.AnyAsync(l => l.UserId == currentUser.Id && l.TargetType == "Resource" && l.TargetId == resource.ResourceId);
                var history = await _context.ReadingHistories.FirstOrDefaultAsync(h => h.UserId == currentUser.Id && h.ResourceId == resource.ResourceId);
                vm.IsSaved = history?.IsBookmarked ?? false;
            }
            vm.LikeCount = await _context.Likes.CountAsync(l => l.TargetType == "Resource" && l.TargetId == resource.ResourceId);
            vm.Tags = await _context.ResourceTags.Where(rt => rt.ResourceId == resource.ResourceId).Include(rt => rt.Tag).Select(rt => rt.Tag!.TagName).ToListAsync();
            vm.Categories = await _context.ResourceCategoryMaps.Where(m => m.ResourceId == resource.ResourceId).Include(m => m.Category).Select(m => m.Category!.CategoryName).ToListAsync();

            int ratingPct = vm.Rating > 0 ? (int)Math.Round((vm.Rating / 5.0) * 100) : 0;
            string formatLabel = (vm.FileFormat?.TrimStart('.').ToUpperInvariant() ?? "") switch
            {
                "PDF" => "PDF Document",
                "DOCX" or "DOC" => "Word Document",
                "PPTX" or "PPT" => "PowerPoint Presentation",
                "XLSX" or "XLS" => "Excel Spreadsheet",
                "LINK" => "External Link",
                _ => "Document"
            };

            var reviewDueAt = GetPendingReviewDeadline(resource);
            var reviewDaysRemaining = resource.Status == "Pending"
                ? Math.Max(0, (int)Math.Ceiling((reviewDueAt - DateTime.Now).TotalDays))
                : 0;

            // Compute preview URL for embedded viewer
            string? previewUrl = null;
            if (IsLinkFileFormat(resource.FileFormat))
            {
                var externalUrl = NormalizeExternalResourceUrl(resource.FilePath);
                previewUrl = BuildExternalPreviewUrl(externalUrl);
            }
            else if (!string.IsNullOrEmpty(resource.FilePath))
            {
                if (resource.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    var driveFileId = _storage.ExtractFileId(resource.FilePath);
                    if (!string.IsNullOrEmpty(driveFileId))
                    {
                        var fmt = resource.FileFormat?.TrimStart('.').Trim().ToUpperInvariant() ?? "";
                        var isImage = fmt is "JPG" or "JPEG" or "PNG" or "GIF" or "WEBP" or "BMP" or "SVG";
                        if (isImage)
                            previewUrl = $"https://drive.google.com/uc?export=view&id={driveFileId}";
                        else
                            previewUrl = _storage.GetPreviewUrl(driveFileId, fmt);
                    }
                }
                else
                {
                    // Local file
                    var fmt = resource.FileFormat?.TrimStart('.').Trim().ToUpperInvariant() ?? "";
                    var isImage = fmt is "JPG" or "JPEG" or "PNG" or "GIF" or "WEBP" or "BMP" or "SVG";
                    var isOffice = fmt is "DOCX" or "DOC" or "PPTX" or "PPT" or "XLSX" or "XLS";

                    if (isImage || fmt == "PDF")
                    {
                        previewUrl = Url.Action("DownloadResource", "Home", new { id = resource.ResourceId, inline = true });
                        if (fmt == "PDF") previewUrl += "#view=FitH";
                    }
                    else if (isOffice)
                    {
                        var req = Request;
                        var isLocalhost = req.Host.Host == "localhost" || req.Host.Host == "127.0.0.1";
                        if (!isLocalhost)
                        {
                            var fileUrl = _storage.GetDirectDownloadUrl(resource.FilePath);
                            var fullUrl = $"{req.Scheme}://{req.Host}{fileUrl}";
                            previewUrl = "https://view.officeapps.live.com/op/embed.aspx?src=" + Uri.EscapeDataString(fullUrl);
                        }
                    }
                }
            }

            return Json(new
            {
                id = vm.Id,
                title = vm.Title,
                description = vm.Description,
                subject = vm.Subject,
                gradeLevel = vm.GradeLevel,
                quarter = vm.Quarter,
                resourceType = vm.ResourceType,
                fileFormat = vm.FileFormat,
                fileSize = vm.FileSize,
                viewCount = vm.ViewCount,
                downloadCount = vm.DownloadCount,
                rating = vm.Rating,
                ratingCount = vm.RatingCount,
                ratingPct = ratingPct,
                formatLabel = formatLabel,
                uploader = vm.Uploader,
                uploaderInitials = vm.UploaderInitials,
                uploaderColor = vm.UploaderColor,
                isLiked = vm.IsLiked,
                isSaved = vm.IsSaved,
                likeCount = vm.LikeCount,
                tags = vm.Tags,
                categories = vm.Categories,
                createdAt = vm.CreatedAt.ToString("MMM dd, yyyy"),
                reviewDueAt = resource.Status == "Pending" ? reviewDueAt.ToString("MMM dd, yyyy h:mm tt") : null,
                reviewPreviewedAt = resource.PendingReviewPreviewedAt?.ToString("MMM dd, yyyy h:mm tt"),
                reviewDaysRemaining = reviewDaysRemaining,
                detailUrl = Url.Action("ResourceDetail", "Home", new { id = vm.Id }),
                previewUrl = previewUrl,
                sourceUrl = IsLinkFileFormat(resource.FileFormat) ? NormalizeExternalResourceUrl(resource.FilePath) : null,
                sourceLabel = IsLinkFileFormat(resource.FileFormat) ? GetExternalSourceLabel(resource.FilePath) : null
            });
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> LikeResource([FromForm] int id)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Unauthorized();

            var resource = await _context.Resources.FindAsync(id);
            if (resource == null) return NotFound();

            var existingLike = await _context.Likes.FirstOrDefaultAsync(l => l.UserId == currentUser.Id && l.TargetType == "Resource" && l.TargetId == id);
            
            if (existingLike != null)
            {
                // Unlike — remove the like
                _context.Likes.Remove(existingLike);
                await _context.SaveChangesAsync();
                var currentCount = await _context.Likes.CountAsync(l => l.TargetType == "Resource" && l.TargetId == id);
                return Json(new { success = true, isLiked = false, count = currentCount });
            }

            _context.Likes.Add(new Like { UserId = currentUser.Id, TargetType = "Resource", TargetId = id, CreatedAt = DateTime.Now });
            await LogActivity(currentUser.Id, "Like", resource.Title, resource.ResourceId);

            await _context.SaveChangesAsync();
            var count = await _context.Likes.CountAsync(l => l.TargetType == "Resource" && l.TargetId == id);
            return Json(new { success = true, isLiked = true, count = count });
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> RateResource([FromForm] int id, [FromForm] int rating)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Unauthorized();

            var resource = await _context.Resources.FindAsync(id);
            if (resource == null) return NotFound();

            if (!resource.AllowRatings)
                return Json(new { success = false, message = "Ratings are disabled for this resource." });

            if (rating < 1 || rating > 5) return BadRequest();

            var existingRating = await _context.Likes.FirstOrDefaultAsync(l => l.UserId == currentUser.Id && l.TargetType == "ResourceRating" && l.TargetId == id);
            if (existingRating != null)
            {
                return Json(new { success = false, message = "You have already rated this resource." });
            }

            _context.Likes.Add(new Like { UserId = currentUser.Id, TargetType = "ResourceRating", TargetId = id, CreatedAt = DateTime.Now });
            
            double totalPoints = (resource.Rating * resource.RatingCount) + rating;
            resource.RatingCount++;
            resource.Rating = totalPoints / resource.RatingCount;
            
            await _context.SaveChangesAsync();
            return Json(new { success = true, rating = resource.Rating.ToString("0.0"), count = resource.RatingCount });
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> SaveResource([FromForm] int id)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Unauthorized();

            var resource = await _context.Resources.FindAsync(id);
            if (resource == null) return NotFound();

            var history = await _context.ReadingHistories.FirstOrDefaultAsync(h => h.UserId == currentUser.Id && h.ResourceId == id);
            bool isSaved;
            if (history != null) {
                history.IsBookmarked = !history.IsBookmarked;
                isSaved = history.IsBookmarked;
            } else {
                history = new ReadingHistory {
                    UserId = currentUser.Id,
                    ResourceId = id,
                    IsBookmarked = true,
                    LastAccessed = DateTime.Now,
                    ProgressStatus = "Not Started"
                };
                _context.ReadingHistories.Add(history);
                isSaved = true;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, isSaved });
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> ViewResource(int id)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            var resource = await _context.Resources.FirstOrDefaultAsync(r => r.ResourceId == id);
            if (resource == null)
            {
                return NotFound();
            }

            if (string.IsNullOrEmpty(resource.FilePath))
            {
                return NotFound();
            }

            var localUrl = resource.FilePath.StartsWith("/uploads/") ? resource.FilePath : $"/uploads/{resource.FilePath}";
            if (string.IsNullOrEmpty(localUrl))
            {
                return NotFound();
            }

            return Redirect(localUrl);
        }

        private string GetContentType(string fileFormat)
        {
            if (string.IsNullOrWhiteSpace(fileFormat))
                return "application/octet-stream";
                
            var fmt = fileFormat.TrimStart('.').Trim().ToUpperInvariant();
            return fmt switch
            {
                "PDF" => "application/pdf",
                "DOCX" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "DOC" => "application/msword",
                "PPTX" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                "PPT" => "application/vnd.ms-powerpoint",
                "XLSX" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "XLS" => "application/vnd.ms-excel",
                _ => "application/octet-stream"
            };
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> DownloadResource(int id, bool inline = false)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            var resource = await _context.Resources.FirstOrDefaultAsync(r => r.ResourceId == id);
            if (resource == null)
            {
                TempData["ErrorMessage"] = "Resource not found.";
                return RedirectToAction("Repository");
            }

            bool isOwner = resource.UserId == currentUser.Id;
            bool isAdmin = User.IsInRole("SuperAdmin") || User.IsInRole("Manager");

            // Check access duration
            if (!isOwner && !isAdmin && resource.AccessDuration == "Custom" && resource.AccessExpiresAt.HasValue && resource.AccessExpiresAt.Value < DateTime.Now)
            {
                if (inline) return NotFound();
                TempData["ErrorMessage"] = "This resource is no longer accessible. The access period has expired.";
                return RedirectToAction("Repository");
            }

            // Check restricted access
            if (!isOwner && !isAdmin && resource.AccessLevel == "Restricted")
            {
                var hasGrant = await _context.ResourceAccessGrants.AnyAsync(g => g.ResourceId == id && g.UserId == currentUser.Id);
                if (!hasGrant)
                {
                    if (inline) return NotFound();
                    TempData["ErrorMessage"] = "You do not have permission to access this resource.";
                    return RedirectToAction("Repository");
                }
            }

            // Policy enforcement: block downloads if the owner disabled them (except for the owner itself)
            if (!resource.AllowDownloads && !isOwner && !isAdmin)
            {
                if (inline) return NotFound();
                TempData["ErrorMessage"] = "Downloads are disabled for this resource by the author.";
                return RedirectToAction("ResourceDetail", new { id });
            }

            if (string.IsNullOrEmpty(resource.FilePath))
            {
                if (inline) return NotFound();
                TempData["ErrorMessage"] = $"No file is attached to \"{resource.Title}\". The contributor may not have uploaded a file yet.";
                return RedirectToAction("ResourceDetail", new { id });
            }

            if (!inline)
            {
                resource.DownloadCount++;
                var log = new UserActivityLog
                {
                    UserId = currentUser.Id,
                    ResourceId = id,
                    ActivityType = "Download",
                    TargetTitle = resource.Title,
                    ActivityDate = DateTime.Now
                };
                _context.UserActivityLogs.Add(log);

                // Bump reading progress to 50% on download
                var dlHistory = await _context.ReadingHistories.FirstOrDefaultAsync(h => h.UserId == currentUser.Id && h.ResourceId == id);
                if (dlHistory != null && dlHistory.ProgressStatus != "Completed")
                {
                    if (dlHistory.ProgressPercent < 50) dlHistory.ProgressPercent = 50;
                    dlHistory.LastAccessed = DateTime.Now;
                }

                await _context.SaveChangesAsync();
            }

            string contentType = GetContentType(resource.FileFormat);
            string downloadName = $"{resource.Title}.{resource.FileFormat?.ToLower() ?? "bin"}";

            // Check if file is stored on Google Drive (URL starts with http)
            if (resource.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var driveFileId = _storage.ExtractFileId(resource.FilePath);
                if (!string.IsNullOrEmpty(driveFileId))
                {
                    var stream = await _storage.DownloadAsync(driveFileId);
                    if (stream == null)
                    {
                        if (inline) return NotFound();
                        TempData["ErrorMessage"] = "Error downloading file from cloud storage.";
                        return RedirectToAction("ResourceDetail", new { id });
                    }

                    if (inline)
                    {
                        Response.Headers.Append("Content-Disposition", new System.Net.Mime.ContentDisposition
                        {
                            FileName = downloadName,
                            Inline = true
                        }.ToString());
                        return File(stream, contentType);
                    }
                    return File(stream, "application/octet-stream", downloadName);
                }
            }

            // Local file handling
            string relativePath = resource.FilePath.StartsWith("/uploads/") ? resource.FilePath.Substring(9) : resource.FilePath;
            var filepath = Path.Combine(_environment.WebRootPath, "uploads", relativePath);
            if (!System.IO.File.Exists(filepath))
            {
                if (inline) return NotFound();
                TempData["ErrorMessage"] = "Error downloading file: unable to access the file from local storage.";
                return RedirectToAction("ResourceDetail", new { id });
            }
            
            var content = await System.IO.File.ReadAllBytesAsync(filepath);

            if (inline) 
            {
                Response.Headers.Append("Content-Disposition", new System.Net.Mime.ContentDisposition {
                    FileName = downloadName,
                    Inline = true
                }.ToString());
                return File(content, contentType);
            }

            return File(content, "application/octet-stream", downloadName);
        }

        // ==================== Public Download (for anonymous access to Public resources) ====================

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> PublicDownload(int id, bool inline = false)
        {
            var resource = await _context.Resources.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.ResourceId == id);
            if (resource == null || (resource.Status != "Published" && resource.Status != "Pending") || resource.AccessLevel != "Public")
                return NotFound();

            if (!resource.AllowDownloads)
            {
                if (inline) return NotFound();
                TempData["ErrorMessage"] = "Downloads are disabled for this resource.";
                return RedirectToAction("Index");
            }

            // Check duration
            if (resource.AccessDuration == "Custom" && resource.AccessExpiresAt.HasValue && resource.AccessExpiresAt.Value < DateTime.Now)
                return NotFound();

            if (string.IsNullOrEmpty(resource.FilePath))
                return NotFound();

            resource.DownloadCount++;
            await _context.SaveChangesAsync();

            string contentType = GetContentType(resource.FileFormat);
            string downloadName = $"{resource.Title}.{resource.FileFormat?.ToLower() ?? "bin"}";

            // Google Drive file
            if (resource.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var driveFileId = _storage.ExtractFileId(resource.FilePath);
                if (!string.IsNullOrEmpty(driveFileId))
                {
                    var stream = await _storage.DownloadAsync(driveFileId);
                    if (stream == null) return NotFound();

                    if (inline)
                    {
                        Response.Headers.Append("Content-Disposition", new System.Net.Mime.ContentDisposition
                        {
                            FileName = downloadName,
                            Inline = true
                        }.ToString());
                        return File(stream, contentType);
                    }
                    return File(stream, "application/octet-stream", downloadName);
                }
            }

            // Local file handling
            string relPath = resource.FilePath.StartsWith("/uploads/") ? resource.FilePath.Substring(9) : resource.FilePath;
            var filepath = Path.Combine(_environment.WebRootPath, "uploads", relPath);
            if (!System.IO.File.Exists(filepath))
                return NotFound();

            var content = await System.IO.File.ReadAllBytesAsync(filepath);

            if (inline)
            {
                Response.Headers.Append("Content-Disposition", new System.Net.Mime.ContentDisposition
                {
                    FileName = downloadName,
                    Inline = true
                }.ToString());
                return File(content, contentType);
            }

            return File(content, "application/octet-stream", downloadName);
        }

        /// <summary>
        /// Updates reading progress based on time spent reading.
        /// </summary>
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> UpdateReadingProgress(int id, int progress)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Json(new { success = false });

            progress = Math.Clamp(progress, 0, 99);

            var history = await _context.ReadingHistories
                .FirstOrDefaultAsync(h => h.UserId == currentUser.Id && h.ResourceId == id);

            if (history != null && history.ProgressStatus != "Completed")
            {
                if (progress > history.ProgressPercent)
                    history.ProgressPercent = progress;
                history.LastAccessed = DateTime.Now;
            }
            else if (history == null)
            {
                history = new ReadingHistory
                {
                    UserId = currentUser.Id,
                    ResourceId = id,
                    LastAccessed = DateTime.Now,
                    ProgressStatus = "In Progress",
                    ProgressPercent = progress
                };
                _context.ReadingHistories.Add(history);
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, progress = history!.ProgressPercent });
        }

        /// <summary>
        /// Marks a resource as 100% completed in reading history.
        /// </summary>
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> MarkComplete(int id)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Json(new { success = false });

            var history = await _context.ReadingHistories
                .FirstOrDefaultAsync(h => h.UserId == currentUser.Id && h.ResourceId == id);

            if (history == null)
            {
                history = new ReadingHistory
                {
                    UserId = currentUser.Id,
                    ResourceId = id,
                    LastAccessed = DateTime.Now,
                    ProgressStatus = "Completed",
                    ProgressPercent = 100,
                    CompletedDate = DateTime.Now
                };
                _context.ReadingHistories.Add(history);
            }
            else
            {
                history.ProgressStatus = "Completed";
                history.ProgressPercent = 100;
                history.CompletedDate = DateTime.Now;
                history.LastAccessed = DateTime.Now;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        // ==================== Resource Comments ====================

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> PostComment(int resourceId, string content, int? parentCommentId = null)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Json(new { success = false, message = "Not authenticated" });

            if (string.IsNullOrWhiteSpace(content))
                return Json(new { success = false, message = "Comment cannot be empty" });

            // Check if comments are allowed
            var resource = await _context.Resources.FindAsync(resourceId);
            if (resource == null) return Json(new { success = false, message = "Resource not found" });
            if (!resource.AllowComments) return Json(new { success = false, message = "Comments are disabled for this resource" });

            var comment = new ResourceComment
            {
                ResourceId = resourceId,
                UserId = currentUser.Id,
                Content = content.Trim(),
                DatePosted = DateTime.Now,
                ParentCommentId = parentCommentId
            };
            _context.ResourceComments.Add(comment);
            await _context.SaveChangesAsync();

            await LogActivity(currentUser.Id, "Comment", resource.Title, resource.ResourceId);

            return Json(new
            {
                success = true,
                comment = new
                {
                    commentId = comment.CommentId,
                    content = comment.Content,
                    datePosted = comment.DatePosted.ToString("MMM dd, yyyy h:mm tt"),
                    authorName = currentUser.FullName,
                    authorInitials = currentUser.Initials,
                    authorColor = currentUser.AvatarColor,
                    parentCommentId = comment.ParentCommentId
                }
            });
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> DeleteComment(int commentId)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Json(new { success = false });

            var comment = await _context.ResourceComments
                .Include(c => c.Replies)
                .FirstOrDefaultAsync(c => c.CommentId == commentId);

            if (comment == null) return Json(new { success = false, message = "Comment not found" });

            // Only author, Manager, or SuperAdmin can delete
            if (comment.UserId != currentUser.Id && !User.IsInRole("SuperAdmin") && !User.IsInRole("Manager"))
                return Json(new { success = false, message = "Not authorized" });

            // Remove replies first, then the comment
            _context.ResourceComments.RemoveRange(comment.Replies);
            _context.ResourceComments.Remove(comment);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> UpdateComment(int resourceId, int commentId, string content)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Json(new { success = false });

            var comment = await _context.ResourceComments
                .FirstOrDefaultAsync(c => c.CommentId == commentId && c.ResourceId == resourceId);

            if (comment == null) return Json(new { success = false });

            // Only author, Manager, or SuperAdmin can edit
            if (comment.UserId != currentUser.Id && !User.IsInRole("SuperAdmin") && !User.IsInRole("Manager"))
                return Json(new { success = false });

            content = content?.Trim() ?? "";
            if (string.IsNullOrEmpty(content) || content.Length > 2000)
                return Json(new { success = false });

            comment.Content = content;
            comment.DateUpdated = DateTime.UtcNow;
            _context.ResourceComments.Update(comment);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> LikeComment(int commentId)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Json(new { success = false });

            var comment = await _context.ResourceComments
                .FirstOrDefaultAsync(c => c.CommentId == commentId);

            if (comment == null) return Json(new { success = false });

            // Toggle like - simplified implementation (increments count)
            // In production, you'd need a CommentLike entity to track which users liked which comments
            comment.LikeCount = Math.Max(0, comment.LikeCount + 1);
            _context.ResourceComments.Update(comment);
            await _context.SaveChangesAsync();

            return Json(new { success = true, count = comment.LikeCount, isLiked = true });
        }

        [AllowAnonymous]
        public async Task<IActionResult> Search(string? q = null)
        {
            var query = (q ?? string.Empty).Trim();

            if (User.Identity?.IsAuthenticated ?? false)
            {
                var currentUser = await GetCurrentUserAsync();
                var canSearchUsers = User.IsInRole("SuperAdmin") || User.IsInRole("Manager");
                var model = new GlobalSearchViewModel
                {
                    Query = query,
                    CanSearchUsers = canSearchUsers
                };

                if (!string.IsNullOrWhiteSpace(query))
                {
                    var pattern = $"%{query}%";

                    var matchedResources = await BuildAccessiblePublishedResourceQuery(currentUser)
                        .Where(r =>
                            EF.Functions.Like(r.Title, pattern) ||
                            EF.Functions.Like(r.Description, pattern) ||
                            EF.Functions.Like(r.Subject, pattern) ||
                            EF.Functions.Like(r.ResourceType, pattern) ||
                            EF.Functions.Like(r.GradeLevel, pattern) ||
                            EF.Functions.Like(r.FileFormat, pattern))
                        .OrderByDescending(r => r.ViewCount + r.DownloadCount)
                        .ThenByDescending(r => r.DateUploaded)
                        .Take(8)
                        .ToListAsync();

                    var lessons = await _context.LessonsLearned
                        .Include(l => l.User)
                        .Include(l => l.Resource)
                        .Where(l =>
                            EF.Functions.Like(l.Title, pattern) ||
                            EF.Functions.Like(l.Content, pattern) ||
                            EF.Functions.Like(l.Category, pattern) ||
                            EF.Functions.Like(l.Tags, pattern) ||
                            (l.Resource != null && EF.Functions.Like(l.Resource.Title, pattern)))
                        .OrderByDescending(l => l.DateSubmitted)
                        .Take(8)
                        .ToListAsync();

                    var discussions = await _context.Discussions
                        .Include(d => d.User)
                        .Include(d => d.Posts)
                        .Where(d =>
                            EF.Functions.Like(d.Title, pattern) ||
                            EF.Functions.Like(d.Content, pattern) ||
                            EF.Functions.Like(d.Category, pattern) ||
                            EF.Functions.Like(d.Tags, pattern) ||
                            EF.Functions.Like(d.Type, pattern))
                        .OrderByDescending(d => d.DateCreated)
                        .Take(8)
                        .ToListAsync();

                    model.Resources = matchedResources.Select(MapResource).ToList();
                    model.Lessons = lessons.Select(l => MapLesson(l)).ToList();
                    model.Discussions = discussions.Select(d => MapDiscussion(d)).ToList();

                    if (canSearchUsers)
                    {
                        var schoolId = GetEffectiveSchoolId();
                        var matchedUsers = await _userManager.Users
                            .Where(u =>
                                (!schoolId.HasValue || u.SchoolId == schoolId.Value) &&
                                (EF.Functions.Like(u.FirstName, pattern) ||
                                 EF.Functions.Like(u.LastName, pattern) ||
                                 EF.Functions.Like(u.Email ?? string.Empty, pattern) ||
                                 EF.Functions.Like(u.GradeOrPosition ?? string.Empty, pattern)))
                            .OrderBy(u => u.FirstName)
                            .ThenBy(u => u.LastName)
                            .Take(8)
                            .ToListAsync();

                        foreach (var user in matchedUsers)
                        {
                            var roles = await _userManager.GetRolesAsync(user);
                            var role = roles.FirstOrDefault() ?? "Student";

                            model.Users.Add(new UserViewModel
                            {
                                Name = user.FullName,
                                Email = user.Email ?? string.Empty,
                                Initials = user.Initials,
                                AvatarColor = user.AvatarColor,
                                Role = role,
                                RoleBadgeClass = GetRoleBadge(role),
                                GradeOrPosition = user.GradeOrPosition,
                                Status = user.Status,
                                StatusBadgeClass = GetStatusBadge(user.Status),
                                JoinedAt = user.DateCreated
                            });
                        }
                    }
                }

                return View(model);
            }

            // Only show PUBLIC resources (both Published and Pending) to anonymous users
            var resources = await _context.Resources
                .IgnoreQueryFilters()
                .Include(r => r.User)
                .Where(r => (r.Status == "Published" || r.Status == "Pending") && r.AccessLevel == "Public")
                .Where(r => r.AccessDuration != "Custom" || r.AccessExpiresAt == null || r.AccessExpiresAt > DateTime.Now)
                .OrderByDescending(r => r.DateUploaded)
                .ToListAsync();

            var mapped = resources.Select(MapResource).ToList();
            ViewBag.Resources = mapped;
            ViewBag.AllResources = mapped;
            ViewBag.SearchQuery = query;
            return View("PublicSearch");
        }

        // ==================== Public Resource Detail (for anonymous access to Public resources) ====================

        [AllowAnonymous]
        public async Task<IActionResult> PublicResourceDetail(int id)
        {
            var resource = await _context.Resources
                .IgnoreQueryFilters()
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.ResourceId == id);

            if (resource == null || (resource.Status != "Published" && resource.Status != "Pending") || resource.AccessLevel != "Public")
            {
                TempData["ErrorMessage"] = "This resource requires sign-in to access.";
                return RedirectToAction("Login");
            }

            // Check duration expiry
            if (resource.AccessDuration == "Custom" && resource.AccessExpiresAt.HasValue && resource.AccessExpiresAt.Value < DateTime.Now)
            {
                TempData["ErrorMessage"] = "This resource is no longer accessible. The access period has expired.";
                return RedirectToAction("Index");
            }

            // Increment view count
            resource.ViewCount++;
            await _context.SaveChangesAsync();

            var vm = MapResource(resource);
            vm.LikeCount = await _context.Likes.CountAsync(l => l.TargetType == "Resource" && l.TargetId == resource.ResourceId);

            // Load tags and categories
            vm.Tags = await _context.ResourceTags
                .Where(rt => rt.ResourceId == resource.ResourceId)
                .Include(rt => rt.Tag)
                .Select(rt => rt.Tag!.TagName)
                .ToListAsync();

            vm.Categories = await _context.ResourceCategoryMaps
                .Where(m => m.ResourceId == resource.ResourceId)
                .Include(m => m.Category)
                .Select(m => m.Category!.CategoryName)
                .ToListAsync();

            ViewBag.Resource = vm;
            ViewBag.CurrentUserId = null;

            // Local file preview
            string localUrl = string.Empty;
            bool canPreview = false;
            ViewBag.ExternalSourceUrl = string.Empty;
            ViewBag.ExternalPreviewUrl = string.Empty;
            ViewBag.ExternalSourceLabel = string.Empty;

            if (IsLinkFileFormat(resource.FileFormat))
            {
                localUrl = NormalizeExternalResourceUrl(resource.FilePath);
                if (!string.IsNullOrEmpty(localUrl))
                {
                    ViewBag.ExternalSourceUrl = localUrl;
                    ViewBag.ExternalPreviewUrl = BuildExternalPreviewUrl(localUrl) ?? string.Empty;
                    ViewBag.ExternalSourceLabel = GetExternalSourceLabel(localUrl);
                    canPreview = true;
                }
            }
            else if (!string.IsNullOrEmpty(resource.FilePath))
            {
                if (resource.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    localUrl = resource.FilePath;
                }
                else
                {
                    localUrl = resource.FilePath.StartsWith("/uploads/") ? resource.FilePath : $"/uploads/{resource.FilePath}";
                }
                canPreview = true;
            }

            ViewBag.CloudUrl = localUrl;
            ViewBag.FileUrl = localUrl;
            ViewBag.CanPreview = canPreview;
            ViewBag.AllowDownloads = resource.AllowDownloads;
            ViewBag.AllowComments = false; // Anonymous users can't comment
            ViewBag.AllowRatings = false;
            ViewBag.EnableVersionHistory = resource.EnableVersionHistory;
            ViewBag.IsPublicView = true;

            // Load comments (read-only for public)
            if (resource.AllowComments)
            {
                try
                {
                    ViewBag.Comments = await _context.ResourceComments
                        .Where(c => c.ResourceId == resource.ResourceId && c.ParentCommentId == null)
                        .Include(c => c.User)
                        .Include(c => c.Replies).ThenInclude(r => r.User)
                        .OrderByDescending(c => c.DatePosted)
                        .ToListAsync();
                }
                catch (Exception)
                {
                    ViewBag.Comments = new List<ResourceComment>();
                }
            }

            if (resource.EnableVersionHistory)
            {
                var versionList = await _context.ResourceVersions
                    .Where(v => v.ResourceId == resource.ResourceId)
                    .OrderByDescending(v => v.DateUpdated)
                    .ToListAsync();
                if (versionList.Any())
                {
                    ViewBag.ResourceVersions = versionList;
                }
            }

            var relatedResources = new List<Resource>();
            try
            {
                var similarIds = await _recommendationService.GetSimilarResourcesAsync(id, 6);
                if (similarIds.Any())
                {
                    relatedResources = await _context.Resources
                        .IgnoreQueryFilters()
                        .Include(r => r.User)
                        .Where(r => similarIds.Contains(r.ResourceId) && r.Status == "Published" && r.AccessLevel == "Public")
                        .ToListAsync();
                    relatedResources = similarIds
                        .Select(sid => relatedResources.FirstOrDefault(r => r.ResourceId == sid))
                        .Where(r => r != null)
                        .Cast<Resource>()
                        .ToList();
                }
            }
            catch { /* fallback below */ }

            if (!relatedResources.Any())
            {
                relatedResources = await _context.Resources
                    .IgnoreQueryFilters()
                    .Include(r => r.User)
                    .Where(r => r.ResourceId != id && r.Subject == resource.Subject && r.Status == "Published" && r.AccessLevel == "Public")
                    .Take(6)
                    .ToListAsync();
            }
            ViewBag.RelatedResources = relatedResources.Select(MapResource).ToList();

            return View("ResourceDetail");
        }

        // ==================== Lessons Learned ====================

        [Authorize]
        public async Task<IActionResult> LessonsLearned()
        {
            var currentUser = await GetCurrentUserAsync();
            ViewBag.CurrentUserId = currentUser?.Id;
            await TryEnsureLessonCommentsSchemaAsync();

            List<LessonLearned> lessons;
            try
            {
                lessons = await _context.LessonsLearned
                    .Include(l => l.User)
                    .Include(l => l.Resource).ThenInclude(r => r!.User)
                    .Include(l => l.Comments).ThenInclude(c => c.User)
                    .OrderByDescending(l => l.DateSubmitted)
                    .ToListAsync();
            }
            catch (SqlException ex) when (IsMissingLessonCommentsTable(ex))
            {
                lessons = await _context.LessonsLearned
                    .Include(l => l.User)
                    .Include(l => l.Resource).ThenInclude(r => r!.User)
                    .OrderByDescending(l => l.DateSubmitted)
                    .ToListAsync();

                foreach (var lesson in lessons)
                {
                    lesson.Comments = new List<LessonComment>();
                }

                TempData["ErrorMessage"] = "Lesson replies are temporarily unavailable until the database update completes.";
            }

            var likedIds = new HashSet<int>();
            if (currentUser != null)
            {
                likedIds = (await _context.Likes
                    .Where(l => l.UserId == currentUser.Id && l.TargetType == "Lesson")
                    .Select(l => l.TargetId)
                    .ToListAsync()).ToHashSet();
            }

            var mapped = lessons.Select(l => MapLesson(l, likedIds.Contains(l.LessonId))).ToList();
            ViewBag.Lessons = mapped;
            ViewBag.Categories = mapped.Select(l => l.Category).Distinct().ToList();
            ViewBag.TotalLessons = mapped.Count;
            ViewBag.TotalLikes = mapped.Sum(l => l.LikeCount);
            ViewBag.TotalComments = mapped.Sum(l => l.CommentCount);
            ViewBag.Contributors = mapped.Select(l => l.Author).Distinct().Count();

            // Provide a list of published resources for the dropdown
            ViewBag.AvailableResources = await _context.Resources
                .Where(r => r.Status == "Published")
                .OrderByDescending(r => r.DateUploaded)
                .Select(r => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem { Value = r.ResourceId.ToString(), Text = r.Title })
                .ToListAsync();

            return View();
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> SubmitLesson(LessonViewModel model)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            var resource = await _context.Resources.Include(r => r.User).FirstOrDefaultAsync(r => r.ResourceId == model.ResourceId);
            if (resource == null)
            {
                TempData["ErrorMessage"] = "Please select a valid resource.";
                return RedirectToAction("LessonsLearned");
            }

            if (!resource.AllowComments)
            {
                TempData["ErrorMessage"] = "Lesson learned submissions are disabled for this resource.";
                return RedirectToAction("ResourceDetail", new { id = model.ResourceId });
            }

            var lesson = new LessonLearned
            {
                Title = model.Title ?? "",
                Content = model.Content ?? "",
                Category = model.Category ?? "",
                Tags = model.Tags != null ? string.Join(", ", model.Tags) : "",
                UserId = currentUser.Id,
                SchoolId = currentUser.SchoolId,
                ResourceId = resource.ResourceId,
                Rating = 0,
                Comment = "",
                DateSubmitted = DateTime.Now
            };

            _context.LessonsLearned.Add(lesson);
            await _context.SaveChangesAsync();
            await LogActivity(currentUser.Id, "Upload", lesson.Title);

            // Create notification for the resource uploader
            if (resource.UserId != currentUser.Id)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = resource.UserId,
                    Title = "New Lesson Learned",
                    Message = $"{currentUser.FullName} shared a lesson learned about your resource \"{resource.Title}\".",
                    Type = "System",
                    Icon = "bi-lightbulb-fill",
                    IconBg = "#fef3c7",
                    ResourceId = resource.ResourceId,
                    Link = $"/Home/LessonsLearned"
                });
                await _context.SaveChangesAsync();
            }

            TempData["SuccessMessage"] = "Your lesson has been submitted successfully!";
            TempData["SuccessTitle"] = "Lesson Submitted";
            return RedirectToAction("LessonsLearned");
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> EditLesson(int id, string title, string content, string category, string tags, int resourceId)
        {
            var lesson = await _context.LessonsLearned.FindAsync(id);
            if (lesson != null)
            {
                lesson.Title = title;
                lesson.Content = content;
                lesson.Category = category;
                lesson.ResourceId = resourceId;
                if (!string.IsNullOrEmpty(tags))
                    lesson.Tags = tags;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Lesson updated successfully!";
            }
            return RedirectToAction("LessonsLearned");
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> DeleteLesson(int id)
        {
            var lesson = await _context.LessonsLearned.FindAsync(id);
            if (lesson != null)
            {
                _context.LessonsLearned.Remove(lesson);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Lesson deleted successfully.";
            }
            return RedirectToAction("LessonsLearned");
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> LikeLesson(int id)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Json(new { success = false });

            var existing = await _context.Likes
                .FirstOrDefaultAsync(l => l.UserId == currentUser.Id && l.TargetType == "Lesson" && l.TargetId == id);

            var lesson = await _context.LessonsLearned.FindAsync(id);
            if (lesson == null) return Json(new { success = false });

            if (existing != null)
            {
                // Unlike — remove the like and decrement
                _context.Likes.Remove(existing);
                lesson.LikeCount = Math.Max(0, lesson.LikeCount - 1);
                await _context.SaveChangesAsync();
                return Json(new { success = true, likes = lesson.LikeCount, isLiked = false });
            }

            _context.Likes.Add(new Like { UserId = currentUser.Id, TargetType = "Lesson", TargetId = id, CreatedAt = DateTime.Now });
            lesson.LikeCount++;

            await _context.SaveChangesAsync();
            return Json(new { success = true, likes = lesson.LikeCount, isLiked = true });
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> ReplyToLesson(int lessonId, string content)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");
            await TryEnsureLessonCommentsSchemaAsync();

            if (string.IsNullOrWhiteSpace(content))
            {
                TempData["ErrorMessage"] = "Reply content cannot be empty.";
                return RedirectToAction("LessonsLearned");
            }

            var lesson = await _context.LessonsLearned
                .Include(l => l.Resource)
                .FirstOrDefaultAsync(l => l.LessonId == lessonId);
            if (lesson == null) return RedirectToAction("LessonsLearned");

            // Only the resource uploader, managers, or admins can reply
            var isUploader = lesson.Resource?.UserId == currentUser.Id;
            var isPrivileged = User.IsInRole("Manager") || User.IsInRole("SuperAdmin");
            if (!isUploader && !isPrivileged)
            {
                TempData["ErrorMessage"] = "Only the resource uploader or administrators can reply.";
                return RedirectToAction("LessonsLearned");
            }

            try
            {
                var comment = new LessonComment
                {
                    LessonId = lessonId,
                    UserId = currentUser.Id,
                    Content = content,
                    DatePosted = DateTime.Now
                };
                _context.LessonComments.Add(comment);
                lesson.CommentCount++;
                await _context.SaveChangesAsync();
            }
            catch (SqlException ex) when (IsMissingLessonCommentsTable(ex))
            {
                TempData["ErrorMessage"] = "Lesson replies are temporarily unavailable until the database update completes.";
                return RedirectToAction("LessonsLearned");
            }

            // Notify the student who submitted the lesson
            if (lesson.UserId != currentUser.Id)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = lesson.UserId,
                    Title = "Lesson Reply",
                    Message = $"{currentUser.FullName} responded to your lesson \"{lesson.Title}\".",
                    Type = "System",
                    Icon = "bi-reply-fill",
                    IconBg = "#dbeafe",
                    ResourceId = lesson.ResourceId,
                    Link = "/Home/LessonsLearned"
                });
                await _context.SaveChangesAsync();
            }

            TempData["SuccessMessage"] = "Reply posted successfully!";
            return RedirectToAction("LessonsLearned");
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> DeleteLessonComment(int id)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");
            await TryEnsureLessonCommentsSchemaAsync();

            LessonComment? comment;
            try
            {
                comment = await _context.LessonComments.Include(c => c.Lesson).FirstOrDefaultAsync(c => c.LessonCommentId == id);
            }
            catch (SqlException ex) when (IsMissingLessonCommentsTable(ex))
            {
                TempData["ErrorMessage"] = "Lesson replies are temporarily unavailable until the database update completes.";
                return RedirectToAction("LessonsLearned");
            }

            if (comment == null) return RedirectToAction("LessonsLearned");

            var isOwner = comment.UserId == currentUser.Id;
            var isPrivileged = User.IsInRole("Manager") || User.IsInRole("SuperAdmin");
            if (!isOwner && !isPrivileged) return RedirectToAction("LessonsLearned");

            if (comment.Lesson != null)
                comment.Lesson.CommentCount = Math.Max(0, comment.Lesson.CommentCount - 1);

            _context.LessonComments.Remove(comment);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Reply deleted.";
            return RedirectToAction("LessonsLearned");
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> DeleteAccount(string reason, string? feedback)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            if (string.IsNullOrWhiteSpace(reason))
            {
                TempData["ErrorMessage"] = "Please provide a reason for account deletion.";
                return RedirectToAction("Profile");
            }

            // Save farewell feedback before deleting
            _context.AccountDeletionFeedbacks.Add(new AccountDeletionFeedback
            {
                Reason = reason.Trim(),
                Feedback = string.IsNullOrWhiteSpace(feedback) ? null : feedback.Trim(),
                UserEmail = currentUser.Email,
                UserName = currentUser.FullName,
                UserRole = (await _userManager.GetRolesAsync(currentUser)).FirstOrDefault() ?? "",
                DeletedAt = DateTime.Now
            });
            await _context.SaveChangesAsync();

            // Clean up entities with NoAction delete behavior
            var lessonComments = await _context.LessonComments.Where(c => c.UserId == currentUser.Id).ToListAsync();
            _context.LessonComments.RemoveRange(lessonComments);

            var lessonsLearned = await _context.LessonsLearned.Where(l => l.UserId == currentUser.Id).ToListAsync();
            _context.LessonsLearned.RemoveRange(lessonsLearned);

            var bestPractices = await _context.BestPractices.Where(b => b.UserId == currentUser.Id).ToListAsync();
            _context.BestPractices.RemoveRange(bestPractices);

            var resourceComments = await _context.ResourceComments.Where(c => c.UserId == currentUser.Id).ToListAsync();
            _context.ResourceComments.RemoveRange(resourceComments);

            var discussionPosts = await _context.DiscussionPosts.Where(p => p.UserId == currentUser.Id).ToListAsync();
            _context.DiscussionPosts.RemoveRange(discussionPosts);

            var accessGrants = await _context.ResourceAccessGrants.Where(g => g.UserId == currentUser.Id).ToListAsync();
            _context.ResourceAccessGrants.RemoveRange(accessGrants);

            var likes = await _context.Likes.Where(l => l.UserId == currentUser.Id).ToListAsync();
            _context.Likes.RemoveRange(likes);

            await _context.SaveChangesAsync();

            // Sign out before deleting
            await _signInManager.SignOutAsync();

            // Delete the user (cascades to Resources, Discussions, ReadingHistory, ActivityLogs, Notifications, Recommendations)
            await _userManager.DeleteAsync(currentUser);

            TempData["SuccessMessage"] = "Your account has been permanently deleted. We're sorry to see you go.";
            return RedirectToAction("Landing");
        }

        // ==================== Reading History & Best Practices ====================

        [Authorize]
        public async Task<IActionResult> ReadingHistory()
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            var history = await _context.ReadingHistories
                .Include(h => h.Resource).ThenInclude(r => r!.User)
                .Where(h => h.UserId == currentUser.Id)
                .OrderByDescending(h => h.LastAccessed)
                .ToListAsync();

            ViewBag.ReadingHistory = history.Select(h => new ReadingHistoryViewModel
            {
                ResourceId = h.ResourceId,
                ResourceTitle = h.Resource?.Title ?? "",
                Title = h.Resource?.Title ?? "",
                ResourceAuthor = h.Resource?.User?.FullName ?? "",
                Author = h.Resource?.User?.FullName ?? "",
                ResourceSubject = h.Resource?.Subject ?? "",
                Subject = h.Resource?.Subject ?? "",
                ResourceGrade = h.Resource?.GradeLevel ?? "",
                ResourceFormat = h.Resource?.FileFormat ?? "",
                FileFormat = h.Resource?.FileFormat ?? "",
                IconClass = GetIconClass(h.Resource?.FileFormat ?? ""),
                IconColor = GetIconColor(h.Resource?.FileFormat ?? ""),
                IconBg = GetIconBg(h.Resource?.FileFormat ?? ""),
                Progress = h.ProgressPercent,
                ProgressPercent = h.ProgressPercent,
                LastPosition = h.LastPosition,
                ViewedAt = h.LastAccessed,
                LastAccessedDate = h.LastAccessed,
                CompletedDate = h.CompletedDate,
                IsCompleted = h.ProgressStatus == "Completed",
                IsBookmarked = h.IsBookmarked,
                Status = h.ProgressStatus
            }).ToList();

            // Streaks (same logic as StudentDashboard)
            var activityDates = history.Select(h => h.LastAccessed.Date).Distinct().OrderByDescending(d => d).ToList();
            int currentStreak = 0, bestStreak = 0, streak = 0;
            for (int i = 0; i < activityDates.Count; i++)
            {
                if (i == 0)
                {
                    if ((DateTime.Now.Date - activityDates[i]).TotalDays <= 1)
                        streak = 1;
                    else
                        break;
                }
                else if ((activityDates[i - 1] - activityDates[i]).TotalDays == 1)
                    streak++;
                else
                    break;
            }
            currentStreak = streak;
            streak = 1;
            bestStreak = activityDates.Count > 0 ? 1 : 0;
            for (int i = 1; i < activityDates.Count; i++)
            {
                if ((activityDates[i - 1] - activityDates[i]).TotalDays == 1) { streak++; bestStreak = Math.Max(bestStreak, streak); }
                else streak = 1;
            }
            ViewBag.CurrentStreak = currentStreak;
            ViewBag.BestStreak = bestStreak;

            return View();
        }

        [Authorize]
        public async Task<IActionResult> BestPractices()
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            var resources = await _context.Resources.Include(r => r.User)
                .Where(r => r.Status == "Published")
                .ToListAsync();

            ViewBag.Trending = resources.OrderByDescending(r => r.ViewCount + r.DownloadCount).Take(5).Select(MapResource).ToList();

            if (User.IsInRole("SuperAdmin") || User.IsInRole("Manager"))
            {
                // Admin / Manager Logic
                var subjectStats = resources.GroupBy(r => r.Subject)
                    .Select(g => new
                    {
                        Subject = g.Key,
                        ResourceCount = g.Count(),
                        TotalViews = g.Sum(r => r.ViewCount),
                        AvgRating = g.Any(r => r.Rating > 0) ? g.Where(r => r.Rating > 0).Average(r => r.Rating) : 0,
                        DemandScore = g.Count() > 0 ? (double)g.Sum(r => r.ViewCount) / g.Count() : 0 
                    }).ToList();

                // High Demand, Low Supply (Highest Demand Score)
                ViewBag.ContentGaps = subjectStats.OrderByDescending(s => s.DemandScore).Take(4).ToList();
                
                // Needs Improvement (Lowest Rating but has ratings)
                ViewBag.QualityNeeds = subjectStats.Where(s => s.ResourceCount > 0 && s.AvgRating > 0).OrderBy(s => s.AvgRating).Take(4).ToList();

                // Top Contributors
                var topContributors = resources.GroupBy(r => r.UserId)
                    .Select(g => new
                    {
                        UserId = g.Key,
                        User = g.First().User,
                        TotalUploads = g.Count(),
                        TotalViews = g.Sum(r => r.ViewCount)
                    })
                    .OrderByDescending(c => c.TotalViews)
                    .Take(5).ToList();
                ViewBag.TopContributors = topContributors;

                return View();
            }
            else if (User.IsInRole("Contributor"))
            {
                // Contributor Logic
                var myResources = resources.Where(r => r.UserId == currentUser.Id).ToList();
                var mySubjectStats = myResources.GroupBy(r => r.Subject)
                    .Select(g => new
                    {
                        Subject = g.Key,
                        TotalViews = g.Sum(r => r.ViewCount),
                        TotalDownloads = g.Sum(r => r.DownloadCount)
                    })
                    .OrderByDescending(s => s.TotalViews + s.TotalDownloads)
                    .ToList();
                
                ViewBag.MyTopSubjects = mySubjectStats.Take(3).ToList();
                ViewBag.TotalMyViews = myResources.Sum(r => r.ViewCount);
                ViewBag.TotalMyDownloads = myResources.Sum(r => r.DownloadCount);

                // System Hot Topics
                var globalSubjectStats = resources.GroupBy(r => r.Subject)
                    .Select(g => new { Subject = g.Key, DemandScore = g.Count() > 0 ? (double)g.Sum(r => r.ViewCount) / g.Count() : 0 })
                    .OrderByDescending(s => s.DemandScore).Take(4).ToList();
                ViewBag.SystemHotTopics = globalSubjectStats;

                ViewBag.MyTopResources = myResources.OrderByDescending(r => r.ViewCount).Take(3).Select(MapResource).ToList();

                return View();
            }
            else
            {
                // Student Logic
                var recommendedList = new List<ResourceViewModel>();
                try
                {
                    var recIds = await _recommendationService.GetPersonalizedRecommendationsAsync(currentUser.Id, 8, GetEffectiveSchoolId());
                    if (recIds.Any())
                    {
                        var recResources = resources.Where(r => recIds.Contains(r.ResourceId)).ToList();
                        recommendedList = recIds
                            .Select(rid => recResources.FirstOrDefault(r => r.ResourceId == rid))
                            .Where(r => r != null)
                            .Select(r => MapResource(r!))
                            .ToList();
                    }
                }
                catch { /* fallback below */ }

                if (!recommendedList.Any())
                {
                    recommendedList = resources.OrderByDescending(r => r.Rating).Take(8).Select(MapResource).ToList();
                }
                ViewBag.Recommendations = recommendedList;

                var continueReading = await _context.ReadingHistories
                    .Include(h => h.Resource).ThenInclude(r => r!.User)
                    .Where(h => h.UserId == currentUser.Id && h.ProgressStatus != "Completed")
                    .ToListAsync();

                ViewBag.ContinueReading = continueReading.Select(h => new ReadingHistoryViewModel
                {
                    ResourceId = h.ResourceId,
                    ResourceTitle = h.Resource?.Title ?? "",
                    Title = h.Resource?.Title ?? "",
                    ResourceAuthor = h.Resource?.User?.FullName ?? "",
                    Author = h.Resource?.User?.FullName ?? "",
                    ResourceSubject = h.Resource?.Subject ?? "",
                    Subject = h.Resource?.Subject ?? "",
                    ResourceGrade = h.Resource?.GradeLevel ?? "",
                    ResourceFormat = h.Resource?.FileFormat ?? "",
                    FileFormat = h.Resource?.FileFormat ?? "",
                    IconClass = GetIconClass(h.Resource?.FileFormat ?? ""),
                    IconColor = GetIconColor(h.Resource?.FileFormat ?? ""),
                    IconBg = GetIconBg(h.Resource?.FileFormat ?? ""),
                    Progress = h.ProgressPercent,
                    ProgressPercent = h.ProgressPercent,
                    LastPosition = h.LastPosition,
                    ViewedAt = h.LastAccessed,
                    LastAccessedDate = h.LastAccessed,
                    IsCompleted = false,
                    IsBookmarked = h.IsBookmarked,
                    Status = h.ProgressStatus
                }).ToList();

                var userHistoryAll = await _context.ReadingHistories.Include(h => h.Resource).Where(h => h.UserId == currentUser.Id).ToListAsync();
                
                 var progress = resources.GroupBy(r => r.Subject)
                    .Select(g => 
                    {
                        var subjectTotal = g.Count();
                        var userRead = userHistoryAll.Count(h => h.Resource != null && h.Resource.Subject == g.Key);
                        return new 
                        { 
                            Subject = g.Key, 
                            Percent = subjectTotal > 0 ? Math.Min(100, (int)((double)userRead / subjectTotal * 100)) : 0,
                            Color = GetSubjectColor(g.Key)
                        };
                    })
                    .OrderByDescending(p => p.Percent)
                    .Take(4)
                    .ToList();

                ViewBag.LearningProgress = progress;
                ViewBag.TotalProgress = resources.Any() ? (int)((double)userHistoryAll.Count(h => h.Resource != null) / resources.Count * 100) : 0;

                // User's most read resources (resources the user has accessed, ordered by their popularity)
                var userMostRead = userHistoryAll
                    .Where(h => h.Resource != null && h.Resource.Status == "Published")
                    .OrderByDescending(h => h.Resource!.ViewCount)
                    .Take(5)
                    .Select(h => MapResource(h.Resource!))
                    .ToList();
                ViewBag.UserMostRead = userMostRead;

                return View();
            }
        }

        // ==================== Knowledge Portal & Discussions ====================

        [Authorize]
        public async Task<IActionResult> KnowledgePortal()
        {
            var currentUser = await GetCurrentUserAsync();
            ViewBag.CurrentUserId = currentUser?.Id;
            ViewBag.CurrentUserInitials = currentUser?.Initials ?? "?";
            ViewBag.CurrentUserColor = currentUser?.AvatarColor ?? "";
            ViewBag.CurrentUserName = currentUser?.FullName ?? "";

            var discussions = await _context.Discussions
                .Include(d => d.User)
                .Include(d => d.Posts)
                    .ThenInclude(p => p.User)
                .OrderByDescending(d => d.DateCreated)
                .ToListAsync();

            var likedIds = new HashSet<int>();
            var likedReplyIds = new HashSet<int>();
            if (currentUser != null)
            {
                likedIds = (await _context.Likes
                    .Where(l => l.UserId == currentUser.Id && l.TargetType == "Discussion")
                    .Select(l => l.TargetId)
                    .ToListAsync()).ToHashSet();
                likedReplyIds = (await _context.Likes
                    .Where(l => l.UserId == currentUser.Id && l.TargetType == "Reply")
                    .Select(l => l.TargetId)
                    .ToListAsync()).ToHashSet();
            }

            // Get liker names per discussion (top 3 for preview)
            var discussionIds = discussions.Select(d => d.DiscussionId).ToList();
            var likersByDiscussion = await _context.Likes
                .Where(l => l.TargetType == "Discussion" && discussionIds.Contains(l.TargetId))
                .Include(l => l.User)
                .GroupBy(l => l.TargetId)
                .ToDictionaryAsync(g => g.Key, g => g.OrderByDescending(l => l.CreatedAt).Take(3).Select(l => l.User?.FullName ?? "Someone").ToList());

            // Calculate popular tags
            var allTagsStr = await _context.Discussions
                .Where(d => !string.IsNullOrEmpty(d.Tags))
                .Select(d => d.Tags)
                .ToListAsync();

            var tagCounts = new Dictionary<string, int>();
            foreach (var tagsCsv in allTagsStr)
            {
                if(tagsCsv != null)
                {
                    var splitTags = tagsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim());
                    foreach (var tag in splitTags)
                    {
                        if (tagCounts.ContainsKey(tag)) tagCounts[tag]++;
                        else tagCounts[tag] = 1;
                    }
                }
            }
            ViewBag.PopularTags = tagCounts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).Take(15).ToList();

            // Active contributors
            var topContributors = discussions
                .Where(d => d.User != null)
                .GroupBy(d => d.UserId)
                .Select(g => new { User = g.First().User!, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .Select(x => new { Name = x.User.FullName, Initials = x.User.Initials ?? "?", Color = x.User.AvatarColor ?? "", Posts = x.Count })
                .ToList();
            ViewBag.TopContributors = topContributors;

            // Activity notifications for left sidebar (likes, comments on user's posts)
            var activityNotifications = new List<dynamic>();
            if (currentUser != null)
            {
                var userDiscussionIds = discussions.Where(d => d.UserId == currentUser.Id).Select(d => d.DiscussionId).ToList();

                // Recent likes on user's discussions
                var recentLikes = await _context.Likes
                    .Where(l => l.TargetType == "Discussion" && userDiscussionIds.Contains(l.TargetId) && l.UserId != currentUser.Id)
                    .Include(l => l.User)
                    .OrderByDescending(l => l.CreatedAt)
                    .Take(10)
                    .ToListAsync();

                foreach (var like in recentLikes)
                {
                    var disc = discussions.FirstOrDefault(d => d.DiscussionId == like.TargetId);
                    activityNotifications.Add(new { Type = "like", UserName = like.User?.FullName ?? "Someone", Initials = like.User?.Initials ?? "?", Color = like.User?.AvatarColor ?? "", Title = disc?.Title ?? "", DiscussionId = like.TargetId, CreatedAt = like.CreatedAt });
                }

                // Recent replies on user's discussions
                var recentReplies = await _context.DiscussionPosts
                    .Where(p => userDiscussionIds.Contains(p.DiscussionId) && p.UserId != currentUser.Id)
                    .Include(p => p.User)
                    .OrderByDescending(p => p.DatePosted)
                    .Take(10)
                    .ToListAsync();

                foreach (var reply in recentReplies)
                {
                    var disc = discussions.FirstOrDefault(d => d.DiscussionId == reply.DiscussionId);
                    activityNotifications.Add(new { Type = "comment", UserName = reply.User?.FullName ?? "Someone", Initials = reply.User?.Initials ?? "?", Color = reply.User?.AvatarColor ?? "", Title = disc?.Title ?? "", DiscussionId = reply.DiscussionId, CreatedAt = reply.DatePosted });
                }

                activityNotifications = activityNotifications.OrderByDescending(a => (DateTime)a.CreatedAt).Take(15).ToList();
            }
            ViewBag.ActivityNotifications = activityNotifications;

            ViewBag.Discussions = discussions.Select(d => {
                var vm = MapDiscussion(d, likedIds.Contains(d.DiscussionId));
                vm.LikerNames = likersByDiscussion.ContainsKey(d.DiscussionId) ? likersByDiscussion[d.DiscussionId] : new List<string>();
                // Latest 2 replies for inline preview
                vm.LatestReplies = (d.Posts ?? new List<DiscussionPost>())
                    .OrderByDescending(p => p.DatePosted)
                    .Take(2)
                    .Select(p => MapReply(p, likedReplyIds.Contains(p.PostId)))
                    .Reverse()
                    .ToList();
                return vm;
            }).ToList();
            return View();
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> PostDiscussion(DiscussionViewModel model)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            var discussion = new Discussion
            {
                Title = model.Title ?? "",
                Content = model.Content ?? "",
                Category = model.Category ?? "",
                Type = model.Type ?? "Question",
                Tags = model.Tags != null ? string.Join(", ", model.Tags) : "",
                UserId = currentUser.Id,
                SchoolId = currentUser.SchoolId,
                Status = "Open",
                DateCreated = DateTime.Now
            };

            _context.Discussions.Add(discussion);
            await _context.SaveChangesAsync();
            await LogActivity(currentUser.Id, "Discussion", discussion.Title);

            TempData["SuccessMessage"] = "Your discussion has been posted successfully!";
            TempData["SuccessTitle"] = "Discussion Started";
            return RedirectToAction("KnowledgePortal");
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> EditDiscussion(int id, string title, string content, string type, string tags)
        {
            var discussion = await _context.Discussions.FindAsync(id);
            if (discussion != null)
            {
                discussion.Title = title;
                discussion.Content = content;
                discussion.Type = type;
                if(tags != null)
                    discussion.Tags = string.Join(", ", tags.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()));
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Discussion updated successfully!";
            }
            return RedirectToAction("KnowledgePortal");
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> DeleteDiscussion(int id)
        {
            var discussion = await _context.Discussions.FindAsync(id);
            if (discussion != null)
            {
                _context.Discussions.Remove(discussion);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Discussion deleted successfully.";
            }
            return RedirectToAction("KnowledgePortal");
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> LikeDiscussion(int id)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Json(new { success = false });

            var existing = await _context.Likes
                .FirstOrDefaultAsync(l => l.UserId == currentUser.Id && l.TargetType == "Discussion" && l.TargetId == id);

            var discussion = await _context.Discussions.FindAsync(id);
            if (discussion == null) return Json(new { success = false });

            if (existing != null)
            {
                // Unlike — remove the like and decrement
                _context.Likes.Remove(existing);
                discussion.LikeCount = Math.Max(0, discussion.LikeCount - 1);
                await _context.SaveChangesAsync();
                return Json(new { success = true, likes = discussion.LikeCount, isLiked = false });
            }

            _context.Likes.Add(new Like { UserId = currentUser.Id, TargetType = "Discussion", TargetId = id, CreatedAt = DateTime.Now });
            discussion.LikeCount++;

            await _context.SaveChangesAsync();
            return Json(new { success = true, likes = discussion.LikeCount, isLiked = true });
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> PostReply(int discussionId, string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return RedirectToAction("Discussions", new { id = discussionId });

            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            var post = new DiscussionPost
            {
                DiscussionId = discussionId,
                UserId = currentUser.Id,
                Content = content,
                DatePosted = DateTime.Now
            };

            _context.DiscussionPosts.Add(post);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Your reply has been posted successfully!";
            return RedirectToAction("Discussions", new { id = discussionId });
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> DeleteReply(int id, int discussionId)
        {
            var reply = await _context.DiscussionPosts.FindAsync(id);
            if (reply != null)
            {
                _context.DiscussionPosts.Remove(reply);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Reply deleted successfully.";
            }
            return RedirectToAction("Discussions", new { id = discussionId });
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> LikeReply(int id)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Json(new { success = false });

            var existing = await _context.Likes
                .FirstOrDefaultAsync(l => l.UserId == currentUser.Id && l.TargetType == "Reply" && l.TargetId == id);

            var reply = await _context.DiscussionPosts.FindAsync(id);
            if (reply == null) return Json(new { success = false });

            if (existing != null)
            {
                // Unlike — remove the like and decrement
                _context.Likes.Remove(existing);
                reply.LikeCount = Math.Max(0, reply.LikeCount - 1);
                await _context.SaveChangesAsync();
                return Json(new { success = true, likes = reply.LikeCount, isLiked = false });
            }

            _context.Likes.Add(new Like { UserId = currentUser.Id, TargetType = "Reply", TargetId = id, CreatedAt = DateTime.Now });
            reply.LikeCount++;

            await _context.SaveChangesAsync();
            return Json(new { success = true, likes = reply.LikeCount, isLiked = true });
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetDiscussionLikers(int id, string type = "Discussion")
        {
            var likers = await _context.Likes
                .Where(l => l.TargetType == type && l.TargetId == id)
                .Include(l => l.User)
                .OrderByDescending(l => l.CreatedAt)
                .Select(l => new { 
                    name = l.User != null ? l.User.FirstName + " " + l.User.LastName : "Someone",
                    initials = l.User != null ? l.User.Initials : "?",
                    color = l.User != null ? l.User.AvatarColor : "",
                    role = "User"
                })
                .ToListAsync();
            return Json(likers);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> QuickReply(int discussionId, string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return Json(new { success = false });

            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Json(new { success = false });

            var post = new DiscussionPost
            {
                DiscussionId = discussionId,
                UserId = currentUser.Id,
                Content = System.Net.WebUtility.HtmlEncode(content),
                DatePosted = DateTime.Now
            };

            _context.DiscussionPosts.Add(post);
            await _context.SaveChangesAsync();

            return Json(new { 
                success = true, 
                reply = new {
                    id = post.PostId,
                    content = post.Content,
                    author = currentUser.FullName,
                    initials = currentUser.Initials ?? "?",
                    color = currentUser.AvatarColor ?? "",
                    time = "Just now"
                }
            });
        }

        [Authorize]
        public async Task<IActionResult> Discussions(int id)
        {
            var currentUser = await GetCurrentUserAsync();
            ViewBag.CurrentUserId = currentUser?.Id;
            ViewBag.CurrentUserInitials = currentUser?.Initials ?? "?";
            ViewBag.CurrentUserColor = currentUser?.AvatarColor ?? "";
            ViewBag.CurrentUserName = currentUser?.FullName ?? "";

            var discussions = await _context.Discussions
                .Include(d => d.User)
                .Include(d => d.Posts)
                .ToListAsync();

            var discussion = discussions.FirstOrDefault(d => d.DiscussionId == id)
                ?? discussions.FirstOrDefault();

            if (discussion == null) return RedirectToAction("KnowledgePortal");

            // Increment view count
            discussion.ViewCount++;
            await _context.SaveChangesAsync();

            var posts = await _context.DiscussionPosts
                .Include(p => p.User)
                .Where(p => p.DiscussionId == discussion.DiscussionId)
                .OrderBy(p => p.DatePosted)
                .ToListAsync();

            var likedReplyIds = new HashSet<int>();
            var isDiscussionLiked = false;
            if (currentUser != null)
            {
                likedReplyIds = (await _context.Likes
                    .Where(l => l.UserId == currentUser.Id && l.TargetType == "Reply")
                    .Select(l => l.TargetId)
                    .ToListAsync()).ToHashSet();
                isDiscussionLiked = await _context.Likes
                    .AnyAsync(l => l.UserId == currentUser.Id && l.TargetType == "Discussion" && l.TargetId == discussion.DiscussionId);
            }

            // Get liker names for discussion
            var discLikers = await _context.Likes
                .Where(l => l.TargetType == "Discussion" && l.TargetId == discussion.DiscussionId)
                .Include(l => l.User)
                .OrderByDescending(l => l.CreatedAt)
                .Take(10)
                .Select(l => l.User != null ? l.User.FirstName + " " + l.User.LastName : "Someone")
                .ToListAsync();

            var discVm = MapDiscussion(discussion, isDiscussionLiked);
            discVm.LikerNames = discLikers;

            ViewBag.Discussion = discVm;
            ViewBag.Replies = posts.Select(p => MapReply(p, likedReplyIds.Contains(p.PostId))).ToList();
            ViewBag.SimilarDiscussions = discussions
                .Where(d => d.DiscussionId != discussion.DiscussionId)
                .Take(3)
                .Select(d => MapDiscussion(d))
                .ToList();
            return View();
        }

        // ==================== Upload / Content ====================

        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        public async Task<IActionResult> Upload(int? id)
        {
            await LoadSchoolSettingsToViewBag();
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            Resource? resource = null;
            if (id.HasValue && id.Value > 0)
            {
                resource = await _context.Resources.FirstOrDefaultAsync(r => r.ResourceId == id.Value);
                if (resource == null || (!User.IsInRole("SuperAdmin") && !User.IsInRole("Manager") && resource.UserId != currentUser.Id))
                {
                    return NotFound();
                }
            }

            ViewBag.IsPublishedEdit = resource != null && resource.Status == "Published";

            var recentUploads = await _context.Resources
                .Include(r => r.User)
                .Where(r => r.UserId == currentUser.Id)
                .OrderByDescending(r => r.DateUploaded)
                .Take(3)
                .ToListAsync();

            ViewBag.RecentUploads = recentUploads.Select(MapResource).ToList();

            // Load all categories for the category picker
            ViewBag.AllCategories = await _context.ResourceCategories.OrderBy(c => c.CategoryName).ToListAsync();

            // Load existing tags and categories when editing
            if (resource != null)
            {
                var resourceTagNames = await _context.ResourceTags
                    .Where(rt => rt.ResourceId == resource.ResourceId)
                    .Include(rt => rt.Tag)
                    .Select(rt => rt.Tag!.TagName)
                    .ToListAsync();
                ViewBag.ExistingTags = string.Join(", ", resourceTagNames);

                ViewBag.ExistingCategoryIds = await _context.ResourceCategoryMaps
                    .Where(m => m.ResourceId == resource.ResourceId)
                    .Select(m => m.CategoryId)
                    .ToListAsync();
            }
            else
            {
                ViewBag.ExistingTags = "";
                ViewBag.ExistingCategoryIds = new List<int>();
            }

            return View(resource);
        }

        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        [HttpPost]
        public async Task<IActionResult> Upload(int? resourceId, string title, string description, string subject,
            string gradeLevel, string resourceType, string quarter, IFormFile? file, string? tags = null, int[]? categoryIds = null, string? versionNotes = null, bool isDraft = false,
            string? accessLevel = null, string? accessDuration = null, DateTime? accessExpiresAt = null,
            bool allowDownloads = true, bool allowComments = true, bool enableVersionHistory = false,
            string? restrictedUserIds = null)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            Resource? resource;
            bool isNew = false;

            if (resourceId.HasValue && resourceId.Value > 0)
            {
                resource = await _context.Resources.FindAsync(resourceId.Value);
                if (resource == null || (!User.IsInRole("SuperAdmin") && !User.IsInRole("Manager") && resource.UserId != currentUser.Id))
                {
                    return NotFound();
                }
            }
            else
            {
                resource = new Resource
                {
                    UserId = currentUser.Id,
                    SchoolId = currentUser.SchoolId,
                    DateUploaded = DateTime.Now
                };
                _context.Resources.Add(resource);
                isNew = true;
            }

            // Track if this is an edit of a published resource
            bool isPublishedEdit = !isNew && resource.Status == "Published";

            // For published resources, snapshot the current state before applying changes when version history is enabled
            if (isPublishedEdit && resource.EnableVersionHistory)
            {
                var existingVersionCount = await _context.ResourceVersions.CountAsync(v => v.ResourceId == resource.ResourceId);

                if (existingVersionCount == 0)
                {
                    // Create V1 to preserve the original published state
                    _context.ResourceVersions.Add(new ResourceVersion
                    {
                        ResourceId = resource.ResourceId,
                        VersionNumber = "V1",
                        VersionNotes = "Original published version",
                        FilePath = resource.FilePath,
                        FileFormat = resource.FileFormat,
                        FileSize = resource.FileSize,
                        Title = resource.Title,
                        Description = resource.Description,
                        Subject = resource.Subject,
                        GradeLevel = resource.GradeLevel,
                        ResourceType = resource.ResourceType,
                        Quarter = resource.Quarter,
                        DateUpdated = resource.DateUploaded
                    });
                }
            }

            resource.Title = title ?? "";
            resource.Description = description ?? "";
            resource.Subject = subject ?? "";
            resource.GradeLevel = gradeLevel ?? "";
            resource.ResourceType = resourceType ?? "";
            resource.Quarter = quarter ?? "";

            // Published resources stay Published after edits (no re-approval needed)
            if (!isPublishedEdit)
            {
                resource.Status = isDraft ? "Draft" : "Pending";
                resource.PendingReviewPreviewedAt = isDraft ? resource.PendingReviewPreviewedAt : null;
            }

            // ---- Policy Settings ----
            resource.AccessLevel = accessLevel ?? "Registered";
            resource.AccessDuration = accessDuration ?? "Unlimited";
            resource.AccessExpiresAt = (accessDuration == "Custom" && accessExpiresAt.HasValue)
                ? accessExpiresAt.Value
                : null;
            resource.AllowDownloads = allowDownloads;
            resource.AllowComments = allowComments;
            resource.EnableVersionHistory = enableVersionHistory;

            // Handle file upload — save to Google Drive
            if (file != null && file.Length > 0)
            {
                using (var stream = file.OpenReadStream())
                {
                    var result = await _storage.UploadAsync(stream, file.FileName, file.ContentType);
                    if (!result.Success)
                    {
                        TempData["ErrorMessage"] = $"File upload failed: {result.Message}";
                        return RedirectToUploadForm(resourceId);
                    }

                    resource.FilePath = result.WebViewLink ?? result.WebContentLink ?? "";

                    resource.FileFormat = Path.GetExtension(file.FileName).TrimStart('.').ToUpperInvariant();
                    resource.FileSize = result.FileSize ?? "";
                }

                if (!isPublishedEdit && resource.EnableVersionHistory)
                {
                    var versionNumber = "V1";
                    if (!isNew)
                    {
                        var previousVersionsCount = await _context.ResourceVersions.CountAsync(v => v.ResourceId == resource.ResourceId);
                        versionNumber = $"V{previousVersionsCount + 1}";
                    }

                    var newVersion = new ResourceVersion
                    {
                        Resource = resource,
                        VersionNumber = versionNumber,
                        VersionNotes = versionNotes,
                        FilePath = resource.FilePath,
                        FileFormat = resource.FileFormat,
                        FileSize = resource.FileSize,
                        Title = resource.Title,
                        Description = resource.Description,
                        Subject = resource.Subject,
                        GradeLevel = resource.GradeLevel,
                        ResourceType = resource.ResourceType,
                        Quarter = resource.Quarter,
                        DateUpdated = DateTime.Now
                    };
                    _context.ResourceVersions.Add(newVersion);
                }
            }

            // For published edits, create a new version with the updated state
            if (isPublishedEdit && resource.EnableVersionHistory)
            {
                var versionCount = await _context.ResourceVersions.CountAsync(v => v.ResourceId == resource.ResourceId);
                _context.ResourceVersions.Add(new ResourceVersion
                {
                    ResourceId = resource.ResourceId,
                    VersionNumber = $"V{versionCount + 1}",
                    VersionNotes = versionNotes ?? "Updated",
                    FilePath = resource.FilePath,
                    FileFormat = resource.FileFormat,
                    FileSize = resource.FileSize,
                    Title = resource.Title,
                    Description = resource.Description,
                    Subject = resource.Subject,
                    GradeLevel = resource.GradeLevel,
                    ResourceType = resource.ResourceType,
                    Quarter = resource.Quarter,
                    DateUpdated = DateTime.Now
                });
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Failed to save resource: {ex.InnerException?.Message ?? ex.Message}";
                return RedirectToUploadForm(resourceId);
            }

            // --- Process Tags ---
            var existingTags = _context.ResourceTags.Where(rt => rt.ResourceId == resource.ResourceId);
            _context.ResourceTags.RemoveRange(existingTags);

            if (!string.IsNullOrWhiteSpace(tags))
            {
                var tagNames = tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t => t.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var tagName in tagNames)
                {
                    var tag = await _context.Tags.FirstOrDefaultAsync(t => t.TagName.ToLower() == tagName.ToLower());
                    if (tag == null)
                    {
                        tag = new Tag { TagName = tagName };
                        _context.Tags.Add(tag);
                        await _context.SaveChangesAsync();
                    }
                    _context.ResourceTags.Add(new ResourceTag { ResourceId = resource.ResourceId, TagId = tag.TagId });
                }
            }

            // --- Process Categories ---
            var existingCats = _context.ResourceCategoryMaps.Where(m => m.ResourceId == resource.ResourceId);
            _context.ResourceCategoryMaps.RemoveRange(existingCats);

            if (categoryIds != null && categoryIds.Length > 0)
            {
                foreach (var catId in categoryIds.Distinct())
                {
                    _context.ResourceCategoryMaps.Add(new ResourceCategoryMap { ResourceId = resource.ResourceId, CategoryId = catId });
                }
            }

            // --- Process Restricted Access Grants ---
            var existingGrants = _context.ResourceAccessGrants.Where(g => g.ResourceId == resource.ResourceId);
            _context.ResourceAccessGrants.RemoveRange(existingGrants);

            if (resource.AccessLevel == "Restricted" && !string.IsNullOrWhiteSpace(restrictedUserIds))
            {
                var userIds = restrictedUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(id => id.Trim())
                    .Where(id => id.Length > 0)
                    .Distinct()
                    .ToList();

                foreach (var userId in userIds)
                {
                    _context.ResourceAccessGrants.Add(new ResourceAccessGrant
                    {
                        ResourceId = resource.ResourceId,
                        UserId = userId,
                        GrantedAt = DateTime.Now
                    });
                }
            }

            await _context.SaveChangesAsync();

            await LogActivity(currentUser.Id, isNew ? "Upload" : "Edit", resource.Title, resource.ResourceId);

            if (isPublishedEdit)
            {
                TempData["SuccessMessage"] = resource.EnableVersionHistory
                    ? "Resource updated successfully! A new version has been saved to the version history."
                    : "Resource updated successfully!";
                TempData["SuccessTitle"] = "Update Saved";
            }
            else if (isDraft)
            {
                TempData["SuccessMessage"] = "Resource saved as draft successfully!";
                TempData["SuccessTitle"] = "Draft Saved";
            }
            else
            {
                // Notify managers and admins about the new pending resource
                var managerUserIds = await _userManager.GetUsersInRoleAsync("Manager");
                var adminUserIds = await _userManager.GetUsersInRoleAsync("SuperAdmin");
                var reviewers = managerUserIds.Concat(adminUserIds)
                    .Where(u => u.Id != currentUser.Id)
                    .Select(u => u.Id)
                    .Distinct();

                foreach (var reviewerId in reviewers)
                {
                    _context.Notifications.Add(new Notification
                    {
                        UserId = reviewerId,
                        Title = "New resource pending review",
                        Message = $"\"{resource.Title}\" was uploaded by {currentUser.FullName} and needs review.",
                        Type = "Upload",
                        Icon = "bi-cloud-arrow-up",
                        IconBg = "#dbeafe",
                        ResourceId = resource.ResourceId,
                        Link = "/Home/Dashboard"
                    });
                }
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Resource uploaded successfully! It is now pending review.";
                TempData["SuccessTitle"] = "Upload Successful";
            }
            return RedirectToAction("MyUploads");
        }

        // ==================== Tags API for Type-Ahead ====================

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> SearchTags(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return Json(Array.Empty<object>());

            var tags = await _context.Tags
                .Where(t => t.TagName.Contains(term))
                .OrderBy(t => t.TagName)
                .Take(10)
                .Select(t => new { value = t.TagName })
                .ToListAsync();

            return Json(tags);
        }

        // ==================== School Users API (for restricted access modal) ====================

        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        [HttpGet]
        public async Task<IActionResult> GetSchoolUsers()
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Unauthorized();

            var schoolId = GetEffectiveSchoolId();
            var allUsers = await _userManager.Users.ToListAsync();

            var users = schoolId.HasValue
                ? allUsers.Where(u => u.SchoolId == schoolId.Value && u.Id != currentUser.Id)
                : allUsers.Where(u => u.Id != currentUser.Id);

            var result = new List<object>();
            foreach (var u in users.OrderBy(u => u.FullName))
            {
                var roles = await _userManager.GetRolesAsync(u);
                result.Add(new
                {
                    userId = u.Id,
                    fullName = u.FullName,
                    email = u.Email,
                    initials = u.Initials,
                    avatarColor = u.AvatarColor,
                    role = roles.FirstOrDefault() ?? "Student"
                });
            }

            return Json(result);
        }

        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        [HttpGet]
        public async Task<IActionResult> GetResourceAccessGrants(int resourceId)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Unauthorized();

            var resource = await _context.Resources.FindAsync(resourceId);
            if (resource == null) return NotFound();
            if (resource.UserId != currentUser.Id && !User.IsInRole("SuperAdmin"))
                return Forbid();

            var grants = await _context.ResourceAccessGrants
                .Where(g => g.ResourceId == resourceId)
                .Include(g => g.User)
                .Select(g => new
                {
                    userId = g.UserId,
                    fullName = g.User!.FullName,
                    email = g.User.Email,
                    initials = g.User.Initials,
                    avatarColor = g.User.AvatarColor
                })
                .ToListAsync();

            return Json(grants);
        }



        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        public async Task<IActionResult> MyUploads(string? search, string? status, int page = 1, int pageSize = 12)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            IQueryable<Resource> query;
            if (User.IsInRole("SuperAdmin"))
            {
                query = _context.Resources.Include(r => r.User);
            }
            else
            {
                query = _context.Resources
                    .Include(r => r.User)
                    .Where(r => r.UserId == currentUser.Id);
            }

            // Filter
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(r => r.Title.Contains(search) || r.Description.Contains(search));
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(r => r.Status == status);

            // Stats (before pagination)
            var allResources = User.IsInRole("SuperAdmin")
                ? await _context.Resources.ToListAsync()
                : await _context.Resources.Where(r => r.UserId == currentUser.Id).ToListAsync();

            ViewBag.TotalUploads = allResources.Count;
            ViewBag.PublishedCount = allResources.Count(r => r.Status == "Published");
            ViewBag.PendingCount = allResources.Count(r => r.Status == "Pending");
            ViewBag.DraftCount = allResources.Count(r => r.Status == "Draft");
            ViewBag.RejectedCount = allResources.Count(r => r.Status == "Rejected");

            var yesterday = DateTime.Now.Date;
            var yesterdayResources = allResources.Where(r => r.DateUploaded < yesterday).ToList();
            ViewBag.YesterdayUploads = yesterdayResources.Count;
            ViewBag.YesterdayPublished = yesterdayResources.Count(r => r.Status == "Published");
            ViewBag.YesterdayPending = yesterdayResources.Count(r => r.Status == "Pending");
            ViewBag.YesterdayDrafts = yesterdayResources.Count(r => r.Status == "Draft");

            query = query.OrderByDescending(r => r.DateUploaded);
            var totalCount = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            ViewBag.Uploads = items.Select(MapResource).ToList();

            ViewBag.CurrentSearch = search ?? "";
            ViewBag.CurrentStatus = status ?? "";
            ViewBag.Pagination = new {
                PageIndex = page,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                TotalCount = totalCount,
                PageSize = pageSize,
                HasPreviousPage = page > 1,
                HasNextPage = page < (int)Math.Ceiling(totalCount / (double)pageSize),
                StartPage = Math.Max(1, page - 2),
                EndPage = Math.Min((int)Math.Ceiling(totalCount / (double)pageSize), page + 2),
                BaseUrl = Url.Action("MyUploads", "Home"),
                QueryParams = BuildMyUploadsQuery(search, status)
            };
            return View();
        }

        private string BuildMyUploadsQuery(string? search, string? status)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(search)) parts.Add($"search={Uri.EscapeDataString(search)}");
            if (!string.IsNullOrWhiteSpace(status)) parts.Add($"status={Uri.EscapeDataString(status)}");
            return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
        }

        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        [HttpPost]
        public async Task<IActionResult> DeleteResource(int[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                return Json(new { success = false, message = "No resources selected." });
            }

            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Unauthorized();

            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var resourcesToDelete = await _context.Resources
                        .Where(r => ids.Contains(r.ResourceId))
                        .ToListAsync();

                // Only SuperAdmin can delete anyone's resources. 
                
                if (!User.IsInRole("SuperAdmin"))
                {
                    resourcesToDelete = resourcesToDelete.Where(r => r.UserId == currentUser.Id).ToList();
                }

                if (resourcesToDelete.Count == 0)
                {
                    return Json(new { success = false, message = "No valid resources found to delete." });
                }

                var resourceIds = resourcesToDelete.Select(r => r.ResourceId).ToList();

                // 1. Remove related entities that don't have cascade delete
                var relatedHistory = await _context.ReadingHistories.Where(h => resourceIds.Contains(h.ResourceId)).ToListAsync();
                _context.ReadingHistories.RemoveRange(relatedHistory);

                var relatedLogs = await _context.UserActivityLogs.Where(l => l.ResourceId.HasValue && resourceIds.Contains(l.ResourceId.Value)).ToListAsync();
                _context.UserActivityLogs.RemoveRange(relatedLogs);

                var relatedNotifications = await _context.Notifications.Where(n => n.ResourceId.HasValue && resourceIds.Contains(n.ResourceId.Value)).ToListAsync();
                _context.Notifications.RemoveRange(relatedNotifications);

                var relatedRecommendations = await _context.Recommendations.Where(r => resourceIds.Contains(r.ResourceId)).ToListAsync();
                _context.Recommendations.RemoveRange(relatedRecommendations);

                // 2. Delete physical files and Resources
                foreach (var resource in resourcesToDelete)
                {
                    if (!string.IsNullOrEmpty(resource.FilePath))
                    {
                        var filePath = resource.FilePath.Trim();
                        var fullPath = Path.Combine(_environment.WebRootPath, "uploads", filePath);
                        if (System.IO.File.Exists(fullPath))
                        {
                            System.IO.File.Delete(fullPath);
                        }
                    }
                    _context.Resources.Remove(resource);
                    
                    // Log the deletion activity (without ResourceId to avoid FK ref issues if database is weird)
                    _context.UserActivityLogs.Add(new UserActivityLog
                    {
                        UserId = currentUser.Id,
                        ActivityType = "Delete",
                        TargetTitle = resource.Title,
                        ActivityDate = DateTime.Now
                    });
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new { success = true, message = $"{resourcesToDelete.Count} resource(s) deleted successfully." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "An error occurred while deleting resources: " + ex.Message });
            }
            });
        }

        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        public async Task<IActionResult> Policies()
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            // Load only resources uploaded by the current user
            var myResources = await _context.Resources
                .Where(r => r.UserId == currentUser.Id)
                .OrderByDescending(r => r.DateUploaded)
                .Select(r => new { r.ResourceId, r.Title, r.ResourceType, r.DateUploaded })
                .ToListAsync();

            ViewBag.MyResources = myResources;

            // Also load version history for sidebar display
            ViewBag.ResourceVersions = await _context.ResourceVersions
                .Include(v => v.Resource)
                .Where(v => v.Resource != null && v.Resource.UserId == currentUser.Id)
                .OrderByDescending(v => v.DateUpdated)
                .Take(10)
                .ToListAsync();

            return View();
        }

        [HttpGet]
        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        public async Task<IActionResult> GetResourcePolicy(int id)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Unauthorized();

            var resource = await _context.Resources.FirstOrDefaultAsync(r => r.ResourceId == id);
            if (resource == null) return NotFound();

            // Security: only the owner (or SuperAdmin) can view/edit policies
            if (resource.UserId != currentUser.Id && !User.IsInRole("SuperAdmin"))
                return Forbid();

            // Load restricted user IDs for this resource
            var restrictedUserIds = await _context.ResourceAccessGrants
                .Where(g => g.ResourceId == id)
                .Select(g => g.UserId)
                .ToListAsync();

            return Json(new
            {
                resourceId = resource.ResourceId,
                title = resource.Title,
                accessLevel = resource.AccessLevel,
                accessDuration = resource.AccessDuration,
                allowDownloads = resource.AllowDownloads,
                enableVersionHistory = resource.EnableVersionHistory,
                requireVersionNotes = resource.RequireVersionNotes,
                allowComments = resource.AllowComments,
                allowRatings = resource.AllowRatings,
                moderateComments = resource.ModerateComments,
                restrictedUserIds = restrictedUserIds
            });
        }

        [HttpPost]
        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        public async Task<IActionResult> SaveResourcePolicy(
            int resourceId,
            string accessLevel,
            string accessDuration,
            bool allowDownloads,
            bool enableVersionHistory,
            bool requireVersionNotes,
            bool allowComments,
            bool allowRatings,
            bool moderateComments,
            List<string>? restrictedUserIds = null)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Unauthorized();

            var resource = await _context.Resources.FirstOrDefaultAsync(r => r.ResourceId == resourceId);
            if (resource == null) return NotFound();

            // Security: only the owner (or SuperAdmin) can edit policies
            if (resource.UserId != currentUser.Id && !User.IsInRole("SuperAdmin"))
                return Forbid();

            resource.AccessLevel = accessLevel ?? "Registered";
            resource.AccessDuration = accessDuration ?? "Unlimited";
            resource.AllowDownloads = allowDownloads;
            resource.EnableVersionHistory = enableVersionHistory;
            resource.RequireVersionNotes = requireVersionNotes;
            resource.AllowComments = allowComments;
            resource.AllowRatings = allowRatings;
            resource.ModerateComments = moderateComments;

            // Handle restricted user access grants
            if (accessLevel == "Restricted" && restrictedUserIds != null && restrictedUserIds.Any())
            {
                // Remove existing grants for this resource
                var existingGrants = await _context.ResourceAccessGrants
                    .Where(g => g.ResourceId == resourceId)
                    .ToListAsync();
                _context.ResourceAccessGrants.RemoveRange(existingGrants);

                // Add new grants
                foreach (var userId in restrictedUserIds)
                {
                    _context.ResourceAccessGrants.Add(new ResourceAccessGrant
                    {
                        ResourceId = resourceId,
                        UserId = userId,
                        GrantedAt = DateTime.Now
                    });
                }
            }
            else if (accessLevel != "Restricted")
            {
                // Clear grants if access level is no longer restricted
                var existingGrants = await _context.ResourceAccessGrants
                    .Where(g => g.ResourceId == resourceId)
                    .ToListAsync();
                _context.ResourceAccessGrants.RemoveRange(existingGrants);
            }

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Policy settings saved successfully." });
        }

        [HttpGet]
        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        public async Task<IActionResult> GetAllUsers()
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Unauthorized();

            var schoolId = GetEffectiveSchoolId();

            // Get users from the same school (or all if no school context)
            var usersQuery = _userManager.Users.AsQueryable();
            if (schoolId.HasValue)
                usersQuery = usersQuery.Where(u => u.SchoolId == schoolId.Value);

            var users = await usersQuery
                .Where(u => u.Id != currentUser.Id) // Exclude current user
                .OrderBy(u => u.FullName)
                .Select(u => new
                {
                    id = u.Id,
                    fullName = u.FullName,
                    userName = u.UserName,
                    email = u.Email ?? ""
                })
                .ToListAsync();

            return Json(users);
        }

        // ==================== Administration — SuperAdmin & Manager ====================

        [Authorize(Roles = "SuperAdmin,Manager")]
        public async Task<IActionResult> Users(string? search, string? role, string? status, int page = 1, int pageSize = 12)
        {
            var schoolId = GetEffectiveSchoolId();
            var allUsers = await _userManager.Users.Include(u => u.Department).ToListAsync();
            
            var users = schoolId.HasValue
                ? allUsers.Where(u => u.SchoolId == schoolId.Value).ToList()
                : allUsers;
            var userViewModels = new List<UserViewModel>();

            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                var roleStr = roles.FirstOrDefault() ?? "Student";
                var resourceCount = await _context.Resources.CountAsync(r => r.UserId == u.Id);

                userViewModels.Add(new UserViewModel
                {
                    Name = u.FullName,
                    Email = u.Email ?? "",
                    Initials = u.Initials,
                    AvatarColor = u.AvatarColor,
                    Role = roleStr,
                    RoleBadgeClass = GetRoleBadge(roleStr),
                    GradeOrPosition = u.GradeOrPosition,
                    Status = u.Status,
                    StatusBadgeClass = GetStatusBadge(u.Status),
                    JoinedAt = u.DateCreated,
                    LastActive = GetTimeAgo(u.DateCreated),
                    ResourceCount = resourceCount,
                    SuspensionReason = u.SuspensionReason,
                    SuspensionDate = u.SuspensionDate
                });
            }

            // Filter
            var filtered = userViewModels.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(search))
                filtered = filtered.Where(u => u.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || u.Email.Contains(search, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(role))
                filtered = filtered.Where(u => u.Role == role);
            if (!string.IsNullOrWhiteSpace(status))
                filtered = filtered.Where(u => u.Status == status);

            var filteredList = filtered.ToList();
            var totalCount = filteredList.Count;
            var pagedUsers = filteredList.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            ViewBag.UserList = pagedUsers;
            ViewBag.TotalUsers = userViewModels.Count;
            ViewBag.ActiveCount = userViewModels.Count(u => u.Status == "Active");
            ViewBag.ContributorCount = userViewModels.Count(u => u.Role == "Contributor");
            ViewBag.ManagerCount = userViewModels.Count(u => u.Role == "Manager");

            var yesterday = DateTime.Now.Date;
            var yesterdayUsers = userViewModels.Where(u => u.JoinedAt < yesterday).ToList();
            ViewBag.YesterdayTotalUsers = yesterdayUsers.Count;
            ViewBag.YesterdayActiveCount = yesterdayUsers.Count(u => u.Status == "Active");
            ViewBag.YesterdayContributorCount = yesterdayUsers.Count(u => u.Role == "Contributor");
            ViewBag.YesterdayManagerCount = yesterdayUsers.Count(u => u.Role == "Manager");

            ViewBag.CurrentSearch = search ?? "";
            ViewBag.CurrentRole = role ?? "";
            ViewBag.CurrentStatusFilter = status ?? "";
            ViewBag.Pagination = new {
                PageIndex = page,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                TotalCount = totalCount,
                PageSize = pageSize,
                HasPreviousPage = page > 1,
                HasNextPage = page < (int)Math.Ceiling(totalCount / (double)pageSize),
                StartPage = Math.Max(1, page - 2),
                EndPage = Math.Min((int)Math.Ceiling(totalCount / (double)pageSize), page + 2),
                BaseUrl = Url.Action("Users", "Home"),
                QueryParams = BuildUsersQuery(search, role, status)
            };
            ViewBag.Schools = await _context.Schools.Where(s => s.IsActive).OrderBy(s => s.Name).ToListAsync();
            return View();
        }

        private string BuildUsersQuery(string? search, string? role, string? status)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(search)) parts.Add($"search={Uri.EscapeDataString(search)}");
            if (!string.IsNullOrWhiteSpace(role)) parts.Add($"role={Uri.EscapeDataString(role)}");
            if (!string.IsNullOrWhiteSpace(status)) parts.Add($"status={Uri.EscapeDataString(status)}");
            return parts.Count > 0 ? "?" + string.Join("&", parts) : "";
        }

        [Authorize(Roles = "SuperAdmin,Manager")]
        [HttpPost]
        public async Task<IActionResult> PostAddUser(UserViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Name))
            {
                TempData["ErrorMessage"] = "Name and email are required.";
                return RedirectToAction("Users");
            }

            var currentUser = await GetCurrentUserAsync();
            var names = model.Name.Split(' ', 2);
            var firstName = names[0];
            var lastName = names.Length > 1 ? names[1] : "";
            var initials = model.Name.Length >= 2 ? model.Name.Substring(0, 2).ToUpper() : "U";

            // Determine target school based on role
            int? targetSchoolId;
            var role = model.Role ?? "Student";

            if (role == "Manager" && model.SchoolId.HasValue)
            {
                // SuperAdmin assigning a Manager to a specific school
                targetSchoolId = model.SchoolId.Value;
            }
            else if (User.IsInRole("Manager") && !User.IsInRole("SuperAdmin"))
            {
                targetSchoolId = currentUser?.SchoolId;
            }
            else
            {
                targetSchoolId = GetEffectiveSchoolId();
            }

            // Manager cannot assign SuperAdmin role
            if (User.IsInRole("Manager") && !User.IsInRole("SuperAdmin") && role == "SuperAdmin")
            {
                TempData["ErrorMessage"] = "Managers cannot assign the SuperAdmin role.";
                return RedirectToAction("Users");
            }

            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                EmailConfirmed = true,
                FirstName = firstName,
                LastName = lastName,
                Initials = initials,
                GradeOrPosition = model.GradeOrPosition ?? "",
                AvatarColor = "background: linear-gradient(135deg, #6366f1, #4f46e5)",
                Status = "Active",
                DateCreated = DateTime.Now,
                DepartmentId = null,
                SchoolId = targetSchoolId
            };

            var password = "Temp123!";
            var result = await _userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                await _userManager.AddToRoleAsync(user, role);

                // Send Gmail invitation email
                try
                {
                    if (_emailService.IsConfigured)
                    {
                        var schoolName = "";
                        if (targetSchoolId.HasValue)
                        {
                            var school = await _context.Schools.FindAsync(targetSchoolId.Value);
                            schoolName = school?.Name ?? "";
                        }

                        var htmlBody = $@"
                        <div style='font-family: Inter, Arial, sans-serif; max-width: 600px; margin: 0 auto; background: #ffffff; border-radius: 12px; overflow: hidden; box-shadow: 0 4px 24px rgba(0,0,0,0.08);'>
                            <div style='background: linear-gradient(135deg, #3B7DD8, #2563eb); padding: 32px 24px; text-align: center;'>
                                <h1 style='color: #ffffff; margin: 0; font-size: 28px; font-weight: 800;'>LearnLink</h1>
                                <p style='color: rgba(255,255,255,0.85); margin: 8px 0 0; font-size: 14px;'>Your Learning Resource Hub</p>
                            </div>
                            <div style='padding: 32px 24px;'>
                                <h2 style='color: #1e293b; font-size: 20px; margin: 0 0 16px;'>Welcome, {firstName}! 🎉</h2>
                                <p style='color: #475569; font-size: 15px; line-height: 1.6;'>
                                    Your <strong>{role}</strong> account has been created on LearnLink{(string.IsNullOrEmpty(schoolName) ? "" : $" for <strong>{schoolName}</strong>")}. Use the credentials below to log in:
                                </p>
                                <div style='background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 10px; padding: 20px; margin: 20px 0;'>
                                    <table style='width: 100%; border-collapse: collapse;'>
                                        <tr>
                                            <td style='padding: 8px 0; color: #94a3b8; font-size: 13px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;'>Email</td>
                                            <td style='padding: 8px 0; color: #1e293b; font-size: 15px; font-weight: 600;'>{model.Email}</td>
                                        </tr>
                                        <tr>
                                            <td style='padding: 8px 0; color: #94a3b8; font-size: 13px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px;'>Password</td>
                                            <td style='padding: 8px 0; color: #1e293b; font-size: 15px; font-weight: 600;'>{password}</td>
                                        </tr>
                                    </table>
                                </div>
                                <p style='color: #ef4444; font-size: 13px;'><strong>⚠️ Important:</strong> Please change your password after your first login for security purposes.</p>
                            </div>
                            <div style='background: #f8fafc; padding: 20px 24px; text-align: center; border-top: 1px solid #e2e8f0;'>
                                <p style='color: #94a3b8; font-size: 12px; margin: 0;'>© {DateTime.Now.Year} LearnLink. All rights reserved.</p>
                            </div>
                        </div>";

                        await _emailService.SendAsync(model.Email, "Welcome to LearnLink — Your Account is Ready!", htmlBody);
                        TempData["SuccessMessage"] = $"{model.Name} has been added successfully and an invitation email has been sent!";
                    }
                    else
                    {
                        TempData["SuccessMessage"] = $"{model.Name} has been added successfully! (Temporary password: {password}) — Email not sent: SMTP credentials not configured.";
                    }
                }
                catch (Exception emailEx)
                {
                    TempData["SuccessMessage"] = $"{model.Name} has been added successfully! (Temporary password: {password}) — Email could not be sent: {emailEx.Message}";
                }
            }
            else
            {
                TempData["ErrorMessage"] = string.Join(" ", result.Errors.Select(e => e.Description));
            }

            return RedirectToAction("Users");
        }

        [Authorize(Roles = "SuperAdmin,Manager")]
        public async Task<IActionResult> UserDetails(string email)
        {
            var user = await _userManager.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Email == email);
            if (user == null) return RedirectToAction("Users");

            // School-scoping guard
            if (!IsSameSchool(user)) return RedirectToAction("Users");

            var roles = await _userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? "Student";
            var resourceCount = await _context.Resources.CountAsync(r => r.UserId == user.Id);

            ViewBag.UserDetails = new UserViewModel
            {
                Name = user.FullName,
                Email = user.Email ?? "",
                Initials = user.Initials,
                AvatarColor = user.AvatarColor,
                Role = role,
                RoleBadgeClass = GetRoleBadge(role),
                GradeOrPosition = user.GradeOrPosition,
                Status = user.Status,
                StatusBadgeClass = GetStatusBadge(user.Status),
                JoinedAt = user.DateCreated,
                LastActive = GetTimeAgo(user.DateCreated),
                ResourceCount = resourceCount,
                SuspensionReason = user.SuspensionReason,
                SuspensionDate = user.SuspensionDate
            };

            ViewBag.UserActivity = await _context.UserActivityLogs
                .Include(a => a.User)
                .Where(a => a.UserId == user.Id)
                .OrderByDescending(a => a.ActivityDate)
                .Take(10)
                .Select(a => new ActivityViewModel
                {
                    User = a.User!.FirstName + " " + a.User.LastName,
                    UserInitials = a.User.Initials,
                    UserColor = a.User.AvatarColor,
                    Action = a.ActivityType,
                    Target = a.TargetTitle,
                    TimeAgo = "",
                    IconClass = "bi-activity",
                    IconColor = "text-muted"
                }).ToListAsync();

            ViewBag.UserResources = await _context.Resources
                .Include(r => r.User)
                .Where(r => r.UserId == user.Id)
                .Select(r => MapResource(r))
                .ToListAsync();

            return View();
        }

        [Authorize(Roles = "SuperAdmin,Manager")]
        [HttpPost]
        public async Task<IActionResult> EditUser(string email, string name, string grade)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user != null && IsSameSchool(user))
            {
                var names = name.Split(' ', 2);
                user.FirstName = names[0];
                user.LastName = names.Length > 1 ? names[1] : "";
                user.Initials = name.Length >= 2 ? name.Substring(0, 2).ToUpper() : "U";
                user.GradeOrPosition = grade ?? "";
                await _userManager.UpdateAsync(user);
                TempData["SuccessMessage"] = $"Profile for {user.FullName} has been updated.";
            }
            return RedirectToAction("Users");
        }

        [Authorize(Roles = "SuperAdmin,Manager")]
        [HttpPost]
        public async Task<IActionResult> ChangeRole(string email, string role)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user != null && IsSameSchool(user))
            {
                // Manager cannot promote to SuperAdmin
                if (User.IsInRole("Manager") && !User.IsInRole("SuperAdmin") && role == "SuperAdmin")
                {
                    TempData["ErrorMessage"] = "Managers cannot assign the SuperAdmin role.";
                    return RedirectToAction("Users");
                }
                var currentRoles = await _userManager.GetRolesAsync(user);
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
                await _userManager.AddToRoleAsync(user, role);
                TempData["SuccessMessage"] = $"Role for {user.FullName} changed to {role}.";
            }
            return RedirectToAction("Users");
        }

        [Authorize(Roles = "SuperAdmin,Manager")]
        [HttpPost]
        public async Task<IActionResult> SuspendUser(string email, string reason)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user != null && IsSameSchool(user))
            {
                user.Status = "Suspended";
                user.SuspensionReason = reason;
                user.SuspensionDate = DateTime.Now;
                await _userManager.UpdateAsync(user);
                TempData["SuccessMessage"] = $"{user.FullName} has been suspended.";
            }
            return RedirectToAction("Users");
        }

        [Authorize(Roles = "SuperAdmin,Manager")]
        [HttpPost]
        public async Task<IActionResult> ReactivateUser(string email)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user != null && IsSameSchool(user))
            {
                user.Status = "Active";
                user.SuspensionReason = null;
                user.SuspensionDate = null;
                await _userManager.UpdateAsync(user);
                TempData["SuccessMessage"] = $"{user.FullName} has been reactivated.";
            }
            return RedirectToAction("Users");
        }

        [Authorize(Roles = "SuperAdmin,Manager")]
        [HttpPost]
        public async Task<IActionResult> DeleteUser(string email)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null) return NotFound();

            // Manager can only delete users in their own school
            if (User.IsInRole("Manager") && !IsSameSchool(user))
                return Forbid();

            // Prevent deleting SuperAdmins unless you are a SuperAdmin
            if (await _userManager.IsInRoleAsync(user, "SuperAdmin") && !User.IsInRole("SuperAdmin"))
                return Forbid();

            var result = await _userManager.DeleteAsync(user);
            if (result.Succeeded)
            {
                var currentUser = await GetCurrentUserAsync();
                if (currentUser != null)
                    await LogActivity(currentUser.Id, "Delete User", user.Email ?? "Unknown User", 0);

                TempData["SuccessMessage"] = $"User {user.Email} has been deleted.";
                return RedirectToAction("Users");
            }

            return BadRequest("Failed to delete user.");
        }

        [Authorize(Roles = "SuperAdmin,Manager")]
        [HttpPost]
        public async Task<IActionResult> DeleteMultipleUsers(string[] emails)
        {
            if (emails == null || emails.Length == 0)
                return Json(new { success = false, message = "No users selected." });

            int deletedCount = 0;
            foreach (var email in emails)
            {
                var user = await _userManager.FindByEmailAsync(email);
                if (user != null)
                {
                    // Manager can only delete users in their own school
                    if (User.IsInRole("Manager") && !IsSameSchool(user))
                        continue;
                    // Don't delete SuperAdmins unless caller is SuperAdmin
                    if (await _userManager.IsInRoleAsync(user, "SuperAdmin") && !User.IsInRole("SuperAdmin"))
                        continue;

                    var result = await _userManager.DeleteAsync(user);
                    if (result.Succeeded)
                    {
                        var currentUser = await GetCurrentUserAsync();
                        if (currentUser != null)
                            await LogActivity(currentUser.Id, "Delete User", user.Email ?? "Unknown User", 0);
                        deletedCount++;
                    }
                }
            }

            return Json(new { success = true, message = $"{deletedCount} user(s) deleted successfully." });
        }

        [Authorize(Roles = "SuperAdmin,Manager")]
        public async Task<IActionResult> Settings()
        {
            await LoadSchoolSettingsToViewBag();
            var schoolId = GetEffectiveSchoolId();

            if (schoolId.HasValue)
            {
                var settings = await _context.SchoolSettings
                    .IgnoreQueryFilters()
                    .Include(s => s.School)
                    .FirstOrDefaultAsync(s => s.SchoolId == schoolId.Value);
                ViewBag.SchoolSettingsModel = settings;
            }

            return View();
        }

        [Authorize(Roles = "SuperAdmin,Manager")]
        [HttpPost]
        public async Task<IActionResult> SaveSettings(string institutionName, string adminEmail,
            string subjects, string gradeLevels, string resourceTypes, string quarters)
        {
            var schoolId = GetEffectiveSchoolId();
            if (!schoolId.HasValue)
            {
                TempData["ErrorMessage"] = "No school context selected.";
                return RedirectToAction("Settings");
            }

            var settings = await _context.SchoolSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId.Value);

            if (settings == null)
            {
                settings = new SchoolSettings { SchoolId = schoolId.Value };
                _context.SchoolSettings.Add(settings);
            }

            settings.InstitutionName = institutionName ?? "";
            settings.AdminEmail = adminEmail ?? "";

            // Parse comma-separated values to JSON arrays
            if (!string.IsNullOrWhiteSpace(subjects))
                settings.Subjects = JsonSerializer.Serialize(subjects.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (!string.IsNullOrWhiteSpace(gradeLevels))
                settings.GradeLevels = JsonSerializer.Serialize(gradeLevels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (!string.IsNullOrWhiteSpace(resourceTypes))
                settings.ResourceTypes = JsonSerializer.Serialize(resourceTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (!string.IsNullOrWhiteSpace(quarters))
                settings.Quarters = JsonSerializer.Serialize(quarters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            // Also update the school name
            var school = await _context.Schools.FindAsync(schoolId.Value);
            if (school != null && !string.IsNullOrWhiteSpace(institutionName))
            {
                school.Name = institutionName;
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Settings saved successfully.";
            return RedirectToAction("Settings");
        }

        // ==================== School Management (SuperAdmin only) ====================

        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> Schools()
        {
            var schools = await _context.Schools
                .Include(s => s.Settings)
                .OrderBy(s => s.Name)
                .ToListAsync();

            var schoolViewModels = new List<dynamic>();
            foreach (var school in schools)
            {
                var userCount = await _userManager.Users.CountAsync(u => u.SchoolId == school.SchoolId);
                var resourceCount = await _context.Resources.IgnoreQueryFilters().CountAsync(r => r.SchoolId == school.SchoolId);
                schoolViewModels.Add(new
                {
                    school.SchoolId,
                    school.Name,
                    school.Code,
                    school.Description,
                    school.ContactEmail,
                    school.IsActive,
                    school.AllowCrossSchoolSharing,
                    school.DateCreated,
                    UserCount = userCount,
                    ResourceCount = resourceCount,
                    ManagerName = (await _userManager.Users
                        .Where(u => u.SchoolId == school.SchoolId)
                        .ToListAsync())
                        .Where(u => _userManager.IsInRoleAsync(u, "Manager").Result)
                        .Select(u => u.FullName)
                        .FirstOrDefault() ?? "No manager assigned"
                });
            }

            ViewBag.Schools = schoolViewModels;

            // Load users eligible to be managers (Contributors in each school + existing Managers)
            var allSchoolUsers = await _userManager.Users
                .Where(u => u.SchoolId != null && u.Status == "Active")
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.SchoolId })
                .ToListAsync();

            var managerUsers = new List<object>();
            foreach (var u in allSchoolUsers)
            {
                var appUser = await _userManager.FindByIdAsync(u.Id);
                if (appUser != null)
                {
                    var roles = await _userManager.GetRolesAsync(appUser);
                    if (roles.Contains("Contributor") || roles.Contains("Manager"))
                    {
                        managerUsers.Add(new { u.Id, u.FirstName, u.LastName, u.SchoolId, Role = roles.Contains("Manager") ? "Manager" : "Contributor" });
                    }
                }
            }
            ViewBag.ManagerCandidates = managerUsers;

            // Yesterday comparison for KPI cards
            var yesterday = DateTime.Now.Date;
            ViewBag.YesterdaySchoolCount = schools.Count(s => s.DateCreated < yesterday);
            ViewBag.YesterdayActiveSchools = schools.Count(s => s.DateCreated < yesterday && s.IsActive);
            // User/resource counts as of yesterday are approximated by current counts minus today's additions
            var todayNewUsers = await _userManager.Users.CountAsync(u => u.DateCreated >= yesterday);
            var todayNewResources = await _context.Resources.IgnoreQueryFilters().CountAsync(r => r.DateUploaded >= yesterday);
            ViewBag.YesterdayTotalUsers = schoolViewModels.Sum(s => (int)s.UserCount) - todayNewUsers;
            ViewBag.YesterdayTotalResources = schoolViewModels.Sum(s => (int)s.ResourceCount) - todayNewResources;
            return View();
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost]
        public async Task<IActionResult> CreateSchool(string name, string code, string description, string contactEmail, string address)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
            {
                TempData["ErrorMessage"] = "School name and code are required.";
                return RedirectToAction("Schools");
            }

            code = code.Trim().ToUpper();

            // Check for duplicate code
            if (await _context.Schools.AnyAsync(s => s.Code == code))
            {
                TempData["ErrorMessage"] = $"A school with code '{code}' already exists.";
                return RedirectToAction("Schools");
            }

            var school = new School
            {
                Name = name.Trim(),
                Code = code,
                Description = description?.Trim() ?? "",
                ContactEmail = contactEmail?.Trim() ?? "",
                Address = address?.Trim() ?? "",
                IsActive = true,
                DateCreated = DateTime.Now
            };

            _context.Schools.Add(school);
            await _context.SaveChangesAsync();

            // Create default settings for the new school
            _context.SchoolSettings.Add(new SchoolSettings
            {
                SchoolId = school.SchoolId,
                InstitutionName = name.Trim(),
                AdminEmail = contactEmail?.Trim() ?? ""
            });

            // Create default departments for the new school
            var defaultDepts = new[]
            {
                ("Mathematics", "Mathematics Department"),
                ("English", "English and Literature Department"),
                ("Filipino", "Filipino Language Department"),
                ("Science", "Science Department"),
                ("Araling Panlipunan", "Social Studies Department"),
                ("TLE", "Technology and Livelihood Education"),
                ("MAPEH", "Music, Arts, PE, and Health Department")
            };

            foreach (var (deptName, deptDesc) in defaultDepts)
            {
                _context.Departments.Add(new Department
                {
                    DepartmentName = deptName,
                    Description = deptDesc,
                    SchoolId = school.SchoolId
                });
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"School '{name}' created successfully with code '{code}'.";
            return RedirectToAction("Schools");
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost]
        public async Task<IActionResult> ToggleSchoolStatus(int id)
        {
            var school = await _context.Schools.FindAsync(id);
            if (school != null)
            {
                school.IsActive = !school.IsActive;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"School '{school.Name}' has been {(school.IsActive ? "activated" : "deactivated")}.";
            }
            return RedirectToAction("Schools");
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost]
        public async Task<IActionResult> ToggleCrossSchoolSharing(int id)
        {
            var school = await _context.Schools.FindAsync(id);
            if (school != null)
            {
                school.AllowCrossSchoolSharing = !school.AllowCrossSchoolSharing;
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Cross-school sharing for '{school.Name}' has been {(school.AllowCrossSchoolSharing ? "enabled" : "disabled")}.";
            }
            return RedirectToAction("Schools");
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost]
        public async Task<IActionResult> EditSchool(int id, string name, string code, string description, string contactEmail, string address)
        {
            var school = await _context.Schools.FindAsync(id);
            if (school == null)
            {
                TempData["ErrorMessage"] = "School not found.";
                return RedirectToAction("Schools");
            }

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
            {
                TempData["ErrorMessage"] = "School name and code are required.";
                return RedirectToAction("Schools");
            }

            code = code.Trim().ToUpper();

            // Check for duplicate code (exclude current school)
            if (await _context.Schools.AnyAsync(s => s.Code == code && s.SchoolId != id))
            {
                TempData["ErrorMessage"] = $"A school with code '{code}' already exists.";
                return RedirectToAction("Schools");
            }

            school.Name = name.Trim();
            school.Code = code;
            school.Description = description?.Trim() ?? "";
            school.ContactEmail = contactEmail?.Trim() ?? "";
            school.Address = address?.Trim() ?? "";
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"School '{school.Name}' updated successfully.";
            return RedirectToAction("Schools");
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost]
        public async Task<IActionResult> DeleteSchool(int id)
        {
            var school = await _context.Schools.FindAsync(id);
            if (school == null)
            {
                TempData["ErrorMessage"] = "School not found.";
                return RedirectToAction("Schools");
            }

            // Prevent deleting a school that still has users
            var userCount = await _userManager.Users.CountAsync(u => u.SchoolId == id);
            if (userCount > 0)
            {
                TempData["ErrorMessage"] = $"Cannot delete '{school.Name}' — it still has {userCount} user(s). Reassign or remove them first.";
                return RedirectToAction("Schools");
            }

            _context.Schools.Remove(school);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"School '{school.Name}' has been deleted.";
            return RedirectToAction("Schools");
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost]
        public async Task<IActionResult> AssignManager(int schoolId, string userId)
        {
            var school = await _context.Schools.FindAsync(schoolId);
            if (school == null)
            {
                TempData["ErrorMessage"] = "School not found.";
                return RedirectToAction("Schools");
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null || user.SchoolId != schoolId)
            {
                TempData["ErrorMessage"] = "User not found or does not belong to this school.";
                return RedirectToAction("Schools");
            }

            var currentRoles = await _userManager.GetRolesAsync(user);

            // Remove current role(s) except SuperAdmin
            foreach (var role in currentRoles.Where(r => r != "SuperAdmin"))
            {
                await _userManager.RemoveFromRoleAsync(user, role);
            }

            // Assign Manager role
            await _userManager.AddToRoleAsync(user, "Manager");

            TempData["SuccessMessage"] = $"{user.FullName} has been assigned as Manager of '{school.Name}'.";
            return RedirectToAction("Schools");
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost]
        public async Task<IActionResult> SwitchSchoolContext(int? schoolId, string? returnUrl)
        {
            if (schoolId.HasValue && schoolId.Value > 0)
            {
                var school = await _context.Schools.FindAsync(schoolId.Value);
                if (school != null)
                {
                    HttpContext.Session.SetInt32("SwitchedSchoolId", schoolId.Value);
                    HttpContext.Session.SetString("SwitchedSchoolName", school.Name);
                    TempData["SuccessMessage"] = $"Switched to {school.Name} context.";
                }
            }
            else
            {
                HttpContext.Session.Remove("SwitchedSchoolId");
                HttpContext.Session.Remove("SwitchedSchoolName");
                TempData["SuccessMessage"] = "Switched to All Schools view.";
            }
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Dashboard");
        }

        // ==================== Reports ====================

        private async Task<object> BuildReportData(int? schoolId)
        {
            var allUsers = await _userManager.Users.ToListAsync();
            List<string>? scopedUserIds = null;
            if (schoolId.HasValue && schoolId > 0)
                scopedUserIds = allUsers.Where(u => u.SchoolId == schoolId).Select(u => u.Id).ToList();

            // Resources – IgnoreQueryFilters so we can filter by any school
            var resourcesQuery = _context.Resources.IgnoreQueryFilters()
                .Include(r => r.User)
                .Where(r => r.Status == "Published");
            if (schoolId.HasValue && schoolId > 0)
                resourcesQuery = resourcesQuery.Where(r => r.SchoolId == schoolId);
            var resources = await resourcesQuery.ToListAsync();

            // KPIs
            var totalEngagements = resources.Sum(r => r.ViewCount) + resources.Sum(r => r.DownloadCount);
            var totalDownloads = resources.Sum(r => r.DownloadCount);
            var avgRating = resources.Any() ? Math.Round(resources.Average(r => r.Rating), 1) : 0;
            var newContributions = resources.Count;

            var yesterday = DateTime.Now.Date;
            var yRes = resources.Where(r => r.DateUploaded < yesterday).ToList();
            var yEngagements = yRes.Sum(r => r.ViewCount) + yRes.Sum(r => r.DownloadCount);
            var yDownloads = yRes.Sum(r => r.DownloadCount);
            var yAvgRating = yRes.Any() ? Math.Round(yRes.Average(r => r.Rating), 1) : 0;
            var yContributions = yRes.Count;

            // Engagement Trends (30 days) – broken out by type
            var thirtyDaysAgo = DateTime.Now.AddDays(-30);
            var logsQuery = _context.UserActivityLogs.AsQueryable();
            if (scopedUserIds != null)
                logsQuery = logsQuery.Where(l => scopedUserIds.Contains(l.UserId));
            var logs = await logsQuery.Where(l => l.ActivityDate >= thirtyDaysAgo).ToListAsync();

            var dates = Enumerable.Range(0, 30).Select(i => thirtyDaysAgo.AddDays(i).Date).ToList();

            // Subject Distribution
            var subjectGroups = resources.GroupBy(r => r.Subject)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count).ToList();
            var totalResources = resources.Count;

            // Top Resources (10)
            var topRes = resources.OrderByDescending(r => r.ViewCount + r.DownloadCount).Take(10).ToList();

            // Top Contributors
            var scopedUsers = (schoolId.HasValue && schoolId > 0)
                ? allUsers.Where(u => u.SchoolId == schoolId).ToList()
                : allUsers;
            var contributorList = new List<object>();
            foreach (var u in scopedUsers)
            {
                var roles = await _userManager.GetRolesAsync(u);
                var role = roles.FirstOrDefault() ?? "";
                if (role == "Contributor" || role == "Manager")
                {
                    var resCount = resources.Count(r => r.UserId == u.Id);
                    contributorList.Add(new
                    {
                        name = u.FullName,
                        initials = u.Initials,
                        avatarColor = u.AvatarColor,
                        resourceCount = resCount,
                        role
                    });
                }
            }

            // School Stats (only for Overall view)
            List<object>? schoolStatsResult = null;
            int totalSystemUsers = allUsers.Count;
            if (!schoolId.HasValue || schoolId == 0)
            {
                var allSchools = await _context.Schools.Where(s => s.IsActive).ToListAsync();
                schoolStatsResult = new List<object>();
                foreach (var school in allSchools)
                {
                    var schoolUserCount = allUsers.Count(u => u.SchoolId == school.SchoolId);
                    var sUserIds = allUsers.Where(u => u.SchoolId == school.SchoolId).Select(u => u.Id).ToList();
                    var mostRead = await _context.ReadingHistories
                        .Where(rh => sUserIds.Contains(rh.UserId))
                        .GroupBy(rh => rh.ResourceId)
                        .Select(g => new { ResourceId = g.Key, ReadCount = g.Count() })
                        .OrderByDescending(g => g.ReadCount)
                        .FirstOrDefaultAsync();
                    string mostReadTitle = "No activity yet";
                    int mostReadCount = 0;
                    if (mostRead != null)
                    {
                        var res = await _context.Resources.IgnoreQueryFilters()
                            .FirstOrDefaultAsync(r => r.ResourceId == mostRead.ResourceId);
                        if (res != null) { mostReadTitle = res.Title; mostReadCount = mostRead.ReadCount; }
                    }
                    var schoolResCount = await _context.Resources.IgnoreQueryFilters()
                        .CountAsync(r => r.SchoolId == school.SchoolId && r.Status == "Published");
                    schoolStatsResult.Add(new
                    {
                        schoolName = school.Name,
                        schoolCode = school.Code,
                        userCount = schoolUserCount,
                        resourceCount = schoolResCount,
                        mostReadTitle,
                        mostReadCount
                    });
                }
            }

            return new
            {
                totalEngagements,
                totalDownloads,
                avgRating,
                newContributions,
                yEngagements,
                yDownloads,
                yAvgRating,
                yContributions,
                engagementTrends = new
                {
                    labels = dates.Select(d => d.ToString("MMM dd")).ToArray(),
                    views = dates.Select(d => logs.Count(l => l.ActivityDate.Date == d && l.ActivityType == "View")).ToArray(),
                    downloads = dates.Select(d => logs.Count(l => l.ActivityDate.Date == d && l.ActivityType == "Download")).ToArray(),
                    uploads = dates.Select(d => logs.Count(l => l.ActivityDate.Date == d && l.ActivityType == "Upload")).ToArray(),
                    comments = dates.Select(d => logs.Count(l => l.ActivityDate.Date == d && l.ActivityType == "Comment")).ToArray()
                },
                subjectDistribution = subjectGroups.Select(g => new
                {
                    name = g.Name,
                    count = g.Count,
                    percent = totalResources > 0 ? (int)((double)g.Count / totalResources * 100) : 0,
                    colorClass = GetSubjectColor(g.Name)
                }).ToList(),
                topResources = topRes.Select(r => new
                {
                    title = r.Title,
                    uploader = r.User?.FullName ?? "Unknown",
                    uploaderInitials = r.User?.Initials ?? "?",
                    uploaderColor = r.User?.AvatarColor ?? "",
                    downloads = r.DownloadCount,
                    views = r.ViewCount,
                    rating = Math.Round(r.Rating, 1),
                    ratingCount = r.RatingCount,
                    engagementScore = r.ViewCount + r.DownloadCount
                }).ToList(),
                topContributors = contributorList
                    .Cast<dynamic>()
                    .OrderByDescending(c => (int)c.resourceCount)
                    .Take(10)
                    .Select(c => new { name = (string)c.name, initials = (string)c.initials, avatarColor = (string)c.avatarColor, resourceCount = (int)c.resourceCount, role = (string)c.role })
                    .ToList(),
                schoolStats = schoolStatsResult?.Cast<dynamic>()
                    .OrderByDescending(s => (int)s.userCount)
                    .Select(s => new { schoolName = (string)s.schoolName, schoolCode = (string)s.schoolCode, userCount = (int)s.userCount, resourceCount = (int)s.resourceCount, mostReadTitle = (string)s.mostReadTitle, mostReadCount = (int)s.mostReadCount })
                    .ToList(),
                totalSystemUsers
            };
        }

        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        public async Task<IActionResult> Reports(int? schoolId)
        {
            await LoadSchoolSettingsToViewBag();

            var data = await BuildReportData(schoolId);
            var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            ViewBag.InitialReportData = JsonSerializer.Serialize(data, jsonOpts);
            ViewBag.InitialSchoolId = schoolId;

            var schools = await _context.Schools.Where(s => s.IsActive)
                .OrderBy(s => s.Name)
                .Select(s => new { s.SchoolId, s.Name })
                .ToListAsync();
            ViewBag.SchoolsJson = JsonSerializer.Serialize(schools, jsonOpts);

            return View();
        }

        [HttpGet]
        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        public async Task<IActionResult> ReportData(int? schoolId)
        {
            var data = await BuildReportData(schoolId);
            return Json(data);
        }



        private string GetSubjectColor(string subject) => subject switch
        {
            "Mathematics" => "primary",
            "Science" => "success",
            "English" => "warning",
            "Filipino" => "info",
            "History" => "danger",
            _ => "secondary"
        };

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            var exceptionFeature = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
            var model = new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                ExceptionMessage = exceptionFeature?.Error?.Message,
                ExceptionDetails = exceptionFeature?.Error?.ToString()
            };
            return View(model);
        }
    }
}
