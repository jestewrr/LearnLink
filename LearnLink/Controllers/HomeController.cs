using System.Diagnostics;
using LearnLink.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LearnLink.Controllers
{
    public class HomeController : Controller
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;

        public HomeController(SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        // ==================== Sample Data Methods ====================

        private static List<ResourceViewModel> GetSampleResources()
        {
            return new List<ResourceViewModel>
            {
                new() { Id = 1, Title = "Mathematics Grade 8 Reviewer", Description = "Comprehensive reviewer covering algebra, geometry, and statistics for Grade 8 students.", Subject = "Mathematics", GradeLevel = "Grade 8", ResourceType = "Reviewer", FileFormat = "PDF", FileSize = "2.4 MB", Status = "Published", ViewCount = 3890, DownloadCount = 1245, Rating = 4.8, RatingCount = 156, Uploader = "Maria Santos", UploaderInitials = "MS", UploaderColor = "", Quarter = "Q3", IconClass = "bi-file-earmark-pdf", IconColor = "text-danger", IconBg = "#fee2e2", CreatedAt = new DateTime(2024, 12, 15) },
                new() { Id = 2, Title = "English Literature Study Guide", Description = "Study guide for Filipino and English literature with reading comprehension exercises.", Subject = "English", GradeLevel = "Grade 9", ResourceType = "Study Guide", FileFormat = "DOCX", FileSize = "1.8 MB", Status = "Pending", ViewCount = 0, DownloadCount = 0, Rating = 0, RatingCount = 0, Uploader = "Maria Santos", UploaderInitials = "MS", UploaderColor = "", Quarter = "Q2", IconClass = "bi-file-earmark-word", IconColor = "text-primary", IconBg = "#dbeafe", CreatedAt = new DateTime(2024, 12, 14) },
                new() { Id = 3, Title = "Philippine History Presentation", Description = "Interactive presentation on Philippine history from pre-colonial to modern era.", Subject = "History", GradeLevel = "Grade 10", ResourceType = "Presentation", FileFormat = "PPTX", FileSize = "5.2 MB", Status = "Published", ViewCount = 4562, DownloadCount = 1023, Rating = 4.9, RatingCount = 203, Uploader = "John Reyes", UploaderInitials = "JR", UploaderColor = "background: linear-gradient(135deg, #10b981, #059669)", Quarter = "Q1", IconClass = "bi-file-earmark-ppt", IconColor = "text-warning", IconBg = "#fef3c7", CreatedAt = new DateTime(2024, 12, 12) },
                new() { Id = 4, Title = "Science Lab Activities", Description = "Hands-on laboratory activities for General Science covering biology and chemistry.", Subject = "Science", GradeLevel = "Grade 7", ResourceType = "Activity Sheet", FileFormat = "PDF", FileSize = "3.1 MB", Status = "Draft", ViewCount = 0, DownloadCount = 0, Rating = 0, RatingCount = 0, Uploader = "Maria Santos", UploaderInitials = "MS", UploaderColor = "", Quarter = "Q4", IconClass = "bi-file-earmark", IconColor = "text-muted", IconBg = "#e2e8f0", CreatedAt = new DateTime(2024, 12, 10) },
                new() { Id = 5, Title = "Filipino Grammar Workbook", Description = "Complete grammar workbook with exercises on Filipino language structure and usage.", Subject = "Filipino", GradeLevel = "Grade 8", ResourceType = "Workbook", FileFormat = "PDF", FileSize = "4.0 MB", Status = "Published", ViewCount = 2341, DownloadCount = 876, Rating = 4.6, RatingCount = 98, Uploader = "Ana Cruz", UploaderInitials = "AC", UploaderColor = "background: linear-gradient(135deg, #f59e0b, #d97706)", Quarter = "Q2", IconClass = "bi-file-earmark-pdf", IconColor = "text-danger", IconBg = "#fee2e2", CreatedAt = new DateTime(2024, 12, 8) },
                new() { Id = 6, Title = "MAPEH Arts Module", Description = "Module covering music, arts, physical education, and health for junior high school.", Subject = "MAPEH", GradeLevel = "Grade 7", ResourceType = "Module", FileFormat = "PDF", FileSize = "6.5 MB", Status = "Published", ViewCount = 1987, DownloadCount = 654, Rating = 4.5, RatingCount = 72, Uploader = "Pedro Garcia", UploaderInitials = "PG", UploaderColor = "background: linear-gradient(135deg, #8b5cf6, #7c3aed)", Quarter = "Q3", IconClass = "bi-file-earmark-pdf", IconColor = "text-danger", IconBg = "#fee2e2", CreatedAt = new DateTime(2024, 12, 5) },
                new() { Id = 7, Title = "TLE Cookery Lesson Plan", Description = "Detailed lesson plan for Technology and Livelihood Education - Cookery strand.", Subject = "TLE", GradeLevel = "Grade 9", ResourceType = "Lesson Plan", FileFormat = "DOCX", FileSize = "1.2 MB", Status = "Published", ViewCount = 1543, DownloadCount = 432, Rating = 4.7, RatingCount = 65, Uploader = "Rosa Mendoza", UploaderInitials = "RM", UploaderColor = "background: linear-gradient(135deg, #ef4444, #dc2626)", Quarter = "Q1", IconClass = "bi-file-earmark-word", IconColor = "text-primary", IconBg = "#dbeafe", CreatedAt = new DateTime(2024, 12, 2) },
                new() { Id = 8, Title = "Values Education Worksheets", Description = "Worksheets on values formation, character development, and civic responsibility.", Subject = "Values Education", GradeLevel = "Grade 10", ResourceType = "Worksheet", FileFormat = "PDF", FileSize = "1.9 MB", Status = "Published", ViewCount = 1210, DownloadCount = 389, Rating = 4.4, RatingCount = 51, Uploader = "John Reyes", UploaderInitials = "JR", UploaderColor = "background: linear-gradient(135deg, #10b981, #059669)", Quarter = "Q4", IconClass = "bi-file-earmark-pdf", IconColor = "text-danger", IconBg = "#fee2e2", CreatedAt = new DateTime(2024, 11, 28) },
                new() { Id = 9, Title = "Math Algebra Basics for Beginners", Description = "Introduction to algebraic expressions, equations, and inequalities for Grade 7 students. Includes practice exercises and answer keys.", Subject = "Mathematics", GradeLevel = "Grade 7", ResourceType = "Module", FileFormat = "PDF", FileSize = "3.5 MB", Status = "Published", ViewCount = 2780, DownloadCount = 1102, Rating = 4.7, RatingCount = 134, Uploader = "Maria Santos", UploaderInitials = "MS", UploaderColor = "", Quarter = "Q1", IconClass = "bi-file-earmark-pdf", IconColor = "text-danger", IconBg = "#fee2e2", CreatedAt = new DateTime(2024, 11, 20) },
                new() { Id = 10, Title = "Math Geometry Theorems and Proofs", Description = "Covers fundamental geometry theorems including Pythagorean theorem, triangle congruence, and circle properties with step-by-step proofs.", Subject = "Mathematics", GradeLevel = "Grade 9", ResourceType = "Reviewer", FileFormat = "PPTX", FileSize = "4.8 MB", Status = "Published", ViewCount = 3120, DownloadCount = 987, Rating = 4.9, RatingCount = 178, Uploader = "John Reyes", UploaderInitials = "JR", UploaderColor = "background: linear-gradient(135deg, #10b981, #059669)", Quarter = "Q2", IconClass = "bi-file-earmark-ppt", IconColor = "text-warning", IconBg = "#fef3c7", CreatedAt = new DateTime(2024, 11, 15) },
                new() { Id = 11, Title = "Math Statistics and Probability Introduction", Description = "Beginner-friendly guide to statistics concepts: mean, median, mode, range, and basic probability with real-world examples.", Subject = "Mathematics", GradeLevel = "Grade 10", ResourceType = "Study Guide", FileFormat = "DOCX", FileSize = "2.1 MB", Status = "Published", ViewCount = 1890, DownloadCount = 654, Rating = 4.5, RatingCount = 89, Uploader = "Ana Cruz", UploaderInitials = "AC", UploaderColor = "background: linear-gradient(135deg, #f59e0b, #d97706)", Quarter = "Q3", IconClass = "bi-file-earmark-word", IconColor = "text-primary", IconBg = "#dbeafe", CreatedAt = new DateTime(2024, 11, 10) },
                new() { Id = 12, Title = "Math Trigonometry Basics and Applications", Description = "Introduction to trigonometric ratios (sine, cosine, tangent), unit circle, and real-world applications in measurement and navigation.", Subject = "Mathematics", GradeLevel = "Grade 10", ResourceType = "Module", FileFormat = "PDF", FileSize = "5.3 MB", Status = "Published", ViewCount = 2450, DownloadCount = 823, Rating = 4.6, RatingCount = 112, Uploader = "Pedro Garcia", UploaderInitials = "PG", UploaderColor = "background: linear-gradient(135deg, #8b5cf6, #7c3aed)", Quarter = "Q4", IconClass = "bi-file-earmark-pdf", IconColor = "text-danger", IconBg = "#fee2e2", CreatedAt = new DateTime(2024, 11, 5) }
            };
        }

        private static List<LessonViewModel> GetSampleLessons()
        {
            return new List<LessonViewModel>
            {
                new() { Id = 1, Title = "Effective Strategies for Teaching Fractions", Content = "Using visual aids and manipulatives significantly improved student understanding of fractions. Students who worked with physical fraction tiles scored 25% higher on assessments compared to those who only used traditional methods.", Category = "Teaching Strategies", Author = "Maria Santos", AuthorInitials = "MS", AuthorColor = "", AuthorRole = "Contributor", CreatedAt = new DateTime(2024, 12, 15), Tags = new() { "Mathematics", "Fractions", "Visual Learning" }, LikeCount = 24, CommentCount = 8 },
                new() { Id = 2, Title = "Integrating Technology in Science Labs", Content = "Virtual lab simulations can supplement physical experiments effectively. Students showed equal understanding when combining virtual and physical labs, while reducing material costs by 40%. The key is to use virtual labs for introduction and physical labs for reinforcement.", Category = "Technology Integration", Author = "John Reyes", AuthorInitials = "JR", AuthorColor = "background: linear-gradient(135deg, #10b981, #059669)", AuthorRole = "Contributor", CreatedAt = new DateTime(2024, 12, 12), Tags = new() { "Science", "Technology", "Virtual Labs" }, LikeCount = 31, CommentCount = 12 },
                new() { Id = 3, Title = "Building Reading Comprehension Skills", Content = "Implementing the SQ3R method (Survey, Question, Read, Recite, Review) showed remarkable improvement in reading comprehension scores. Students improved their reading speed by 15% and comprehension accuracy by 30% over one semester.", Category = "Reading Skills", Author = "Ana Cruz", AuthorInitials = "AC", AuthorColor = "background: linear-gradient(135deg, #f59e0b, #d97706)", AuthorRole = "Contributor", CreatedAt = new DateTime(2024, 12, 10), Tags = new() { "English", "Reading", "SQ3R" }, LikeCount = 18, CommentCount = 5 },
                new() { Id = 4, Title = "Classroom Management for Large Classes", Content = "Group rotation technique works effectively for classes with 40+ students. By dividing the class into 4 groups and rotating activities every 15 minutes, engagement increased and behavioral issues decreased by 60%.", Category = "Classroom Management", Author = "Pedro Garcia", AuthorInitials = "PG", AuthorColor = "background: linear-gradient(135deg, #8b5cf6, #7c3aed)", AuthorRole = "Manager", CreatedAt = new DateTime(2024, 12, 8), Tags = new() { "Management", "Large Classes", "Group Work" }, LikeCount = 42, CommentCount = 15 },
                new() { Id = 5, Title = "Assessment Alternatives Beyond Written Tests", Content = "Portfolio-based assessment combined with oral presentations provided a more holistic view of student learning. Students who struggled with written tests showed their understanding through creative projects and verbal explanations.", Category = "Assessment", Author = "Rosa Mendoza", AuthorInitials = "RM", AuthorColor = "background: linear-gradient(135deg, #ef4444, #dc2626)", AuthorRole = "Contributor", CreatedAt = new DateTime(2024, 12, 5), Tags = new() { "Assessment", "Portfolio", "Alternative" }, LikeCount = 27, CommentCount = 9 }
            };
        }

        private static List<DiscussionViewModel> GetSampleDiscussions()
        {
            return new List<DiscussionViewModel>
            {
                new() { Id = 1, Title = "Tips for Solving Quadratic Equations Faster", Content = "I've been teaching quadratic equations for years and noticed many students struggle with the formula method. Does anyone have tips or alternative approaches that work better for visual learners?", Author = "Maria Santos", AuthorInitials = "MS", AuthorColor = "", AuthorRole = "Contributor", Type = "Question", Category = "Teaching Strategies", Tags = new() { "Mathematics", "Grade 8", "Algebra" }, ReplyCount = 12, ViewCount = 156, LikeCount = 24, CreatedAt = DateTime.Now.AddHours(-2), Status = "Open" },
                new() { Id = 2, Title = "Sharing my Science Lab Safety Guidelines Poster", Content = "Created a colorful and engaging lab safety poster for our science classroom. Feel free to download and use it! It covers all the essential safety protocols in a way that students find easy to remember.", Author = "John Reyes", AuthorInitials = "JR", AuthorColor = "background: linear-gradient(135deg, #10b981, #059669)", AuthorRole = "Contributor", Type = "Resource", Category = "Resources", Tags = new() { "Science", "Lab Safety", "Poster" }, ReplyCount = 8, ViewCount = 234, LikeCount = 45, CreatedAt = DateTime.Now.AddHours(-5), Status = "Open" },
                new() { Id = 3, Title = "Study Group: Grade 9 English Literature - Short Stories", Content = "Starting a study group for Grade 9 students preparing for the upcoming English quarterly exam. We'll be focusing on short stories analysis and interpretation. Join us this Saturday!", Author = "Ana Cruz", AuthorInitials = "AC", AuthorColor = "background: linear-gradient(135deg, #f59e0b, #d97706)", AuthorRole = "User", Type = "Study Group", Category = "Study Groups", Tags = new() { "English", "Grade 9", "Study Group" }, ReplyCount = 25, ViewCount = 189, LikeCount = 18, CreatedAt = DateTime.Now.AddDays(-1), Status = "Open" },
                new() { Id = 4, Title = "💡 Idea: Interactive Timeline for Philippine History", Content = "What if we created an interactive digital timeline showing key events in Philippine History? Students could click on events to see detailed information, images, and related resources. Who's interested in collaborating?", Author = "Pedro Garcia", AuthorInitials = "PG", AuthorColor = "background: linear-gradient(135deg, #8b5cf6, #7c3aed)", AuthorRole = "Contributor", Type = "Idea", Category = "Ideas", Tags = new() { "History", "Interactive", "Collaboration" }, ReplyCount = 34, ViewCount = 412, LikeCount = 67, CreatedAt = DateTime.Now.AddDays(-2), Status = "Open" },
                new() { Id = 5, Title = "Best practices for creating engaging worksheets?", Content = "I want to create more engaging worksheets for my Filipino class. Any tips on layout, formatting, or activities that keep students interested? Looking for practical advice from experienced contributors.", Author = "Rosa Mendoza", AuthorInitials = "RM", AuthorColor = "background: linear-gradient(135deg, #ef4444, #dc2626)", AuthorRole = "Contributor", Type = "Question", Category = "Best Practices", Tags = new() { "Filipino", "Worksheet Design", "Best Practices" }, ReplyCount = 19, ViewCount = 278, LikeCount = 31, CreatedAt = DateTime.Now.AddDays(-3), Status = "Open" }
            };
        }

        private static List<ReplyViewModel> GetSampleReplies()
        {
            return new List<ReplyViewModel>
            {
                new() { Id = 1, Content = "<p>Great question, Maria! I've found that teaching the \"ac method\" (also known as factoring by grouping) works really well for visual learners. Here's a quick overview:</p><ol><li>Multiply a × c to get the \"magic number\"</li><li>Find two numbers that multiply to give the magic number and add to give b</li><li>Rewrite the middle term using these numbers</li><li>Factor by grouping</li></ol><p>I also recommend using colored markers to highlight different parts of the equation. Visual cues really help students track the coefficients!</p>", Author = "John Reyes", AuthorInitials = "JR", AuthorColor = "background: linear-gradient(135deg, #10b981, #059669)", AuthorRole = "Contributor", AuthorTitle = "Science Teacher", LikeCount = 15, IsBestAnswer = false, IsLiked = false, CreatedAt = DateTime.Now.AddHours(-1) },
                new() { Id = 2, Content = "<p>As a student, I can share that what helped me the most was practicing with the \"box method\" or area model. It made factoring trinomials so much easier to visualize! Also, using the quadratic formula song (to the tune of \"Pop Goes the Weasel\") really helped me remember it! 🎵</p>", Author = "Ana Cruz", AuthorInitials = "AC", AuthorColor = "background: linear-gradient(135deg, #f59e0b, #d97706)", AuthorRole = "User", AuthorTitle = "Grade 8 Student", LikeCount = 8, IsBestAnswer = false, IsLiked = true, CreatedAt = DateTime.Now.AddMinutes(-45) },
                new() { Id = 3, Content = "<p>I've compiled a comprehensive approach that has shown great results in our department:</p><div class=\"bg-light rounded p-3 mb-3\"><h6 class=\"fw-bold\">Multi-Sensory Approach to Quadratics:</h6><ol class=\"mb-0\"><li><strong>Visual:</strong> Use graphing calculators to show how changing a, b, c affects the parabola</li><li><strong>Kinesthetic:</strong> Have students physically sort coefficient cards</li><li><strong>Auditory:</strong> Create mnemonics for the formula (negative boy, etc.)</li><li><strong>Practice:</strong> Start with \"nice\" numbers before moving to complex ones</li></ol></div><p>I've actually uploaded a comprehensive resource on this topic. You can find it here: <a href=\"#\">Quadratic Equations Visual Guide</a></p>", Author = "Pedro Garcia", AuthorInitials = "PG", AuthorColor = "background: linear-gradient(135deg, #8b5cf6, #7c3aed)", AuthorRole = "Moderator", AuthorTitle = "Math Department Head", LikeCount = 32, IsBestAnswer = true, IsLiked = false, CreatedAt = DateTime.Now.AddMinutes(-30) }
            };
        }

        private static List<UserViewModel> GetSampleUsers()
        {
            return new List<UserViewModel>
            {
                new() { Name = "Maria Santos", Email = "maria.santos@learnlink.edu", Initials = "MS", AvatarColor = "", Role = "Contributor", RoleBadgeClass = "ll-badge-warning", GradeOrPosition = "Mathematics Teacher", Status = "Active", StatusBadgeClass = "ll-badge-success", JoinedAt = new DateTime(2024, 1, 15), LastActive = "2 hours ago", ResourceCount = 24 },
                new() { Name = "John Reyes", Email = "john.reyes@learnlink.edu", Initials = "JR", AvatarColor = "background: linear-gradient(135deg, #10b981, #059669)", Role = "Contributor", RoleBadgeClass = "ll-badge-warning", GradeOrPosition = "Science Teacher", Status = "Active", StatusBadgeClass = "ll-badge-success", JoinedAt = new DateTime(2024, 2, 8), LastActive = "5 hours ago", ResourceCount = 18 },
                new() { Name = "Ana Cruz", Email = "ana.cruz@student.learnlink.edu", Initials = "AC", AvatarColor = "background: linear-gradient(135deg, #f59e0b, #d97706)", Role = "Student", RoleBadgeClass = "ll-badge-info", GradeOrPosition = "Grade 8 - Section A", Status = "Active", StatusBadgeClass = "ll-badge-success", JoinedAt = new DateTime(2024, 3, 12), LastActive = "1 day ago", ResourceCount = 3 },
                new() { Name = "Pedro Garcia", Email = "pedro.garcia@learnlink.edu", Initials = "PG", AvatarColor = "background: linear-gradient(135deg, #8b5cf6, #7c3aed)", Role = "Manager", RoleBadgeClass = "ll-badge-danger", GradeOrPosition = "Content Moderator", Status = "Active", StatusBadgeClass = "ll-badge-success", JoinedAt = new DateTime(2024, 1, 5), LastActive = "3 hours ago", ResourceCount = 12 },
                new() { Name = "Rosa Mendoza", Email = "rosa.mendoza@student.learnlink.edu", Initials = "RM", AvatarColor = "background: linear-gradient(135deg, #ef4444, #dc2626)", Role = "Student", RoleBadgeClass = "ll-badge-info", GradeOrPosition = "Grade 9 - Section B", Status = "Pending", StatusBadgeClass = "ll-badge-warning", JoinedAt = new DateTime(2024, 12, 18), LastActive = "Never", ResourceCount = 0 },
                new() { Name = "Luis Torres", Email = "luis.torres@student.learnlink.edu", Initials = "LT", AvatarColor = "background: linear-gradient(135deg, #6b7280, #4b5563)", Role = "Student", RoleBadgeClass = "ll-badge-info", GradeOrPosition = "Grade 7 - Section C", Status = "Suspended", StatusBadgeClass = "ll-badge-danger", JoinedAt = new DateTime(2024, 4, 20), LastActive = "1 week ago", ResourceCount = 1 }
            };
        }

        private static List<ActivityViewModel> GetSampleActivities()
        {
            return new List<ActivityViewModel>
            {
                new() { User = "Maria Santos", UserInitials = "MS", UserColor = "", Action = "uploaded", Target = "Mathematics Grade 8 Reviewer", TimeAgo = "2 hours ago", IconClass = "bi-cloud-arrow-up", IconColor = "text-primary" },
                new() { User = "John Reyes", UserInitials = "JR", UserColor = "background: linear-gradient(135deg, #10b981, #059669)", Action = "commented on", Target = "Philippine History Presentation", TimeAgo = "4 hours ago", IconClass = "bi-chat-dots", IconColor = "text-success" },
                new() { User = "Ana Cruz", UserInitials = "AC", UserColor = "background: linear-gradient(135deg, #f59e0b, #d97706)", Action = "downloaded", Target = "Filipino Grammar Workbook", TimeAgo = "5 hours ago", IconClass = "bi-download", IconColor = "text-info" },
                new() { User = "Pedro Garcia", UserInitials = "PG", UserColor = "background: linear-gradient(135deg, #8b5cf6, #7c3aed)", Action = "approved", Target = "TLE Cookery Lesson Plan", TimeAgo = "1 day ago", IconClass = "bi-check-circle", IconColor = "text-warning" },
                new() { User = "Rosa Mendoza", UserInitials = "RM", UserColor = "background: linear-gradient(135deg, #ef4444, #dc2626)", Action = "started discussion", Target = "Study Tips for Grade 9", TimeAgo = "2 days ago", IconClass = "bi-chat-square-text", IconColor = "text-danger" }
            };
        }

        private static List<ReadingHistoryViewModel> GetSampleReadingHistory()
        {
            return new List<ReadingHistoryViewModel>
            {
                new() { ResourceId = 1, ResourceTitle = "Mathematics Grade 7 - Linear Equations", Title = "Mathematics Grade 7 - Linear Equations", ResourceAuthor = "Maria Santos", Author = "Maria Santos", ResourceSubject = "Mathematics", Subject = "Mathematics", ResourceGrade = "Grade 7", ResourceFormat = "PDF", FileFormat = "PDF", IconClass = "bi-file-earmark-pdf", IconColor = "text-danger", IconBg = "#fee2e2", Progress = 37, ProgressPercent = 37, LastPosition = "Page 45 of 120", ViewedAt = DateTime.Now.AddDays(-2), LastAccessedDate = DateTime.Now.AddDays(-2), IsCompleted = false, IsBookmarked = false, Status = "In Progress" },
                new() { ResourceId = 2, ResourceTitle = "Science Video: Cell Division Explained", Title = "Science Video: Cell Division Explained", ResourceAuthor = "John Reyes", Author = "John Reyes", ResourceSubject = "Science", Subject = "Science", ResourceGrade = "Grade 8", ResourceFormat = "MP4", FileFormat = "MP4", IconClass = "bi-play-circle", IconColor = "text-success", IconBg = "#dcfce7", Progress = 28, ProgressPercent = 28, LastPosition = "12:34 of 45:00", ViewedAt = DateTime.Now.AddDays(-3), LastAccessedDate = DateTime.Now.AddDays(-3), IsCompleted = false, IsBookmarked = true, Status = "In Progress" },
                new() { ResourceId = 3, ResourceTitle = "Philippine History - Pre-Colonial Period", Title = "Philippine History - Pre-Colonial Period", ResourceAuthor = "Pedro Garcia", Author = "Pedro Garcia", ResourceSubject = "History", Subject = "History", ResourceGrade = "Grade 10", ResourceFormat = "PPTX", FileFormat = "PPTX", IconClass = "bi-file-earmark-ppt", IconColor = "text-warning", IconBg = "#fef3c7", Progress = 100, ProgressPercent = 100, LastPosition = "Completed", ViewedAt = DateTime.Now.AddDays(-5), LastAccessedDate = DateTime.Now.AddDays(-5), CompletedDate = new DateTime(2024, 12, 10), IsCompleted = true, IsBookmarked = false, Status = "Completed" },
                new() { ResourceId = 4, ResourceTitle = "English Literature - Short Story Analysis", Title = "English Literature - Short Story Analysis", ResourceAuthor = "Ana Cruz", Author = "Ana Cruz", ResourceSubject = "English", Subject = "English", ResourceGrade = "Grade 9", ResourceFormat = "DOCX", FileFormat = "DOCX", IconClass = "bi-file-earmark-word", IconColor = "text-primary", IconBg = "#dbeafe", Progress = 100, ProgressPercent = 100, LastPosition = "Completed", ViewedAt = DateTime.Now.AddDays(-7), LastAccessedDate = DateTime.Now.AddDays(-7), CompletedDate = new DateTime(2024, 12, 8), IsCompleted = true, IsBookmarked = false, Status = "Completed" },
                new() { ResourceId = 5, ResourceTitle = "Algebra Basics - Grade 7 Primer", Title = "Algebra Basics - Grade 7 Primer", ResourceAuthor = "Maria Santos", Author = "Maria Santos", ResourceSubject = "Mathematics", Subject = "Mathematics", ResourceGrade = "Grade 7", ResourceFormat = "PDF", FileFormat = "PDF", IconClass = "bi-file-earmark-pdf", IconColor = "text-danger", IconBg = "#fee2e2", Progress = 100, ProgressPercent = 100, LastPosition = "Completed", ViewedAt = DateTime.Now.AddDays(-10), LastAccessedDate = DateTime.Now.AddDays(-10), CompletedDate = new DateTime(2024, 12, 5), IsCompleted = true, IsBookmarked = true, Status = "Completed" }
            };
        }

        // ==================== Public Pages ====================

        public IActionResult Index()
        {
            return View("Landing");
        }

        public IActionResult Privacy()
        {
            return View();
        }

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

            var result = await _signInManager.PasswordSignInAsync(user, password, rememberMe, false);
            if (result.Succeeded)
            {
                if (await _userManager.IsInRoleAsync(user, "Student"))
                {
                    return RedirectToAction("Repository");
                }
                return RedirectToAction("Dashboard");
            }

            ViewBag.Error = "Invalid email or password.";
            return View();
        }

        public IActionResult Register()
        {
            return View();
        }

        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login");
        }

        public IActionResult AccessDenied()
        {
            return View();
        }

        // ==================== Main Navigation — All Authenticated Users ====================

        [Authorize]
        public IActionResult Dashboard()
        {
            ViewBag.Stats = new DashboardStatsViewModel
            {
                TotalResources = GetSampleResources().Count,
                ActiveUsers = GetSampleUsers().Count(u => u.Status == "Active"),
                TotalDownloads = GetSampleResources().Sum(r => r.DownloadCount),
                ActiveDiscussions = GetSampleDiscussions().Count
            };
            ViewBag.RecentResources = GetSampleResources().OrderByDescending(r => r.CreatedAt).Take(5).ToList();
            ViewBag.RecentActivity = GetSampleActivities();
            return View();
        }

        [Authorize]
        public IActionResult Repository()
        {
            ViewBag.Resources = GetSampleResources();
            return View();
        }

        [AllowAnonymous]
        public IActionResult Search(string q = null)
        {
            var resources = GetSampleResources();
            ViewBag.Resources = resources;
            ViewBag.AllResources = resources;
            ViewBag.SearchQuery = q;

            // Anonymous visitors get a public search page (no sidebar, no auth header)
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return View("PublicSearch");
            }

            return View();
        }

        [Authorize]
        public IActionResult LessonsLearned()
        {
            var lessons = GetSampleLessons();
            ViewBag.Lessons = lessons;
            var categories = lessons.Select(l => l.Category).Distinct().ToList();
            ViewBag.Categories = categories;
            ViewBag.TotalLessons = lessons.Count;
            ViewBag.TotalLikes = lessons.Sum(l => l.LikeCount);
            ViewBag.TotalComments = lessons.Sum(l => l.CommentCount);
            ViewBag.Contributors = lessons.Select(l => l.Author).Distinct().Count();
            return View();
        }

        [Authorize]
        public IActionResult ReadingHistory()
        {
            var history = GetSampleReadingHistory();
            ViewBag.ReadingHistory = history;
            return View();
        }

        [Authorize]
        public IActionResult BestPractices()
        {
            var resources = GetSampleResources().Where(r => r.Status == "Published").ToList();
            ViewBag.Recommendations = resources.OrderByDescending(r => r.Rating).Take(4).ToList();
            ViewBag.Trending = resources.OrderByDescending(r => r.ViewCount).Take(5).ToList();
            ViewBag.ContinueReading = GetSampleReadingHistory().Where(h => !h.IsCompleted).ToList();
            return View();
        }

        [Authorize]
        public IActionResult KnowledgePortal()
        {
            ViewBag.Discussions = GetSampleDiscussions();
            return View();
        }

        [Authorize]
        public IActionResult Discussions(int id = 1)
        {
            var discussions = GetSampleDiscussions();
            ViewBag.Discussion = discussions.FirstOrDefault(d => d.Id == id) ?? discussions.First();
            ViewBag.Replies = GetSampleReplies();
            ViewBag.SimilarDiscussions = discussions.Where(d => d.Id != id).Take(3).ToList();
            return View();
        }

        // ==================== Upload / Content — SuperAdmin, Contributor, Manager ====================

        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        public IActionResult Upload()
        {
            ViewBag.RecentUploads = GetSampleResources().OrderByDescending(r => r.CreatedAt).Take(3).ToList();
            return View();
        }

        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        public IActionResult MyUploads()
        {
            var uploads = GetSampleResources();
            ViewBag.Uploads = uploads;
            ViewBag.TotalUploads = uploads.Count;
            ViewBag.PublishedCount = uploads.Count(r => r.Status == "Published");
            ViewBag.PendingCount = uploads.Count(r => r.Status == "Pending");
            ViewBag.DraftCount = uploads.Count(r => r.Status == "Draft");
            return View();
        }

        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        public IActionResult Policies()
        {
            return View();
        }

        // ==================== Administration — SuperAdmin Only ====================

        [Authorize(Roles = "SuperAdmin")]
        public IActionResult Users()
        {
            var users = GetSampleUsers();
            ViewBag.UserList = users;
            ViewBag.TotalUsers = users.Count;
            ViewBag.ActiveCount = users.Count(u => u.Status == "Active");
            ViewBag.ContributorCount = users.Count(u => u.Role == "Contributor");
            ViewBag.ManagerCount = users.Count(u => u.Role == "Manager");
            return View();
        }

        [Authorize(Roles = "SuperAdmin")]
        public IActionResult Settings()
        {
            return View();
        }

        // ==================== Reports — SuperAdmin, Contributor, Manager ====================

        [Authorize(Roles = "SuperAdmin,Contributor,Manager")]
        public IActionResult Reports()
        {
            var resources = GetSampleResources().Where(r => r.Status == "Published").ToList();
            ViewBag.TotalEngagements = resources.Sum(r => r.ViewCount) + resources.Sum(r => r.DownloadCount);
            ViewBag.TotalDownloads = resources.Sum(r => r.DownloadCount);
            ViewBag.AvgRating = resources.Any() ? resources.Average(r => r.Rating) : 0;
            ViewBag.NewContributions = resources.Count;
            ViewBag.TopResources = resources.OrderByDescending(r => r.ViewCount).Take(5).ToList();
            ViewBag.TopContributors = GetSampleUsers().Where(u => u.Role == "Contributor" || u.Role == "Manager").Take(4).ToList();
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
