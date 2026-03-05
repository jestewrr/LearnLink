using LearnLink.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LearnLink.Data
{
    public static class SeedData
    {
        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();

            // ===== 1. Seed Roles =====
            string[] roles = ["SuperAdmin", "Student", "Contributor", "Manager"];
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
            }

            // ===== 2. Seed Default School =====
            var defaultSchool = await context.Schools
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.Code == "SJHS");

            if (defaultSchool == null)
            {
                defaultSchool = new School
                {
                    Name = "Sample Junior High School",
                    Code = "SJHS",
                    Description = "Default school for LearnLink",
                    Address = "",
                    ContactEmail = "admin@learnlink.edu",
                    IsActive = true,
                    AllowCrossSchoolSharing = false,
                    DateCreated = DateTime.Now
                };
                context.Schools.Add(defaultSchool);
                await context.SaveChangesAsync();

                // Seed school settings
                context.SchoolSettings.Add(new SchoolSettings
                {
                    SchoolId = defaultSchool.SchoolId,
                    InstitutionName = "Sample Junior High School",
                    AdminEmail = "admin@learnlink.edu",
                    TimeZone = "Asia/Manila",
                    DateFormat = "MM/dd/yyyy",
                    Language = "English"
                });
                await context.SaveChangesAsync();
            }

            // ===== 3. Seed Departments (assigned to default school) =====
            if (!context.Departments.IgnoreQueryFilters().Any())
            {
                context.Departments.AddRange(
                    new Department { DepartmentName = "Mathematics", Description = "Mathematics Department", SchoolId = defaultSchool.SchoolId },
                    new Department { DepartmentName = "English", Description = "English and Literature Department", SchoolId = defaultSchool.SchoolId },
                    new Department { DepartmentName = "Filipino", Description = "Filipino Language Department", SchoolId = defaultSchool.SchoolId },
                    new Department { DepartmentName = "Science", Description = "Science Department", SchoolId = defaultSchool.SchoolId },
                    new Department { DepartmentName = "History", Description = "Social Studies and History Department", SchoolId = defaultSchool.SchoolId },
                    new Department { DepartmentName = "TLE", Description = "Technology and Livelihood Education", SchoolId = defaultSchool.SchoolId },
                    new Department { DepartmentName = "MAPEH", Description = "Music, Arts, PE, and Health Department", SchoolId = defaultSchool.SchoolId }
                );
                await context.SaveChangesAsync();
            }

            // ===== 4. Seed Admin User (platform-level, no school) =====
            await CreateUserAsync(userManager, "admin@learnlink.edu", "Admin123", "SuperAdmin",
                "Admin", "User", "AU", "", "System Administrator", schoolId: null);

            // ===== 5. Migrate existing un-assigned data to default school =====
            await AssignOrphanedDataToSchool(context, defaultSchool.SchoolId);

            // ===== 6. Seed Resource Categories =====
            if (!context.ResourceCategories.Any())
            {
                context.ResourceCategories.AddRange(
                    new ResourceCategory { CategoryName = "Reviewer", Description = "Review materials and summaries" },
                    new ResourceCategory { CategoryName = "Study Guide", Description = "Comprehensive study guides" },
                    new ResourceCategory { CategoryName = "Presentation", Description = "Slide presentations" },
                    new ResourceCategory { CategoryName = "Module", Description = "Learning modules" },
                    new ResourceCategory { CategoryName = "Activity Sheet", Description = "Hands-on activities" },
                    new ResourceCategory { CategoryName = "Workbook", Description = "Practice workbooks" },
                    new ResourceCategory { CategoryName = "Lesson Plan", Description = "Structured lesson plans" },
                    new ResourceCategory { CategoryName = "Worksheet", Description = "Practice worksheets" }
                );
                await context.SaveChangesAsync();
            }

            // ===== 7. Seed Tags =====
            if (!context.Tags.Any())
            {
                var tagNames = new[] { "Mathematics", "Science", "English", "Filipino", "History",
                    "Grade 7", "Grade 8", "Grade 9", "Grade 10",
                    "Algebra", "Geometry", "Statistics", "Trigonometry",
                    "Lab Safety", "Reading", "Grammar", "MAPEH", "TLE",
                    "Teaching Strategies", "Technology", "Assessment",
                    "Fractions", "Visual Learning", "Virtual Labs", "SQ3R",
                    "Management", "Large Classes", "Group Work", "Portfolio" };

                foreach (var name in tagNames)
                    context.Tags.Add(new Tag { TagName = name });

                await context.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Assigns any existing data that has no SchoolId to the default school.
        /// This handles data created before multi-tenancy was added.
        /// </summary>
        private static async Task AssignOrphanedDataToSchool(ApplicationDbContext context, int schoolId)
        {
            // Assign orphaned departments
            var orphanedDepts = await context.Departments.IgnoreQueryFilters()
                .Where(d => d.SchoolId == null).ToListAsync();
            foreach (var d in orphanedDepts) d.SchoolId = schoolId;

            // Assign orphaned resources
            var orphanedResources = await context.Resources.IgnoreQueryFilters()
                .Where(r => r.SchoolId == null).ToListAsync();
            foreach (var r in orphanedResources) r.SchoolId = schoolId;

            // Assign orphaned discussions
            var orphanedDiscussions = await context.Discussions.IgnoreQueryFilters()
                .Where(d => d.SchoolId == null).ToListAsync();
            foreach (var d in orphanedDiscussions) d.SchoolId = schoolId;

            // Assign orphaned lessons learned
            var orphanedLessons = await context.LessonsLearned.IgnoreQueryFilters()
                .Where(l => l.SchoolId == null).ToListAsync();
            foreach (var l in orphanedLessons) l.SchoolId = schoolId;

            // Assign orphaned non-admin users
            var adminEmails = new[] { "admin@learnlink.edu" };
            var orphanedUsers = await context.Users
                .Where(u => u.SchoolId == null && !adminEmails.Contains(u.Email))
                .ToListAsync();
            foreach (var u in orphanedUsers) u.SchoolId = schoolId;

            if (orphanedDepts.Any() || orphanedResources.Any() || orphanedDiscussions.Any()
                || orphanedLessons.Any() || orphanedUsers.Any())
            {
                await context.SaveChangesAsync();
            }
        }

        private static async Task<ApplicationUser?> CreateUserAsync(
            UserManager<ApplicationUser> userManager,
            string email, string password, string role,
            string firstName, string lastName, string initials,
            string avatarColor, string gradeOrPosition,
            int? departmentId = null,
            string status = "Active",
            string? suspensionReason = null,
            int? schoolId = null)
        {
            var existing = await userManager.FindByEmailAsync(email);
            if (existing != null) return existing;

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = firstName,
                LastName = lastName,
                Initials = initials,
                AvatarColor = avatarColor,
                GradeOrPosition = gradeOrPosition,
                DepartmentId = departmentId,
                SchoolId = schoolId,
                Status = status,
                DateCreated = DateTime.Now,
                SuspensionReason = suspensionReason,
                SuspensionDate = suspensionReason != null ? DateTime.Now : null
            };

            var result = await userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, role);
                return user;
            }
            return null;
        }
        public static async Task ClearDatabaseAsync(ApplicationDbContext context)
        {
            // 1. Clear Dependent Tables (Child records first)
            context.LessonsLearned.RemoveRange(context.LessonsLearned);
            context.BestPractices.RemoveRange(context.BestPractices);
            context.Recommendations.RemoveRange(context.Recommendations);
            context.ReadingHistories.RemoveRange(context.ReadingHistories);
            context.UserActivityLogs.RemoveRange(context.UserActivityLogs);
            context.Notifications.RemoveRange(context.Notifications);
            context.DiscussionPosts.RemoveRange(context.DiscussionPosts);

            // 2. Clear Main Content Tables
            context.Discussions.RemoveRange(context.Discussions);
            context.Resources.RemoveRange(context.Resources);
            
            // 3. Save Changes to Database
            await context.SaveChangesAsync();

            // 4. Clear Uploaded Files
            var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
            if (Directory.Exists(uploadsDir))
            {
                var files = Directory.GetFiles(uploadsDir);
                foreach (var file in files)
                {
                    try { File.Delete(file); } catch { /* Ignore file lock errors */ }
                }
            }
        }
    }
}
