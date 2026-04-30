using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LearnLink.Models
{
    // ==================== Schools (Multi-Tenant Root) ====================

    public class School
    {
        [Key]
        public int SchoolId { get; set; }

        [Required, StringLength(150)]
        public string Name { get; set; } = "";

        [Required, StringLength(20)]
        public string Code { get; set; } = "";  // Unique short code for registration (e.g. "SJHS")

        [StringLength(500)]
        public string Description { get; set; } = "";

        [StringLength(500)]
        public string Address { get; set; } = "";

        [StringLength(100)]
        public string ContactEmail { get; set; } = "";

        [StringLength(500)]
        public string? LogoPath { get; set; }

        public bool IsActive { get; set; } = true;

        public bool AllowCrossSchoolSharing { get; set; } = false;

        public DateTime DateCreated { get; set; } = DateTime.Now;

        // Navigation
        public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
        public ICollection<Department> Departments { get; set; } = new List<Department>();
        public ICollection<Resource> Resources { get; set; } = new List<Resource>();
        public ICollection<Discussion> Discussions { get; set; } = new List<Discussion>();
        public SchoolSettings? Settings { get; set; }
    }

    // ==================== School Settings (per-school config) ====================

    public class SchoolSettings
    {
        [Key]
        public int Id { get; set; }

        public int SchoolId { get; set; }

        [ForeignKey("SchoolId")]
        public School? School { get; set; }

        [StringLength(150)]
        public string InstitutionName { get; set; } = "";

        [StringLength(100)]
        public string AdminEmail { get; set; } = "";

        [StringLength(50)]
        public string TimeZone { get; set; } = "Asia/Manila";

        [StringLength(20)]
        public string DateFormat { get; set; } = "MM/dd/yyyy";

        [StringLength(20)]
        public string Language { get; set; } = "English";

        // JSON arrays for dynamic configuration (replaces hardcoded dropdown values)
        public string Subjects { get; set; } = "[\"Mathematics\",\"Science\",\"English\",\"Filipino\",\"Araling Panlipunan\",\"MAPEH\",\"TLE\",\"Values Education\"]";
        public string GradeLevels { get; set; } = "[\"Grade 7\",\"Grade 8\",\"Grade 9\",\"Grade 10\"]";
        public string ResourceTypes { get; set; } = "[\"Reviewer/Study Guide\",\"Lesson Plan\",\"Activity Sheet\",\"Assessment/Quiz\",\"Presentation\",\"Video Tutorial\",\"Reading Material\",\"Reference Document\"]";
        public string Quarters { get; set; } = "[\"1st Quarter\",\"2nd Quarter\",\"3rd Quarter\",\"4th Quarter\",\"All Quarters\"]";
    }

    // ==================== Users (extends Identity) ====================

    public class ApplicationUser : IdentityUser
    {
        [Required, StringLength(25)]
        public string FirstName { get; set; } = "";

        [Required, StringLength(25)]
        public string LastName { get; set; } = "";

        [StringLength(25)]
        public string? MiddleName { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "Active";

        public DateTime DateCreated { get; set; } = DateTime.Now;

        public int? DepartmentId { get; set; }

        [ForeignKey("DepartmentId")]
        public Department? Department { get; set; }

        // ----- Multi-tenancy: School assignment -----
        public int? SchoolId { get; set; }

        [ForeignKey("SchoolId")]
        public School? School { get; set; }

        // ----- Convenience / UI helpers (not in ERD but needed for views) -----
        [StringLength(5)]
        public string Initials { get; set; } = "";

        [StringLength(200)]
        public string AvatarColor { get; set; } = "";

        [StringLength(100)]
        public string GradeOrPosition { get; set; } = "";

        public string? SuspensionReason { get; set; }
        public DateTime? SuspensionDate { get; set; }

        // ----- Security: App-level progressive lockout (separate from Identity lockout) -----
        public int ConsecutiveFailedLogins { get; set; } = 0;
        public DateTime? LastFailedLoginAt { get; set; }
        public DateTime? AccountLockedUntil { get; set; }

        // ----- Password reset abuse prevention -----
        public int PasswordResetRequestCount { get; set; } = 0;
        public DateTime? PasswordResetWindowStart { get; set; }

        // Navigation
        public ICollection<Resource> Resources { get; set; } = new List<Resource>();
        public ICollection<LessonLearned> LessonsLearned { get; set; } = new List<LessonLearned>();
        public ICollection<Discussion> Discussions { get; set; } = new List<Discussion>();
        public ICollection<DiscussionPost> DiscussionPosts { get; set; } = new List<DiscussionPost>();
        public ICollection<ReadingHistory> ReadingHistories { get; set; } = new List<ReadingHistory>();
        public ICollection<UserActivityLog> ActivityLogs { get; set; } = new List<UserActivityLog>();
        public ICollection<BestPractice> BestPractices { get; set; } = new List<BestPractice>();
        public ICollection<Recommendation> Recommendations { get; set; } = new List<Recommendation>();
        public ICollection<Notification> Notifications { get; set; } = new List<Notification>();

        // Computed helper
        [NotMapped]
        public string FullName => $"{FirstName} {LastName}";
    }

    // ==================== Departments ====================

    public class Department
    {
        [Key]
        public int DepartmentId { get; set; }

        [Required, StringLength(50)]
        public string DepartmentName { get; set; } = "";

        [StringLength(100)]
        public string Description { get; set; } = "";

        // ----- Multi-tenancy: School assignment -----
        public int? SchoolId { get; set; }

        [ForeignKey("SchoolId")]
        public School? School { get; set; }

        public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();
    }

    // ==================== Knowledge Resources ====================

    public class Resource
    {
        [Key]
        public int ResourceId { get; set; }

        [Required, StringLength(100)]
        public string Title { get; set; } = "";

        [StringLength(500)]
        public string Description { get; set; } = "";

        [StringLength(500)]
        public string FilePath { get; set; } = "";


        [StringLength(30)]
        public string ResourceType { get; set; } = "";  // PDF, Reviewer, Sheet

        [Required]
        public string UserId { get; set; } = "";

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        // ----- Multi-tenancy: School assignment -----
        public int? SchoolId { get; set; }

        [ForeignKey("SchoolId")]
        public School? School { get; set; }

        public bool IsSharedCrossSchool { get; set; } = false;

        public DateTime DateUploaded { get; set; } = DateTime.Now;

        public DateTime? PendingReviewPreviewedAt { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "Active";  // Active, Archive, Pending, Draft

        // ----- Additional fields for existing UI -----
        [StringLength(50)]
        public string Subject { get; set; } = "";

        [StringLength(30)]
        public string GradeLevel { get; set; } = "";

        [StringLength(20)]
        public string FileFormat { get; set; } = "";

        [StringLength(20)]
        public string FileSize { get; set; } = "";

        [StringLength(50)]
        public string Quarter { get; set; } = "";

        public int ViewCount { get; set; }
        public int DownloadCount { get; set; }
        public double Rating { get; set; }
        public int RatingCount { get; set; }

        // Navigation
        public ICollection<ResourceCategoryMap> CategoryMaps { get; set; } = new List<ResourceCategoryMap>();
        public ICollection<ResourceTag> ResourceTags { get; set; } = new List<ResourceTag>();
        public ICollection<ResourceVersion> Versions { get; set; } = new List<ResourceVersion>();
        public ICollection<LessonLearned> LessonsLearned { get; set; } = new List<LessonLearned>();
        public ICollection<BestPractice> BestPractices { get; set; } = new List<BestPractice>();
        public ICollection<Recommendation> Recommendations { get; set; } = new List<Recommendation>();
        public ICollection<ReadingHistory> ReadingHistories { get; set; } = new List<ReadingHistory>();
        public ICollection<UserActivityLog> ActivityLogs { get; set; } = new List<UserActivityLog>();

        // Rejection
        [StringLength(500)]
        public string? RejectionReason { get; set; }

        // ---- Policy Settings (per-resource) ----
        [StringLength(20)]
        public string AccessLevel { get; set; } = "Registered";  // Public, Registered, Restricted

        [StringLength(30)]
        public string AccessDuration { get; set; } = "Unlimited";  // Unlimited, 7 Days, 30 Days, 90 Days, Until End of School Year

        public bool AllowDownloads { get; set; } = true;
        public bool EnableVersionHistory { get; set; } = true;
        public bool AllowComments { get; set; } = true;
        public bool AllowRatings { get; set; } = true;
        public bool ModerateComments { get; set; } = false;
        public bool RequireVersionNotes { get; set; } = false;

        public DateTime? AccessExpiresAt { get; set; }  // Custom expiry date for access duration

        // Navigation for restricted access grants
        public ICollection<ResourceAccessGrant> AccessGrants { get; set; } = new List<ResourceAccessGrant>();
    }

    // ==================== Resource Categories ====================

    public class ResourceCategory
    {
        [Key]
        public int CategoryId { get; set; }

        [Required, StringLength(50)]
        public string CategoryName { get; set; } = "";

        [StringLength(100)]
        public string Description { get; set; } = "";

        public ICollection<ResourceCategoryMap> CategoryMaps { get; set; } = new List<ResourceCategoryMap>();
    }

    // ==================== Resource Category Map (join table) ====================

    public class ResourceCategoryMap
    {
        [Key]
        public int MapId { get; set; }

        public int ResourceId { get; set; }

        [ForeignKey("ResourceId")]
        public Resource? Resource { get; set; }

        public int CategoryId { get; set; }

        [ForeignKey("CategoryId")]
        public ResourceCategory? Category { get; set; }
    }

    // ==================== Resource Versions ====================

    public class ResourceVersion
    {
        [Key]
        public int VersionId { get; set; }

        public int ResourceId { get; set; }

        [ForeignKey("ResourceId")]
        public Resource? Resource { get; set; }

        [StringLength(50)]
        public string VersionNumber { get; set; } = "";

        [StringLength(1000)]
        public string? VersionNotes { get; set; }

        [StringLength(500)]
        public string FilePath { get; set; } = "";

        [StringLength(10)]
        public string FileFormat { get; set; } = "";

        [StringLength(20)]
        public string FileSize { get; set; } = "";

        public DateTime DateUpdated { get; set; } = DateTime.Now;

        // Metadata snapshot for tracking changes
        [StringLength(100)]
        public string? Title { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        [StringLength(50)]
        public string? Subject { get; set; }

        [StringLength(30)]
        public string? GradeLevel { get; set; }

        [StringLength(30)]
        public string? ResourceType { get; set; }

        [StringLength(50)]
        public string? Quarter { get; set; }
    }

    // ==================== Tags ====================

    public class Tag
    {
        [Key]
        public int TagId { get; set; }

        [Required, StringLength(50)]
        public string TagName { get; set; } = "";

        public ICollection<ResourceTag> ResourceTags { get; set; } = new List<ResourceTag>();
    }

    // ==================== Resource Tags (join table) ====================

    public class ResourceTag
    {
        [Key]
        public int ResourceTagId { get; set; }

        public int ResourceId { get; set; }

        [ForeignKey("ResourceId")]
        public Resource? Resource { get; set; }

        public int TagId { get; set; }

        [ForeignKey("TagId")]
        public Tag? Tag { get; set; }
    }

    // ==================== Resource Comments ====================

    public class ResourceComment
    {
        [Key]
        public int CommentId { get; set; }

        public int ResourceId { get; set; }

        [ForeignKey("ResourceId")]
        public Resource? Resource { get; set; }

        [Required]
        public string UserId { get; set; } = "";

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        [Required, StringLength(2000)]
        public string Content { get; set; } = "";

        public DateTime DatePosted { get; set; } = DateTime.Now;

        public DateTime? DateUpdated { get; set; }

        public int? ParentCommentId { get; set; }

        [ForeignKey("ParentCommentId")]
        public ResourceComment? ParentComment { get; set; }

        public ICollection<ResourceComment> Replies { get; set; } = new List<ResourceComment>();

        public int LikeCount { get; set; } = 0;
    }

    // ==================== Lessons Learned ====================

    public class LessonLearned
    {
        [Key]
        public int LessonId { get; set; }

        public int ResourceId { get; set; }

        [ForeignKey("ResourceId")]
        public Resource? Resource { get; set; }

        [Required]
        public string UserId { get; set; } = "";

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        // ----- Multi-tenancy: School assignment -----
        public int? SchoolId { get; set; }

        [ForeignKey("SchoolId")]
        public School? School { get; set; }

        public float Rating { get; set; }

        [StringLength(500)]
        public string Comment { get; set; } = "";

        public DateTime DateSubmitted { get; set; } = DateTime.Now;

        // ----- Additional fields for existing Lessons Learned UI -----
        [StringLength(100)]
        public string Title { get; set; } = "";

        [StringLength(2000)]
        public string Content { get; set; } = "";

        [StringLength(50)]
        public string Category { get; set; } = "";

        [StringLength(500)]
        public string Tags { get; set; } = "";  // Comma-separated tags

        public int LikeCount { get; set; }
        public int CommentCount { get; set; }

        // Navigation
        public ICollection<LessonComment> Comments { get; set; } = new List<LessonComment>();
    }

    // ==================== Lesson Comments (uploader replies) ====================

    public class LessonComment
    {
        [Key]
        public int LessonCommentId { get; set; }

        public int LessonId { get; set; }

        [ForeignKey("LessonId")]
        public LessonLearned? Lesson { get; set; }

        [Required]
        public string UserId { get; set; } = "";

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        [Required, StringLength(2000)]
        public string Content { get; set; } = "";

        public DateTime DatePosted { get; set; } = DateTime.Now;
    }

    // ==================== Account Deletion Feedback ====================

    public class AccountDeletionFeedback
    {
        [Key]
        public int FeedbackId { get; set; }

        [Required, StringLength(500)]
        public string Reason { get; set; } = "";

        [StringLength(2000)]
        public string? Feedback { get; set; }

        [StringLength(100)]
        public string? UserEmail { get; set; }

        [StringLength(100)]
        public string? UserName { get; set; }

        [StringLength(50)]
        public string? UserRole { get; set; }

        public DateTime DeletedAt { get; set; } = DateTime.Now;
    }

    // ==================== Best Practices ====================

    public class BestPractice
    {
        [Key]
        public int PracticeId { get; set; }

        public int ResourceId { get; set; }

        [ForeignKey("ResourceId")]
        public Resource? Resource { get; set; }

        [StringLength(500)]
        public string Description { get; set; } = "";

        [Required]
        public string UserId { get; set; } = "";

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }
    }

    // ==================== Recommendations ====================

    public class Recommendation
    {
        [Key]
        public int RecommendationId { get; set; }

        [Required]
        public string UserId { get; set; } = "";

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        public int ResourceId { get; set; }

        [ForeignKey("ResourceId")]
        public Resource? Resource { get; set; }

        [StringLength(30)]
        public string RecommendationType { get; set; } = "";  // Rule based, user favorites
    }

    // ==================== Reading History ====================

    public class ReadingHistory
    {
        [Key]
        public int HistoryId { get; set; }

        [Required]
        public string UserId { get; set; } = "";

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        public int ResourceId { get; set; }

        [ForeignKey("ResourceId")]
        public Resource? Resource { get; set; }

        public DateTime LastAccessed { get; set; } = DateTime.Now;

        [StringLength(20)]
        public string ProgressStatus { get; set; } = "In Progress";  // In Progress, Completed

        // ----- Additional fields for existing UI -----
        public int ProgressPercent { get; set; }

        [StringLength(50)]
        public string LastPosition { get; set; } = "";

        public DateTime? CompletedDate { get; set; }
        public bool IsBookmarked { get; set; }
    }

    // ==================== Discussions ====================

    public class Discussion
    {
        [Key]
        public int DiscussionId { get; set; }

        [Required, StringLength(100)]
        public string Title { get; set; } = "";

        [Required]
        public string UserId { get; set; } = "";

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        // ----- Multi-tenancy: School assignment -----
        public int? SchoolId { get; set; }

        [ForeignKey("SchoolId")]
        public School? School { get; set; }

        public DateTime DateCreated { get; set; } = DateTime.Now;

        // ----- Additional fields for existing Knowledge Portal UI -----
        [StringLength(2000)]
        public string Content { get; set; } = "";

        [StringLength(50)]
        public string Category { get; set; } = "";

        [StringLength(30)]
        public string Type { get; set; } = "Question";  // Question, Resource, Study Group, Idea

        [StringLength(500)]
        public string Tags { get; set; } = "";  // Comma-separated

        [StringLength(20)]
        public string Status { get; set; } = "Open";

        public int ViewCount { get; set; }
        public int LikeCount { get; set; }

        // Navigation
        public ICollection<DiscussionPost> Posts { get; set; } = new List<DiscussionPost>();
    }

    // ==================== Discussion Posts (Replies) ====================

    public class DiscussionPost
    {
        [Key]
        public int PostId { get; set; }

        public int DiscussionId { get; set; }

        [ForeignKey("DiscussionId")]
        public Discussion? Discussion { get; set; }

        [Required]
        public string UserId { get; set; } = "";

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        [StringLength(2000)]
        public string Content { get; set; } = "";

        public DateTime DatePosted { get; set; } = DateTime.Now;

        // ----- Additional fields for existing UI -----
        public int LikeCount { get; set; }
        public bool IsBestAnswer { get; set; }
    }

    // ==================== User Activity Logs ====================

    public class UserActivityLog
    {
        [Key]
        public int ActivityId { get; set; }

        [Required]
        public string UserId { get; set; } = "";

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        public int? ResourceId { get; set; }

        [ForeignKey("ResourceId")]
        public Resource? Resource { get; set; }

        [StringLength(50)]
        public string ActivityType { get; set; } = "";  // View, Upload, Comment, Download, Approve, etc.

        public DateTime ActivityDate { get; set; } = DateTime.Now;

        // ----- Additional context for UI display -----
        [StringLength(200)]
        public string TargetTitle { get; set; } = "";
    }

    // ==================== Likes (per-user tracking, not in ERD but needed for UI) ====================

    public class Like
    {
        [Key]
        public int LikeId { get; set; }

        [Required]
        public string UserId { get; set; } = "";

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        [Required, StringLength(20)]
        public string TargetType { get; set; } = "";  // "Lesson", "Discussion", "Reply"

        public int TargetId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    // ==================== Notifications ====================

    public class Notification
    {
        [Key]
        public int NotificationId { get; set; }

        [Required]
        public string UserId { get; set; } = "";

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        [Required, StringLength(100)]
        public string Title { get; set; } = "";

        [StringLength(500)]
        public string Message { get; set; } = "";

        [StringLength(30)]
        public string Type { get; set; } = "";  // "Approved", "Rejected", "Upload", "System"

        [StringLength(50)]
        public string Icon { get; set; } = "bi-bell";

        [StringLength(20)]
        public string IconBg { get; set; } = "#dbeafe";

        [StringLength(500)]
        public string? Link { get; set; }

        public int? ResourceId { get; set; }

        [ForeignKey("ResourceId")]
        public Resource? Resource { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    // ==================== Resource Access Grants (for Restricted access level) ====================

    public class ResourceAccessGrant
    {
        [Key]
        public int GrantId { get; set; }

        public int ResourceId { get; set; }

        [ForeignKey("ResourceId")]
        public Resource? Resource { get; set; }

        [Required]
        public string UserId { get; set; } = "";

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        public DateTime GrantedAt { get; set; } = DateTime.Now;
    }

    // ==================== Login Attempts (security audit log) ====================

    public class LoginAttempt
    {
        [Key]
        public int LoginAttemptId { get; set; }

        [StringLength(256)]
        public string Email { get; set; } = "";

        public string? UserId { get; set; }

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        [StringLength(45)]
        public string IpAddress { get; set; } = "";

        [StringLength(512)]
        public string UserAgent { get; set; } = "";

        [StringLength(20)]
        public string Result { get; set; } = "";  // "Success", "Failed", "Locked", "Suspended"

        [StringLength(200)]
        public string? FailureReason { get; set; }

        public bool WasCaptchaRequired { get; set; }
        public bool WasCaptchaPassed { get; set; }

        public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
    }
}
