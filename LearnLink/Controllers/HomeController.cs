using System.Diagnostics;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using LearnLink.Data;
using LearnLink.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Resource = LearnLink.Models.Resource;

namespace LearnLink.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _environment;
        private readonly Cloudinary _cloudinary;

        public HomeController(
            ApplicationDbContext context,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment environment,
            Cloudinary cloudinary)
        {
            _context = context;
            _signInManager = signInManager;
            _userManager = userManager;
            _environment = environment;
            _cloudinary = cloudinary;
        }

        // ==================== Helpers ====================

        private async Task<ApplicationUser?> GetCurrentUserAsync()
            => await _userManager.GetUserAsync(User);

        private static string GetIconClass(string format) => format?.ToUpper() switch
        {
            "PDF" => "bi-file-earmark-pdf",
            "DOCX" or "DOC" => "bi-file-earmark-word",
            "PPTX" or "PPT" => "bi-file-earmark-ppt",
            "XLSX" or "XLS" => "bi-file-earmark-excel",
            "MP4" or "AVI" or "MOV" => "bi-play-circle",
            _ => "bi-file-earmark"
        };
        private static string GetIconColor(string format) => format?.ToUpper() switch
        {
            "PDF" => "text-danger",
            "DOCX" or "DOC" => "text-primary",
            "PPTX" or "PPT" => "text-warning",
            "XLSX" or "XLS" => "text-success",
            "MP4" or "AVI" or "MOV" => "text-success",
            _ => "text-muted"
        };
        private static string GetIconBg(string format) => format?.ToUpper() switch
        {
            "PDF" => "#fee2e2",
            "DOCX" or "DOC" => "#dbeafe",
            "PPTX" or "PPT" => "#fef3c7",
            "XLSX" or "XLS" => "#dcfce7",
            "MP4" or "AVI" or "MOV" => "#dcfce7",
            _ => "#e2e8f0"
        };

        private static bool IsAbsoluteHttpUrl(string value)
            => Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        private string BuildCloudFileUrl(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return string.Empty;

            var normalized = filePath.Trim();

            if (IsAbsoluteHttpUrl(normalized))
                return normalized;

            return _cloudinary.Api.Url
                .ResourceType("raw")
                .Type("upload")
                .Secure(true)
                .Signed(true)
                .BuildUrl(normalized.TrimStart('/'));
        }

        private List<string> BuildCloudFileUrlCandidates(string? filePath)
        {
            var candidates = new List<string>();
            if (string.IsNullOrWhiteSpace(filePath))
                return candidates;

            var normalized = filePath.Trim();
            if (IsAbsoluteHttpUrl(normalized))
            {
                candidates.Add(normalized);
                return candidates;
            }

            var publicId = normalized.TrimStart('/');

            candidates.Add(_cloudinary.Api.Url
                .ResourceType("raw")
                .Type("upload")
                .Secure(true)
                .BuildUrl(publicId));

            candidates.Add(_cloudinary.Api.Url
                .ResourceType("raw")
                .Type("upload")
                .Secure(true)
                .Signed(true)
                .BuildUrl(publicId));

            candidates.Add(_cloudinary.Api.Url
                .ResourceType("raw")
                .Type("authenticated")
                .Secure(true)
                .Signed(true)
                .BuildUrl(publicId));

            return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private async Task<(byte[]? Content, string? ResolvedUrl)> TryFetchCloudFile(string? filePath)
        {
            var urls = BuildCloudFileUrlCandidates(filePath);
            if (!urls.Any())
                return (null, null);

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LearnLink/1.0");

            foreach (var url in urls)
            {
                try
                {
                    using var response = await httpClient.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Debug.WriteLine($"Cloud fetch failed: {(int)response.StatusCode} at {url}");
                        continue;
                    }

                    var fileBytes = await response.Content.ReadAsByteArrayAsync();
                    if (fileBytes.Length == 0)
                        continue;

                    return (fileBytes, url);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Cloud fetch error at {url}: {ex.Message}");
                }
            }

            // Fallback: resolve the resource via Cloudinary Admin API, then fetch its secure URL.
            var fallbackUrl = await ResolveCloudFileUrlViaApi(filePath);
            if (!string.IsNullOrWhiteSpace(fallbackUrl))
            {
                try
                {
                    using var response = await httpClient.GetAsync(fallbackUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var bytes = await response.Content.ReadAsByteArrayAsync();
                        if (bytes.Length > 0)
                            return (bytes, fallbackUrl);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Cloud fallback fetch error at {fallbackUrl}: {ex.Message}");
                }
            }

            return (null, null);
        }

        private async Task<string?> ResolveCloudFileUrlViaApi(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            var normalized = filePath.Trim();
            if (IsAbsoluteHttpUrl(normalized))
                return normalized;

            var publicId = normalized.TrimStart('/');

            foreach (var deliveryType in new[] { "upload", "authenticated" })
            {
                try
                {
                    var resourceResult = await _cloudinary.GetResourceAsync(new GetResourceParams(publicId)
                    {
                        ResourceType = CloudinaryDotNet.Actions.ResourceType.Raw,
                        Type = deliveryType
                    });

                    var secureUrl = resourceResult?.SecureUrl?.ToString();
                    if (!string.IsNullOrWhiteSpace(secureUrl))
                        return secureUrl;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Cloud API resolve failed for {publicId} ({deliveryType}): {ex.Message}");
                }
            }

            return null;
        }

        private ResourceViewModel MapResource(Resource r)
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
                AuthorInitials = l.User?.Initials ?? "?",
                AuthorColor = l.User?.AvatarColor ?? "",
                AuthorRole = "Contributor",
                CreatedAt = l.DateSubmitted,
                Tags = string.IsNullOrEmpty(l.Tags) ? new List<string>() : l.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).ToList(),
                LikeCount = l.LikeCount,
                CommentCount = l.CommentCount,
                IsLiked = isLiked,
                ResourceId = l.ResourceId,
                ResourceTitle = l.Resource?.Title ?? "Unknown Resource"
            };
        }

        private DiscussionViewModel MapDiscussion(Discussion d, bool isLiked = false)
        {
            return new DiscussionViewModel
            {
                Id = d.DiscussionId,
                Title = d.Title,
                Content = d.Content,
                Author = d.User?.FullName ?? "Unknown",
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
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string email, string password, bool rememberMe)
        {
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

            var result = await _signInManager.PasswordSignInAsync(user, password, rememberMe, false);
            if (result.Succeeded)
            {
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

        public IActionResult Register() => View();

        [HttpPost]
        public async Task<IActionResult> Register(string firstName, string lastName, string email, string password, string confirmPassword)
        {
            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName) ||
                string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "All fields are required.";
                return View();
            }

            if (password != confirmPassword)
            {
                ViewBag.Error = "Passwords do not match.";
                return View();
            }

            var initials = (firstName.Substring(0, 1) + lastName.Substring(0, 1)).ToUpper();

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                Initials = initials,
                AvatarColor = "background: linear-gradient(135deg, #6366f1, #4f46e5)", // Default premium color
                Status = "Active",
                DateCreated = DateTime.Now
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
                JoinedAt = currentUser.DateCreated
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

        // ==================== Main Navigation — Authenticated ====================

        [Authorize]
        public async Task<IActionResult> Dashboard()
        {
            if (User.IsInRole("Contributor") || User.IsInRole("Student"))
                return RedirectToAction("Repository");

            var resources = await _context.Resources.Include(r => r.User).ToListAsync();
            var users = await _userManager.Users.ToListAsync();

            ViewBag.Stats = new DashboardStatsViewModel
            {
                TotalResources = resources.Count,
                ActiveUsers = users.Count(u => u.Status == "Active"),
                TotalDownloads = resources.Sum(r => r.DownloadCount),
                ActiveDiscussions = await _context.Discussions.CountAsync()
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

        private static string GetTimeAgo(DateTime dt)
        {
            var diff = DateTime.Now - dt;
            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} minutes ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} hours ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} days ago";
            return dt.ToString("MMM dd, yyyy");
        }

        [Authorize]
        public async Task<IActionResult> Repository()
        {
            var query = _context.Resources.Include(r => r.User).AsQueryable();

            query = query.Where(r => r.Status == "Published");

            var resources = await query.OrderByDescending(r => r.DateUploaded).ToListAsync();
            ViewBag.Resources = resources.Select(MapResource).ToList();
            return View();
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

            string cloudPublicId = string.Empty;
            string extension = string.Empty;

            if (file != null && file.Length > 0)
            {
                extension = Path.GetExtension(file.FileName);
                var uploadParams = new RawUploadParams
                {
                    File = new FileDescription(file.FileName, file.OpenReadStream()),
                    Folder = "learnlink/resources"
                };

                var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                if (uploadResult.StatusCode != System.Net.HttpStatusCode.OK && uploadResult.StatusCode != System.Net.HttpStatusCode.Created)
                {
                    TempData["ErrorMessage"] = "Upload failed. Please try again.";
                    return RedirectToAction("Upload");
                }

                cloudPublicId = uploadResult.PublicId;
            }

            var resource = new Resource
            {
                Title = title,
                Description = description ?? string.Empty,
                Subject = subject ?? string.Empty,
                GradeLevel = gradeLevel ?? string.Empty,
                ResourceType = resourceType ?? string.Empty,
                Quarter = quarter ?? string.Empty,
                FilePath = string.IsNullOrWhiteSpace(cloudPublicId) ? "" : cloudPublicId,
                FileFormat = string.IsNullOrWhiteSpace(extension) ? "" : extension.TrimStart('.').ToUpperInvariant(),
                FileSize = file != null ? $"{file.Length / 1024d / 1024d:0.0} MB" : "",
                Status = isDraft ? "Draft" : "Pending",
                UserId = currentUser.Id,
                DateUploaded = DateTime.Now
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

            // Increment view count
            resource.ViewCount++;
            
            var currentUser = await GetCurrentUserAsync();
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
                } else {
                    _context.ReadingHistories.Add(new ReadingHistory {
                        UserId = currentUser.Id,
                        ResourceId = resource.ResourceId,
                        LastAccessed = DateTime.Now,
                        ProgressStatus = "In Progress"
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
            }

            // Map LikeCount
            vm.LikeCount = await _context.Likes.CountAsync(l => l.TargetType == "Resource" && l.TargetId == resource.ResourceId);

            ViewBag.Resource = vm;

            // Cloudinary-only preview/download
            string cloudUrl = string.Empty;
            bool canPreview = false;

            if (!string.IsNullOrEmpty(resource.FilePath))
            {
                // Normalize file format for comparison
                var fmt = resource.FileFormat?.TrimStart('.').Trim().ToUpperInvariant() ?? "";
                
                // Always use the direct Cloudinary URL.
                cloudUrl = BuildCloudFileUrl(resource.FilePath);

                canPreview = true;
            }

            ViewBag.CloudUrl = cloudUrl;
            ViewBag.FileUrl = cloudUrl;
            ViewBag.CanPreview = canPreview;

            var relatedResources = await _context.Resources
                .Include(r => r.User)
                .Where(r => r.ResourceId != id && r.Subject == resource.Subject && r.Status == "Published")
                .Take(4)
                .ToListAsync();

            ViewBag.RelatedResources = relatedResources.Select(MapResource).ToList();
            return View();
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
            
            bool isLiked;
            if (existingLike != null)
            {
                _context.Likes.Remove(existingLike);
                isLiked = false;
            }
            else
            {
                _context.Likes.Add(new Like { UserId = currentUser.Id, TargetType = "Resource", TargetId = id, CreatedAt = DateTime.Now });
                isLiked = true;
                await LogActivity(currentUser.Id, "Like", resource.Title, resource.ResourceId);
            }

            await _context.SaveChangesAsync();
            var count = await _context.Likes.CountAsync(l => l.TargetType == "Resource" && l.TargetId == id);
            return Json(new { success = true, isLiked, count });
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> RateResource([FromForm] int id, [FromForm] int rating)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Unauthorized();

            var resource = await _context.Resources.FindAsync(id);
            if (resource == null) return NotFound();

            if (rating < 1 || rating > 5) return BadRequest();
            
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

            var cloudUrl = BuildCloudFileUrl(resource.FilePath);
            if (string.IsNullOrEmpty(cloudUrl))
            {
                return NotFound();
            }

            return Redirect(cloudUrl);
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
                "MP4" => "video/mp4",
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

            if (string.IsNullOrEmpty(resource.FilePath))
            {
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
                await _context.SaveChangesAsync();
            }

            // Use proxy fetch to bypass Cloudinary 401 errors directly in browser
            var (content, resolvedUrl) = await TryFetchCloudFile(resource.FilePath);
            if (content == null)
            {
                TempData["ErrorMessage"] = "Error downloading file: unable to access the file from cloud storage.";
                return RedirectToAction("ResourceDetail", new { id });
            }
            
            string contentType = GetContentType(resource.FileFormat);
            string downloadName = $"{resource.Title}.{resource.FileFormat?.ToLower() ?? "bin"}";

            if (inline) 
            {
                Response.Headers.Add("Content-Disposition", new System.Net.Mime.ContentDisposition {
                    FileName = downloadName,
                    Inline = true
                }.ToString());
                return File(content, contentType);
            }

            return File(content, "application/octet-stream", downloadName);
        }

        [AllowAnonymous]
        public async Task<IActionResult> Search(string? q = null)
        {
            if (User.Identity?.IsAuthenticated ?? false)
                return RedirectToAction("Repository");

            var resources = await _context.Resources.Include(r => r.User)
                .Where(r => r.Status == "Published")
                .OrderByDescending(r => r.DateUploaded)
                .ToListAsync();

            var mapped = resources.Select(MapResource).ToList();
            ViewBag.Resources = mapped;
            ViewBag.AllResources = mapped;
            ViewBag.SearchQuery = q;
            return View("PublicSearch");
        }

        // ==================== Lessons Learned ====================

        [Authorize]
        public async Task<IActionResult> LessonsLearned()
        {
            var currentUser = await GetCurrentUserAsync();
            var lessons = await _context.LessonsLearned
                .Include(l => l.User)
                .Include(l => l.Resource)
                .OrderByDescending(l => l.DateSubmitted)
                .ToListAsync();

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

            var lesson = new LessonLearned
            {
                Title = model.Title ?? "",
                Content = model.Content ?? "",
                Category = model.Category ?? "",
                Tags = model.Tags != null ? string.Join(", ", model.Tags) : "",
                UserId = currentUser.Id,
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

            bool isLiked;
            if (existing != null)
            {
                _context.Likes.Remove(existing);
                lesson.LikeCount = Math.Max(0, lesson.LikeCount - 1);
                isLiked = false;
            }
            else
            {
                _context.Likes.Add(new Like { UserId = currentUser.Id, TargetType = "Lesson", TargetId = id });
                lesson.LikeCount++;
                isLiked = true;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, likes = lesson.LikeCount, isLiked });
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

            return View();
        }

        [Authorize]
        public async Task<IActionResult> BestPractices()
        {
            var resources = await _context.Resources.Include(r => r.User)
                .Where(r => r.Status == "Published")
                .ToListAsync();

            ViewBag.Recommendations = resources.OrderByDescending(r => r.Rating).Take(4).Select(MapResource).ToList();
            ViewBag.Trending = resources.OrderByDescending(r => r.ViewCount).Take(5).Select(MapResource).ToList();

            var currentUser = await GetCurrentUserAsync();
            if (currentUser != null)
            {
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

                // Calculate Learning Progress for current user
                var userHistoryAll = await _context.ReadingHistories.Include(h => h.Resource).Where(h => h.UserId == currentUser.Id).ToListAsync();
                var allResources = await _context.Resources.Where(r => r.Status == "Published").ToListAsync();
                
                 var progress = allResources.GroupBy(r => r.Subject)
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
                ViewBag.TotalProgress = allResources.Any() ? (int)((double)userHistoryAll.Count(h => h.Resource != null) / allResources.Count * 100) : 0;
            }
            else
            {
                ViewBag.ContinueReading = new List<ReadingHistoryViewModel>();
                ViewBag.LearningProgress = new List<object>();
                ViewBag.TotalProgress = 0;
            }

            return View();
        }

        // ==================== Knowledge Portal & Discussions ====================

        [Authorize]
        public async Task<IActionResult> KnowledgePortal()
        {
            var currentUser = await GetCurrentUserAsync();
            var discussions = await _context.Discussions
                .Include(d => d.User)
                .Include(d => d.Posts)
                .OrderByDescending(d => d.DateCreated)
                .ToListAsync();

            var likedIds = new HashSet<int>();
            if (currentUser != null)
            {
                likedIds = (await _context.Likes
                    .Where(l => l.UserId == currentUser.Id && l.TargetType == "Discussion")
                    .Select(l => l.TargetId)
                    .ToListAsync()).ToHashSet();
            }

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

            ViewBag.Discussions = discussions.Select(d => MapDiscussion(d, likedIds.Contains(d.DiscussionId))).ToList();
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

            bool isLiked;
            if (existing != null)
            {
                _context.Likes.Remove(existing);
                discussion.LikeCount = Math.Max(0, discussion.LikeCount - 1);
                isLiked = false;
            }
            else
            {
                _context.Likes.Add(new Like { UserId = currentUser.Id, TargetType = "Discussion", TargetId = id });
                discussion.LikeCount++;
                isLiked = true;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, likes = discussion.LikeCount, isLiked });
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

            bool isLiked;
            if (existing != null)
            {
                _context.Likes.Remove(existing);
                reply.LikeCount = Math.Max(0, reply.LikeCount - 1);
                isLiked = false;
            }
            else
            {
                _context.Likes.Add(new Like { UserId = currentUser.Id, TargetType = "Reply", TargetId = id });
                reply.LikeCount++;
                isLiked = true;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, likes = reply.LikeCount, isLiked });
        }

        [Authorize]
        public async Task<IActionResult> Discussions(int id)
        {
            var currentUser = await GetCurrentUserAsync();
            var discussions = await _context.Discussions
                .Include(d => d.User)
                .Include(d => d.Posts)
                .ToListAsync();

            var discussion = discussions.FirstOrDefault(d => d.DiscussionId == id)
                ?? discussions.FirstOrDefault();

            if (discussion == null) return RedirectToAction("KnowledgePortal");

            var posts = await _context.DiscussionPosts
                .Include(p => p.User)
                .Where(p => p.DiscussionId == discussion.DiscussionId)
                .OrderByDescending(p => p.DatePosted)
                .ToListAsync();

            var likedReplyIds = new HashSet<int>();
            if (currentUser != null)
            {
                likedReplyIds = (await _context.Likes
                    .Where(l => l.UserId == currentUser.Id && l.TargetType == "Reply")
                    .Select(l => l.TargetId)
                    .ToListAsync()).ToHashSet();
            }

            ViewBag.Discussion = MapDiscussion(discussion);
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
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            Resource? resource = null;
            if (id.HasValue)
            {
                resource = await _context.Resources.FirstOrDefaultAsync(r => r.ResourceId == id.Value);
                if (resource == null || (!User.IsInRole("SuperAdmin") && resource.UserId != currentUser.Id))
                {
                    return NotFound();
                }
            }

            var recentUploads = await _context.Resources
                .Include(r => r.User)
                .Where(r => r.UserId == currentUser.Id)
                .OrderByDescending(r => r.DateUploaded)
                .Take(3)
                .ToListAsync();

            ViewBag.RecentUploads = recentUploads.Select(MapResource).ToList();
            return View(resource);
        }

        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        [HttpPost]
        public async Task<IActionResult> Upload(int? resourceId, string title, string description, string subject,
            string gradeLevel, string resourceType, string quarter, IFormFile? file, bool isDraft = false)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            Resource? resource;
            bool isNew = false;

            if (resourceId.HasValue && resourceId.Value > 0)
            {
                resource = await _context.Resources.FindAsync(resourceId.Value);
                if (resource == null || (!User.IsInRole("SuperAdmin") && resource.UserId != currentUser.Id))
                {
                    return NotFound();
                }
            }
            else
            {
                resource = new Resource
                {
                    UserId = currentUser.Id,
                    DateUploaded = DateTime.Now
                };
                _context.Resources.Add(resource);
                isNew = true;
            }

            resource.Title = title ?? "";
            resource.Description = description ?? "";
            resource.Subject = subject ?? "";
            resource.GradeLevel = gradeLevel ?? "";
            resource.ResourceType = resourceType ?? "";
            resource.Quarter = quarter ?? "";
            resource.Status = isDraft ? "Draft" : "Pending";

            // Handle file upload — upload to Cloudinary cloud storage
            if (file != null && file.Length > 0)
            {
                var ext = Path.GetExtension(file.FileName).TrimStart('.').ToLower();
                
                // Use ONLY a short GUID as PublicId to guarantee it fits within the
                // 100-character database column limit. The folder "ll" + 32-char GUID = ~35 chars total.
                var shortId = Guid.NewGuid().ToString("N");  // 32 hex chars, no dashes
                
                using var stream = file.OpenReadStream();
                var uploadParams = new RawUploadParams
                {
                    File = new FileDescription(file.FileName, stream),
                    Folder = "ll",
                    PublicId = shortId,
                    Overwrite = false
                };

                var uploadResult = await _cloudinary.UploadAsync(uploadParams);

                if (uploadResult.Error != null)
                {
                    TempData["ErrorMessage"] = $"File upload failed: {uploadResult.Error.Message}";
                    return RedirectToAction("Upload");
                }

                // PublicId for raw uploads includes the extension, e.g. "ll/abc123.pdf"
                // This is always well under 100 chars
                resource.FilePath = uploadResult.PublicId;
                resource.FileFormat = ext.ToUpper();
                resource.FileSize = file.Length < 1024 * 1024
                    ? $"{file.Length / 1024.0:F1} KB"
                    : $"{file.Length / (1024.0 * 1024.0):F1} MB";
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Failed to save resource: {ex.InnerException?.Message ?? ex.Message}";
                return RedirectToAction("Upload");
            }

            await LogActivity(currentUser.Id, isNew ? "Upload" : "Edit", resource.Title, resource.ResourceId);

            if (isDraft)
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

        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        public async Task<IActionResult> MyUploads()
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            List<Resource> userResources;
            if (User.IsInRole("SuperAdmin"))
            {
                userResources = await _context.Resources.Include(r => r.User).ToListAsync();
            }
            else
            {
                userResources = await _context.Resources
                    .Include(r => r.User)
                    .Where(r => r.UserId == currentUser.Id)
                    .ToListAsync();
            }

            var uploads = userResources.Select(MapResource).ToList();
            ViewBag.Uploads = uploads;
            ViewBag.TotalUploads = uploads.Count;
            ViewBag.PublishedCount = uploads.Count(r => r.Status == "Published");
            ViewBag.PendingCount = uploads.Count(r => r.Status == "Pending");
            ViewBag.DraftCount = uploads.Count(r => r.Status == "Draft");
            ViewBag.RejectedCount = uploads.Count(r => r.Status == "Rejected");
            return View();
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

                // Security check: Only SuperAdmin can delete anyone's resources. 
                // Others can only delete their own.
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
                        if (!IsAbsoluteHttpUrl(filePath))
                        {
                            await _cloudinary.DestroyAsync(new DeletionParams(filePath)
                            {
                                ResourceType = CloudinaryDotNet.Actions.ResourceType.Raw,
                                Type = "upload",
                                Invalidate = true
                            });
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
            ViewBag.Policies = await _context.Policies
                .Include(p => p.User)
                .Include(p => p.Procedures)
                .OrderByDescending(p => p.DateCreated)
                .ToListAsync();
            return View();
        }

        // ==================== Administration — SuperAdmin ====================

        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> Users()
        {
            var users = await _userManager.Users.Include(u => u.Department).ToListAsync();
            var userViewModels = new List<UserViewModel>();

            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                var role = roles.FirstOrDefault() ?? "Student";
                var resourceCount = await _context.Resources.CountAsync(r => r.UserId == u.Id);

                userViewModels.Add(new UserViewModel
                {
                    Name = u.FullName,
                    Email = u.Email ?? "",
                    Initials = u.Initials,
                    AvatarColor = u.AvatarColor,
                    Role = role,
                    RoleBadgeClass = GetRoleBadge(role),
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

            ViewBag.UserList = userViewModels;
            ViewBag.TotalUsers = userViewModels.Count;
            ViewBag.ActiveCount = userViewModels.Count(u => u.Status == "Active");
            ViewBag.ContributorCount = userViewModels.Count(u => u.Role == "Contributor");
            ViewBag.ManagerCount = userViewModels.Count(u => u.Role == "Manager");
            return View();
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost]
        public async Task<IActionResult> PostAddUser(UserViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Name))
            {
                TempData["ErrorMessage"] = "Name and email are required.";
                return RedirectToAction("Users");
            }

            var names = model.Name.Split(' ', 2);
            var firstName = names[0];
            var lastName = names.Length > 1 ? names[1] : "";
            var initials = model.Name.Length >= 2 ? model.Name.Substring(0, 2).ToUpper() : "U";

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
                DepartmentId = null
            };

            var password = "Temp123!";
            var result = await _userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                var role = model.Role ?? "Student";
                await _userManager.AddToRoleAsync(user, role);
                TempData["SuccessMessage"] = $"{model.Name} has been added successfully! (Temporary password: {password})";
            }
            else
            {
                TempData["ErrorMessage"] = string.Join(" ", result.Errors.Select(e => e.Description));
            }

            return RedirectToAction("Users");
        }

        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> UserDetails(string email)
        {
            var user = await _userManager.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Email == email);
            if (user == null) return RedirectToAction("Users");

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

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost]
        public async Task<IActionResult> EditUser(string email, string name, string grade)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user != null)
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

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost]
        public async Task<IActionResult> ChangeRole(string email, string role)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user != null)
            {
                var currentRoles = await _userManager.GetRolesAsync(user);
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
                await _userManager.AddToRoleAsync(user, role);
                TempData["SuccessMessage"] = $"Role for {user.FullName} changed to {role}.";
            }
            return RedirectToAction("Users");
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost]
        public async Task<IActionResult> SuspendUser(string email, string reason)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user != null)
            {
                user.Status = "Suspended";
                user.SuspensionReason = reason;
                user.SuspensionDate = DateTime.Now;
                await _userManager.UpdateAsync(user);
                TempData["SuccessMessage"] = $"{user.FullName} has been suspended.";
            }
            return RedirectToAction("Users");
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost]
        public async Task<IActionResult> ReactivateUser(string email)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user != null)
            {
                user.Status = "Active";
                user.SuspensionReason = null;
                user.SuspensionDate = null;
                await _userManager.UpdateAsync(user);
                TempData["SuccessMessage"] = $"{user.FullName} has been reactivated.";
            }
            return RedirectToAction("Users");
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost]
        public async Task<IActionResult> DeleteUser(string email)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null) return NotFound();

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

        [Authorize(Roles = "SuperAdmin")]
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

        [Authorize(Roles = "SuperAdmin")]
        public IActionResult Settings() => View();

        // ==================== Reports ====================

        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        public async Task<IActionResult> Reports()
        {
            var resources = await _context.Resources.Include(r => r.User)
                .Where(r => r.Status == "Published")
                .ToListAsync();

            ViewBag.TotalEngagements = resources.Sum(r => r.ViewCount) + resources.Sum(r => r.DownloadCount);
            ViewBag.TotalDownloads = resources.Sum(r => r.DownloadCount);
            ViewBag.AvgRating = resources.Any() ? resources.Average(r => r.Rating) : 0;
            ViewBag.NewContributions = resources.Count;
            ViewBag.TopResources = resources.OrderByDescending(r => r.ViewCount).Take(5).Select(MapResource).ToList();

            // Top contributors
            var allUsers = await _userManager.Users.ToListAsync();
            var contributors = new List<UserViewModel>();
            foreach (var u in allUsers)
            {
                var roles = await _userManager.GetRolesAsync(u);
                var role = roles.FirstOrDefault() ?? "";
                if (role == "Contributor" || role == "Manager")
                {
                    contributors.Add(new UserViewModel
                    {
                        Name = u.FullName,
                        Email = u.Email ?? "",
                        Initials = u.Initials,
                        AvatarColor = u.AvatarColor,
                        Role = role,
                        RoleBadgeClass = GetRoleBadge(role),
                        GradeOrPosition = u.GradeOrPosition,
                        Status = u.Status,
                        ResourceCount = await _context.Resources.CountAsync(r => r.UserId == u.Id)
                    });
                }
            }
            ViewBag.TopContributors = contributors.Take(4).ToList();

            // Subject Distribution
            var subjectGroups = resources.GroupBy(r => r.Subject)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToList();
            
            var totalResources = resources.Count;
            ViewBag.SubjectDistribution = subjectGroups.Select(g => new 
            {
                Name = g.Name,
                Percent = totalResources > 0 ? (int)((double)g.Count / totalResources * 100) : 0,
                ColorClass = GetSubjectColor(g.Name)
            }).ToList();

            // Engagement Trends (Last 30 Days)
            var thirtyDaysAgo = DateTime.Now.AddDays(-30);
            var logs = await _context.UserActivityLogs
                .Where(l => l.ActivityDate >= thirtyDaysAgo && (l.ActivityType == "View" || l.ActivityType == "Download"))
                .ToListAsync();

            var trends = Enumerable.Range(0, 30)
                .Select(i => thirtyDaysAgo.AddDays(i))
                .Select(date => logs.Count(l => l.ActivityDate.Date == date.Date))
                .ToArray();
            
            ViewBag.EngagementTrendCounts = trends;

            return View();
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

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
