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
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
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
        private readonly bool _googleAuthEnabled;

        public HomeController(
            ApplicationDbContext context,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment environment,
            ISchoolContext schoolContext,
            GoogleAuthFlag googleAuth,
            IRecommendationService recommendationService,
            IEmailService emailService)
        {
            _context = context;
            _signInManager = signInManager;
            _userManager = userManager;
            _environment = environment;
            _schoolContext = schoolContext;
            _googleAuthEnabled = googleAuth.IsEnabled;
            _recommendationService = recommendationService;
            _emailService = emailService;
        }

        // ==================== Helpers ====================

        private async Task<ApplicationUser?> GetCurrentUserAsync()
            => await _userManager.GetUserAsync(User);

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

            ViewBag.SchoolSubjects = settings != null
                ? JsonSerializer.Deserialize<List<string>>(settings.Subjects) ?? new List<string>()
                : new List<string> { "Mathematics", "Science", "English", "Filipino", "Araling Panlipunan", "MAPEH", "TLE", "Values Education" };

            ViewBag.SchoolGradeLevels = settings != null
                ? JsonSerializer.Deserialize<List<string>>(settings.GradeLevels) ?? new List<string>()
                : new List<string> { "Grade 7", "Grade 8", "Grade 9", "Grade 10" };

            ViewBag.SchoolResourceTypes = settings != null
                ? JsonSerializer.Deserialize<List<string>>(settings.ResourceTypes) ?? new List<string>()
                : new List<string> { "Reviewer/Study Guide", "Lesson Plan", "Activity Sheet", "Assessment/Quiz", "Presentation", "Video Tutorial", "Reading Material", "Reference Document" };

            ViewBag.SchoolQuarters = settings != null
                ? JsonSerializer.Deserialize<List<string>>(settings.Quarters) ?? new List<string>()
                : new List<string> { "1st Quarter", "2nd Quarter", "3rd Quarter", "4th Quarter", "All Quarters" };

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
            _ => "bi-file-earmark"
        };
        private static string GetIconColor(string format) => format?.ToUpper() switch
        {
            "PDF" => "text-danger",
            "DOCX" or "DOC" => "text-primary",
            "PPTX" or "PPT" => "text-warning",
            "XLSX" or "XLS" => "text-success",
            _ => "text-muted"
        };
        private static string GetIconBg(string format) => format?.ToUpper() switch
        {
            "PDF" => "#fee2e2",
            "DOCX" or "DOC" => "#dbeafe",
            "PPTX" or "PPT" => "#fef3c7",
            "XLSX" or "XLS" => "#dcfce7",
            _ => "#e2e8f0"
        };



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
            if (User.IsInRole("Student"))
                return RedirectToAction("StudentDashboard");
            if (User.IsInRole("Contributor"))
                return RedirectToAction("Repository");

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

            var matchingTagResourceIds = _context.ResourceTags
                .Where(rt => rt.Tag != null && rt.Tag.TagName.ToLower() == normalizedTagLower)
                .Select(rt => rt.ResourceId);

            var resources = await BuildAccessiblePublishedResourceQuery(currentUser)
                .Where(r => r.Subject.ToLower() == normalizedTagLower || matchingTagResourceIds.Contains(r.ResourceId))
                .OrderByDescending(r => r.DateUploaded)
                .ToListAsync();

            var mappedResources = resources.Select(MapResource).ToList();
            await PopulateResourceMetadataAsync(mappedResources);

            ViewBag.Tag = normalizedTag;
            ViewBag.ResultCount = mappedResources.Count;
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

            string uniqueFileName = string.Empty;
            string extension = string.Empty;

            if (file != null && file.Length > 0)
            {
                extension = Path.GetExtension(file.FileName);
                uniqueFileName = Guid.NewGuid().ToString("N") + extension;

                string uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(fileStream);
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
                FilePath = string.IsNullOrWhiteSpace(uniqueFileName) ? "" : uniqueFileName,
                FileFormat = string.IsNullOrWhiteSpace(extension) ? "" : extension.TrimStart('.').ToUpperInvariant(),
                FileSize = file != null ? $"{file.Length / 1024d / 1024d:0.0} MB" : "",
                Status = isDraft ? "Draft" : "Pending",
                UserId = currentUser.Id,
                SchoolId = currentUser.SchoolId,
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

            // Local file preview/download
            string localUrl = string.Empty;
            bool canPreview = false;

            if (!string.IsNullOrEmpty(resource.FilePath))
            {
                // Normalize file format for comparison
                var fmt = resource.FileFormat?.TrimStart('.').Trim().ToUpperInvariant() ?? "";
                
                localUrl = $"/uploads/{resource.FilePath}";
                canPreview = true;
            }

            ViewBag.CloudUrl = localUrl;
            ViewBag.FileUrl = localUrl;
            ViewBag.CanPreview = canPreview;

            // Pass resource owner's policy flags to the view
            ViewBag.AllowDownloads = resource.AllowDownloads;
            ViewBag.AllowComments = resource.AllowComments;
            ViewBag.AllowRatings = resource.AllowRatings;

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
                ViewBag.ResourceVersions = await _context.ResourceVersions
                    .Where(v => v.ResourceId == resource.ResourceId)
                    .OrderByDescending(v => v.DateUpdated)
                    .ToListAsync();
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
                _ => "Document"
            };

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
                detailUrl = Url.Action("ResourceDetail", "Home", new { id = vm.Id })
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

            if (!resource.AllowComments && !resource.AllowRatings)
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

            var localUrl = $"/uploads/{resource.FilePath}";
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

            // Fetch local file
            var filepath = Path.Combine(_environment.WebRootPath, "uploads", resource.FilePath);
            if (!System.IO.File.Exists(filepath))
            {
                if (inline) return NotFound();
                TempData["ErrorMessage"] = "Error downloading file: unable to access the file from local storage.";
                return RedirectToAction("ResourceDetail", new { id });
            }
            
            var content = await System.IO.File.ReadAllBytesAsync(filepath);
            
            string contentType = GetContentType(resource.FileFormat);
            string downloadName = $"{resource.Title}.{resource.FileFormat?.ToLower() ?? "bin"}";

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
            if (resource == null || resource.Status != "Published" || resource.AccessLevel != "Public")
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

            var filepath = Path.Combine(_environment.WebRootPath, "uploads", resource.FilePath);
            if (!System.IO.File.Exists(filepath))
                return NotFound();

            resource.DownloadCount++;
            await _context.SaveChangesAsync();

            var content = await System.IO.File.ReadAllBytesAsync(filepath);
            string contentType = GetContentType(resource.FileFormat);
            string downloadName = $"{resource.Title}.{resource.FileFormat?.ToLower() ?? "bin"}";

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
            if (User.Identity?.IsAuthenticated ?? false)
                return RedirectToAction("Repository");

            // Only show PUBLIC resources (and not expired) to anonymous users
            var resources = await _context.Resources
                .IgnoreQueryFilters()
                .Include(r => r.User)
                .Where(r => r.Status == "Published" && r.AccessLevel == "Public")
                .Where(r => r.AccessDuration != "Custom" || r.AccessExpiresAt == null || r.AccessExpiresAt > DateTime.Now)
                .OrderByDescending(r => r.DateUploaded)
                .ToListAsync();

            var mapped = resources.Select(MapResource).ToList();
            ViewBag.Resources = mapped;
            ViewBag.AllResources = mapped;
            ViewBag.SearchQuery = q;
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

            if (resource == null || resource.Status != "Published" || resource.AccessLevel != "Public")
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
            if (!string.IsNullOrEmpty(resource.FilePath))
            {
                localUrl = $"/uploads/{resource.FilePath}";
                canPreview = true;
            }

            ViewBag.CloudUrl = localUrl;
            ViewBag.FileUrl = localUrl;
            ViewBag.CanPreview = canPreview;
            ViewBag.AllowDownloads = resource.AllowDownloads;
            ViewBag.AllowComments = false; // Anonymous users can't comment
            ViewBag.AllowRatings = false;
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
                ViewBag.ResourceVersions = await _context.ResourceVersions
                    .Where(v => v.ResourceId == resource.ResourceId)
                    .OrderByDescending(v => v.DateUpdated)
                    .ToListAsync();
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

            // KNN-powered recommendations for logged-in user
            var currentUser = await GetCurrentUserAsync();
            var recommendedList = new List<ResourceViewModel>();
            if (currentUser != null)
            {
                try
                {
                    var recIds = await _recommendationService.GetPersonalizedRecommendationsAsync(currentUser.Id, 4, GetEffectiveSchoolId());
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
            }
            if (!recommendedList.Any())
            {
                recommendedList = resources.OrderByDescending(r => r.Rating).Take(4).Select(MapResource).ToList();
            }
            ViewBag.Recommendations = recommendedList;
            ViewBag.Trending = resources.OrderByDescending(r => r.ViewCount).Take(5).Select(MapResource).ToList();

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
            ViewBag.CurrentUserId = currentUser?.Id;
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
        public async Task<IActionResult> Discussions(int id)
        {
            var currentUser = await GetCurrentUserAsync();
            ViewBag.CurrentUserId = currentUser?.Id;
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
            await LoadSchoolSettingsToViewBag();
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return RedirectToAction("Login");

            Resource? resource = null;
            if (id.HasValue)
            {
                resource = await _context.Resources.FirstOrDefaultAsync(r => r.ResourceId == id.Value);
                if (resource == null || (!User.IsInRole("SuperAdmin") && !User.IsInRole("Manager") && resource.UserId != currentUser.Id))
                {
                    return NotFound();
                }
                
                if (resource.Status == "Published" && !User.IsInRole("SuperAdmin") && !User.IsInRole("Manager"))
                {
                    TempData["ErrorMessage"] = "Published resources can no longer be edited. Please contact an administrator if you need to make changes.";
                    return RedirectToAction("MyUploads");
                }
            }

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
                
                if (resource.Status == "Published" && !User.IsInRole("SuperAdmin") && !User.IsInRole("Manager"))
                {
                    TempData["ErrorMessage"] = "Published resources can no longer be edited. Please contact an administrator if you need to make changes.";
                    return RedirectToAction("MyUploads");
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

            resource.Title = title ?? "";
            resource.Description = description ?? "";
            resource.Subject = subject ?? "";
            resource.GradeLevel = gradeLevel ?? "";
            resource.ResourceType = resourceType ?? "";
            resource.Quarter = quarter ?? "";
            resource.Status = isDraft ? "Draft" : "Pending";

            // ---- Policy Settings ----
            resource.AccessLevel = accessLevel ?? "Registered";
            resource.AccessDuration = accessDuration ?? "Unlimited";
            resource.AccessExpiresAt = (accessDuration == "Custom" && accessExpiresAt.HasValue)
                ? accessExpiresAt.Value
                : null;
            resource.AllowDownloads = allowDownloads;
            resource.AllowComments = allowComments;
            resource.AllowRatings = allowComments; // ratings follow comments toggle
            resource.EnableVersionHistory = enableVersionHistory;

            // Handle file upload — save local file
            if (file != null && file.Length > 0)
            {
                var ext = Path.GetExtension(file.FileName).TrimStart('.').ToLower();
                
                string uniqueFileName = Guid.NewGuid().ToString("N") + "." + ext;
                string uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads");
                
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(fileStream);
                }
               
                resource.FilePath = uniqueFileName;
                resource.FileFormat = ext.ToUpper();
                resource.FileSize = file.Length < 1024 * 1024
                    ? $"{file.Length / 1024.0:F1} KB"
                    : $"{file.Length / (1024.0 * 1024.0):F1} MB";

                if (resource.EnableVersionHistory)
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
                        FilePath = uniqueFileName,
                        FileFormat = ext.ToUpper(),
                        FileSize = resource.FileSize,
                        DateUpdated = DateTime.Now
                    };
                    _context.ResourceVersions.Add(newVersion);
                }
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

        // ==================== Batch / Multi-File Upload ====================

        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        [HttpPost]
        public async Task<IActionResult> BatchUpload(IList<IFormFile> files, string titlePrefix, string subject,
            string gradeLevel, string resourceType, string quarter, string description, string? tags = null, int[]? categoryIds = null)
        {
            var currentUser = await GetCurrentUserAsync();
            if (currentUser == null) return Json(new { success = false, message = "Not authenticated." });

            if (files == null || files.Count == 0)
                return Json(new { success = false, message = "No files selected." });

            var results = new List<object>();

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                foreach (var file in files)
                {
                    if (file.Length == 0) continue;

                    var ext = Path.GetExtension(file.FileName).TrimStart('.').ToLower();
                    var uniqueFileName = Guid.NewGuid().ToString("N") + "." + ext;
                    var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads");
                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                    var filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    using (var fs = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(fs);
                    }

                    var fileTitle = files.Count > 1
                        ? $"{titlePrefix} - {Path.GetFileNameWithoutExtension(file.FileName)}"
                        : titlePrefix;

                    var resource = new Resource
                    {
                        Title = fileTitle,
                        Description = description ?? "",
                        Subject = subject ?? "",
                        GradeLevel = gradeLevel ?? "",
                        ResourceType = resourceType ?? "",
                        Quarter = quarter ?? "",
                        FilePath = uniqueFileName,
                        FileFormat = ext.ToUpper(),
                        FileSize = file.Length < 1024 * 1024
                            ? $"{file.Length / 1024.0:F1} KB"
                            : $"{file.Length / (1024.0 * 1024.0):F1} MB",
                        UserId = currentUser.Id,
                        SchoolId = currentUser.SchoolId,
                        DateUploaded = DateTime.Now,
                        Status = "Pending"
                    };
                    _context.Resources.Add(resource);
                    await _context.SaveChangesAsync();

                    // Tags
                    if (!string.IsNullOrWhiteSpace(tags))
                    {
                        var tagNames = tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
                        foreach (var tagName in tagNames)
                        {
                            var tag = await _context.Tags.FirstOrDefaultAsync(t => t.TagName.ToLower() == tagName.ToLower());
                            if (tag == null) { tag = new Tag { TagName = tagName }; _context.Tags.Add(tag); await _context.SaveChangesAsync(); }
                            _context.ResourceTags.Add(new ResourceTag { ResourceId = resource.ResourceId, TagId = tag.TagId });
                        }
                    }
                    // Categories
                    if (categoryIds != null)
                    {
                        foreach (var catId in categoryIds.Distinct())
                            _context.ResourceCategoryMaps.Add(new ResourceCategoryMap { ResourceId = resource.ResourceId, CategoryId = catId });
                    }
                    await _context.SaveChangesAsync();

                    await LogActivity(currentUser.Id, "Upload", resource.Title, resource.ResourceId);
                    results.Add(new { id = resource.ResourceId, title = resource.Title, status = "Pending" });
                }

                await transaction.CommitAsync();
                return Json(new { success = true, count = results.Count, resources = results });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = ex.InnerException?.Message ?? ex.Message });
            }
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

            // Manager can only add users to their own school; SuperAdmin uses effective school
            var targetSchoolId = User.IsInRole("Manager") ? currentUser?.SchoolId : GetEffectiveSchoolId();

            // Manager cannot assign SuperAdmin role
            var role = model.Role ?? "Student";
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
                TempData["SuccessMessage"] = $"{model.Name} has been added successfully! (Temporary password: {password})";
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
            string timeZone, string dateFormat, string language,
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
            settings.TimeZone = timeZone ?? "Asia/Manila";
            settings.DateFormat = dateFormat ?? "MM/dd/yyyy";
            settings.Language = language ?? "English";

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
        public async Task<IActionResult> SwitchSchoolContext(int? schoolId)
        {
            if (schoolId.HasValue && schoolId.Value > 0)
            {
                var school = await _context.Schools.FindAsync(schoolId.Value);
                if (school != null)
                {
                    HttpContext.Session.SetInt32("SwitchedSchoolId", schoolId.Value);
                    TempData["SuccessMessage"] = $"Switched to {school.Name} context.";
                }
            }
            else
            {
                HttpContext.Session.Remove("SwitchedSchoolId");
                TempData["SuccessMessage"] = "Switched to All Schools view.";
            }
            return RedirectToAction("Dashboard");
        }

        // ==================== Reports ====================

        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        public async Task<IActionResult> Reports()
        {
            await LoadSchoolSettingsToViewBag();
            var schoolId = GetEffectiveSchoolId();

            // Resources auto-filtered by global query filter
            var resources = await _context.Resources.Include(r => r.User)
                .Where(r => r.Status == "Published")
                .ToListAsync();

            ViewBag.TotalEngagements = resources.Sum(r => r.ViewCount) + resources.Sum(r => r.DownloadCount);
            ViewBag.TotalDownloads = resources.Sum(r => r.DownloadCount);
            ViewBag.AvgRating = resources.Any() ? resources.Average(r => r.Rating) : 0;
            ViewBag.NewContributions = resources.Count;

            // Yesterday comparison for KPI cards
            var yesterday = DateTime.Now.Date;
            var yesterdayResources = resources.Where(r => r.DateUploaded < yesterday).ToList();
            var yesterdayEngagements = yesterdayResources.Sum(r => r.ViewCount) + yesterdayResources.Sum(r => r.DownloadCount);
            var yesterdayDownloads = yesterdayResources.Sum(r => r.DownloadCount);
            var yesterdayAvgRating = yesterdayResources.Any() ? yesterdayResources.Average(r => r.Rating) : 0;
            var yesterdayContributions = yesterdayResources.Count;
            ViewBag.YesterdayEngagements = yesterdayEngagements;
            ViewBag.YesterdayDownloads = yesterdayDownloads;
            ViewBag.YesterdayAvgRating = yesterdayAvgRating;
            ViewBag.YesterdayContributions = yesterdayContributions;
            ViewBag.TopResources = resources.OrderByDescending(r => r.ViewCount).Take(5).Select(MapResource).ToList();

            // Top contributors — school-scoped
            var allUsers = await _userManager.Users.ToListAsync();
            var scopedUsers = schoolId.HasValue
                ? allUsers.Where(u => u.SchoolId == schoolId.Value).ToList()
                : allUsers;
            var contributors = new List<UserViewModel>();
            foreach (var u in scopedUsers)
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
