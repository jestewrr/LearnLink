using LearnLink.Models;
using LearnLink.Services;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LearnLink.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        private readonly ISchoolContext? _schoolContext;

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ISchoolContext? schoolContext = null)
            : base(options)
        {
            _schoolContext = schoolContext;
        }

        // ===== Multi-Tenancy =====
        public DbSet<School> Schools { get; set; }
        public DbSet<SchoolSettings> SchoolSettings { get; set; }

        // ===== Core Tables =====
        public DbSet<Department> Departments { get; set; }
        public DbSet<Resource> Resources { get; set; }
        public DbSet<ResourceCategory> ResourceCategories { get; set; }
        public DbSet<ResourceCategoryMap> ResourceCategoryMaps { get; set; }
        public DbSet<ResourceVersion> ResourceVersions { get; set; }
        public DbSet<Tag> Tags { get; set; }
        public DbSet<ResourceTag> ResourceTags { get; set; }
        public DbSet<ResourceComment> ResourceComments { get; set; }

        // ===== Knowledge Management =====
        public DbSet<LessonLearned> LessonsLearned { get; set; }
        public DbSet<LessonComment> LessonComments { get; set; }
        public DbSet<BestPractice> BestPractices { get; set; }
        public DbSet<Recommendation> Recommendations { get; set; }
        public DbSet<AccountDeletionFeedback> AccountDeletionFeedbacks { get; set; }

        // ===== Discussions =====
        public DbSet<Discussion> Discussions { get; set; }
        public DbSet<DiscussionPost> DiscussionPosts { get; set; }

        // ===== Policies =====
        public DbSet<ResourceAccessGrant> ResourceAccessGrants { get; set; }

        // ===== User Activity =====
        public DbSet<ReadingHistory> ReadingHistories { get; set; }
        public DbSet<UserActivityLog> UserActivityLogs { get; set; }

        public DbSet<Like> Likes { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<LoginAttempt> LoginAttempts { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ===== Multi-Tenancy: School relationships =====
            builder.Entity<School>()
                .HasIndex(s => s.Code)
                .IsUnique();

            builder.Entity<SchoolSettings>()
                .HasOne(ss => ss.School)
                .WithOne(s => s.Settings)
                .HasForeignKey<SchoolSettings>(ss => ss.SchoolId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ApplicationUser>()
                .HasOne(u => u.School)
                .WithMany(s => s.Users)
                .HasForeignKey(u => u.SchoolId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Department>()
                .HasOne(d => d.School)
                .WithMany(s => s.Departments)
                .HasForeignKey(d => d.SchoolId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Resource>()
                .HasOne(r => r.School)
                .WithMany(s => s.Resources)
                .HasForeignKey(r => r.SchoolId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Discussion>()
                .HasOne(d => d.School)
                .WithMany(s => s.Discussions)
                .HasForeignKey(d => d.SchoolId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<LessonLearned>()
                .HasOne(l => l.School)
                .WithMany()
                .HasForeignKey(l => l.SchoolId)
                .OnDelete(DeleteBehavior.SetNull);

            // ===== Multi-Tenancy: Indexes =====
            builder.Entity<ApplicationUser>().HasIndex(u => u.SchoolId);
            builder.Entity<Resource>().HasIndex(r => r.SchoolId);
            builder.Entity<Discussion>().HasIndex(d => d.SchoolId);
            builder.Entity<Department>().HasIndex(d => d.SchoolId);

            // ===== Multi-Tenancy: Global Query Filters =====
            // When _schoolContext is null or CurrentSchoolId is null (SuperAdmin), no filter is applied.
            // Otherwise, entities are filtered to the current school.
            var schoolId = _schoolContext?.CurrentSchoolId;

            builder.Entity<Resource>().HasQueryFilter(r =>
                schoolId == null || r.SchoolId == schoolId || r.IsSharedCrossSchool);

            builder.Entity<Discussion>().HasQueryFilter(d =>
                schoolId == null || d.SchoolId == schoolId);

            builder.Entity<LessonLearned>().HasQueryFilter(l =>
                schoolId == null || l.SchoolId == schoolId);

            builder.Entity<Department>().HasQueryFilter(d =>
                schoolId == null || d.SchoolId == schoolId);

            // ===== Department → Users (1:many) =====
            builder.Entity<ApplicationUser>()
                .HasOne(u => u.Department)
                .WithMany(d => d.Users)
                .HasForeignKey(u => u.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull);

            // ===== Resource → User (many:1) =====
            builder.Entity<Resource>()
                .HasOne(r => r.User)
                .WithMany(u => u.Resources)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // ===== ResourceCategoryMap (join) =====
            builder.Entity<ResourceCategoryMap>()
                .HasOne(m => m.Resource)
                .WithMany(r => r.CategoryMaps)
                .HasForeignKey(m => m.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ResourceCategoryMap>()
                .HasOne(m => m.Category)
                .WithMany(c => c.CategoryMaps)
                .HasForeignKey(m => m.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);

            // ===== ResourceVersion → Resource (many:1) =====
            builder.Entity<ResourceVersion>()
                .HasOne(v => v.Resource)
                .WithMany(r => r.Versions)
                .HasForeignKey(v => v.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);

            // ===== ResourceTag (join) =====
            builder.Entity<ResourceTag>()
                .HasOne(rt => rt.Resource)
                .WithMany(r => r.ResourceTags)
                .HasForeignKey(rt => rt.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ResourceTag>()
                .HasOne(rt => rt.Tag)
                .WithMany(t => t.ResourceTags)
                .HasForeignKey(rt => rt.TagId)
                .OnDelete(DeleteBehavior.Cascade);

            // ===== ResourceComment → Resource (many:1) =====
            builder.Entity<ResourceComment>()
                .HasOne(c => c.Resource)
                .WithMany()
                .HasForeignKey(c => c.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ResourceComment>()
                .HasOne(c => c.User)
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.Entity<ResourceComment>()
                .HasOne(c => c.ParentComment)
                .WithMany(c => c.Replies)
                .HasForeignKey(c => c.ParentCommentId)
                .OnDelete(DeleteBehavior.NoAction);

            // ===== LessonLearned → Resource + User =====
            builder.Entity<LessonLearned>()
                .HasOne(l => l.Resource)
                .WithMany(r => r.LessonsLearned)
                .HasForeignKey(l => l.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<LessonLearned>()
                .HasOne(l => l.User)
                .WithMany(u => u.LessonsLearned)
                .HasForeignKey(l => l.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            // ===== LessonComment → Lesson + User =====
            builder.Entity<LessonComment>()
                .HasOne(lc => lc.Lesson)
                .WithMany(l => l.Comments)
                .HasForeignKey(lc => lc.LessonId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<LessonComment>()
                .HasOne(lc => lc.User)
                .WithMany()
                .HasForeignKey(lc => lc.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            // ===== BestPractice → Resource + User =====
            builder.Entity<BestPractice>()
                .HasOne(b => b.Resource)
                .WithMany(r => r.BestPractices)
                .HasForeignKey(b => b.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<BestPractice>()
                .HasOne(b => b.User)
                .WithMany(u => u.BestPractices)
                .HasForeignKey(b => b.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            // ===== Recommendation → User + Resource =====
            builder.Entity<Recommendation>()
                .HasOne(r => r.User)
                .WithMany(u => u.Recommendations)
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Recommendation>()
                .HasOne(r => r.Resource)
                .WithMany(res => res.Recommendations)
                .HasForeignKey(r => r.ResourceId)
                .OnDelete(DeleteBehavior.NoAction);

            // ===== ReadingHistory → User + Resource =====
            builder.Entity<ReadingHistory>()
                .HasOne(rh => rh.User)
                .WithMany(u => u.ReadingHistories)
                .HasForeignKey(rh => rh.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ReadingHistory>()
                .HasOne(rh => rh.Resource)
                .WithMany(r => r.ReadingHistories)
                .HasForeignKey(rh => rh.ResourceId)
                .OnDelete(DeleteBehavior.NoAction);

            // ===== Discussion → User (many:1) =====
            builder.Entity<Discussion>()
                .HasOne(d => d.User)
                .WithMany(u => u.Discussions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // ===== DiscussionPost → Discussion + User =====
            builder.Entity<DiscussionPost>()
                .HasOne(dp => dp.Discussion)
                .WithMany(d => d.Posts)
                .HasForeignKey(dp => dp.DiscussionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<DiscussionPost>()
                .HasOne(dp => dp.User)
                .WithMany(u => u.DiscussionPosts)
                .HasForeignKey(dp => dp.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            // ===== UserActivityLog → User + Resource =====
            builder.Entity<UserActivityLog>()
                .HasOne(a => a.User)
                .WithMany(u => u.ActivityLogs)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<UserActivityLog>()
                .HasOne(a => a.Resource)
                .WithMany(r => r.ActivityLogs)
                .HasForeignKey(a => a.ResourceId)
                .OnDelete(DeleteBehavior.NoAction);

            // ===== Like (polymorphic) =====
            builder.Entity<Like>()
                .HasIndex(l => new { l.UserId, l.TargetType, l.TargetId })
                .IsUnique();

            // ===== Indexes =====
            builder.Entity<Resource>().HasIndex(r => r.Status);
            builder.Entity<Resource>().HasIndex(r => r.Subject);
            builder.Entity<Discussion>().HasIndex(d => d.DateCreated);
            builder.Entity<UserActivityLog>().HasIndex(a => a.ActivityDate);
            builder.Entity<Tag>().HasIndex(t => t.TagName).IsUnique();

            // ===== Notification → User + Resource =====
            builder.Entity<Notification>()
                .HasOne(n => n.User)
                .WithMany(u => u.Notifications)
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Notification>()
                .HasOne(n => n.Resource)
                .WithMany()
                .HasForeignKey(n => n.ResourceId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.Entity<Notification>().HasIndex(n => new { n.UserId, n.IsRead });

            // ===== ResourceAccessGrant → Resource + User =====
            builder.Entity<ResourceAccessGrant>()
                .HasOne(g => g.Resource)
                .WithMany(r => r.AccessGrants)
                .HasForeignKey(g => g.ResourceId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ResourceAccessGrant>()
                .HasOne(g => g.User)
                .WithMany()
                .HasForeignKey(g => g.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.Entity<ResourceAccessGrant>()
                .HasIndex(g => new { g.ResourceId, g.UserId })
                .IsUnique();

            // ===== LoginAttempt → User =====
            builder.Entity<LoginAttempt>()
                .HasOne(la => la.User)
                .WithMany()
                .HasForeignKey(la => la.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<LoginAttempt>().HasIndex(la => la.Email);
            builder.Entity<LoginAttempt>().HasIndex(la => la.AttemptedAt);
            builder.Entity<LoginAttempt>().HasIndex(la => la.IpAddress);
        }
    }
}
