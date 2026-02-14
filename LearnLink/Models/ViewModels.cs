namespace LearnLink.Models
{
    public class ResourceViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Subject { get; set; } = "";
        public string GradeLevel { get; set; } = "";
        public string ResourceType { get; set; } = "";
        public string FileFormat { get; set; } = "";
        public string FileSize { get; set; } = "";
        public string Status { get; set; } = "Published";
        public int ViewCount { get; set; }
        public int DownloadCount { get; set; }
        public double Rating { get; set; }
        public int RatingCount { get; set; }
        public string Uploader { get; set; } = "";
        public string UploaderInitials { get; set; } = "";
        public string UploaderColor { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string Quarter { get; set; } = "";
        public string IconClass { get; set; } = "bi-file-earmark";
        public string IconColor { get; set; } = "text-primary";
        public string IconBg { get; set; } = "#dbeafe";
    }

    public class LessonViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public string Category { get; set; } = "";
        public string Author { get; set; } = "";
        public string AuthorInitials { get; set; } = "";
        public string AuthorColor { get; set; } = "";
        public string AuthorRole { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<string> Tags { get; set; } = new();
        public int LikeCount { get; set; }
        public int CommentCount { get; set; }
        public bool IsLiked { get; set; }
    }

    public class DiscussionViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public string Author { get; set; } = "";
        public string AuthorInitials { get; set; } = "";
        public string AuthorColor { get; set; } = "";
        public string AuthorRole { get; set; } = "";
        public string Category { get; set; } = "";
        public string Type { get; set; } = "Question";
        public List<string> Tags { get; set; } = new();
        public int ReplyCount { get; set; }
        public int ViewCount { get; set; }
        public int LikeCount { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string Status { get; set; } = "Open";
        public bool IsLiked { get; set; }
    }

    public class ReplyViewModel
    {
        public int Id { get; set; }
        public string Content { get; set; } = "";
        public string Author { get; set; } = "";
        public string AuthorInitials { get; set; } = "";
        public string AuthorColor { get; set; } = "";
        public string AuthorRole { get; set; } = "";
        public string AuthorTitle { get; set; } = "";
        public int LikeCount { get; set; }
        public bool IsBestAnswer { get; set; }
        public bool IsLiked { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class UserViewModel
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string Initials { get; set; } = "";
        public string AvatarColor { get; set; } = "";
        public string Role { get; set; } = "";
        public string RoleBadgeClass { get; set; } = "ll-badge-info";
        public string GradeOrPosition { get; set; } = "";
        public string Status { get; set; } = "Active";
        public string StatusBadgeClass { get; set; } = "ll-badge-success";
        public DateTime JoinedAt { get; set; } = DateTime.Now;
        public string LastActive { get; set; } = "";
        public int ResourceCount { get; set; }
        public string? SuspensionReason { get; set; }
        public DateTime? SuspensionDate { get; set; }
    }

    public class ActivityViewModel
    {
        public string User { get; set; } = "";
        public string UserInitials { get; set; } = "";
        public string UserColor { get; set; } = "";
        public string Action { get; set; } = "";
        public string Target { get; set; } = "";
        public string TimeAgo { get; set; } = "";
        public string IconClass { get; set; } = "bi-activity";
        public string IconColor { get; set; } = "text-primary";
    }

    public class ReadingHistoryViewModel
    {
        public int ResourceId { get; set; }
        public string ResourceTitle { get; set; } = "";
        public string ResourceAuthor { get; set; } = "";
        public string ResourceSubject { get; set; } = "";
        public string ResourceGrade { get; set; } = "";
        public string ResourceFormat { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Author { get; set; } = "";
        public string FileFormat { get; set; } = "";
        public string IconClass { get; set; } = "bi-file-earmark";
        public string IconColor { get; set; } = "text-primary";
        public string IconBg { get; set; } = "#dbeafe";
        public int Progress { get; set; }
        public int ProgressPercent { get; set; }
        public string LastPosition { get; set; } = "";
        public DateTime ViewedAt { get; set; } = DateTime.Now;
        public DateTime LastAccessedDate { get; set; } = DateTime.Now;
        public DateTime? CompletedDate { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsBookmarked { get; set; }
        public string Status { get; set; } = "In Progress";
    }

    public class DashboardStatsViewModel
    {
        public int TotalResources { get; set; }
        public int ActiveUsers { get; set; }
        public int TotalDownloads { get; set; }
        public int ActiveDiscussions { get; set; }
    }
}
