using LearnLink.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LearnLink.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // ===== Core Tables =====
        public DbSet<Department> Departments { get; set; }
        public DbSet<Resource> Resources { get; set; }
        public DbSet<ResourceCategory> ResourceCategories { get; set; }
        public DbSet<ResourceCategoryMap> ResourceCategoryMaps { get; set; }
        public DbSet<ResourceVersion> ResourceVersions { get; set; }
        public DbSet<Tag> Tags { get; set; }
        public DbSet<ResourceTag> ResourceTags { get; set; }

        // ===== Knowledge Management =====
        public DbSet<LessonLearned> LessonsLearned { get; set; }
        public DbSet<BestPractice> BestPractices { get; set; }
        public DbSet<Recommendation> Recommendations { get; set; }

        // ===== Discussions =====
        public DbSet<Discussion> Discussions { get; set; }
        public DbSet<DiscussionPost> DiscussionPosts { get; set; }

        // ===== Policies =====
        public DbSet<Policy> Policies { get; set; }
        public DbSet<Procedure> Procedures { get; set; }

        // ===== User Activity =====
        public DbSet<ReadingHistory> ReadingHistories { get; set; }
        public DbSet<UserActivityLog> UserActivityLogs { get; set; }
        public DbSet<SystemLog> SystemLogs { get; set; }
        public DbSet<Like> Likes { get; set; }
        public DbSet<Notification> Notifications { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

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

            // ===== Policy → User (many:1) =====
            builder.Entity<Policy>()
                .HasOne(p => p.User)
                .WithMany(u => u.Policies)
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // ===== Procedure → Policy (many:1) =====
            builder.Entity<Procedure>()
                .HasOne(p => p.Policy)
                .WithMany(pol => pol.Procedures)
                .HasForeignKey(p => p.PolicyId)
                .OnDelete(DeleteBehavior.Cascade);

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

            // ===== SystemLog → User (many:1) =====
            builder.Entity<SystemLog>()
                .HasOne(s => s.User)
                .WithMany(u => u.SystemLogs)
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

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
        }
    }
}
