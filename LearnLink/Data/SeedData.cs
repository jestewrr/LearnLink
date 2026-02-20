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

            // ===== 2. Seed Departments =====
            if (!context.Departments.Any())
            {
                context.Departments.AddRange(
                    new Department { DepartmentName = "Mathematics", Description = "Mathematics Department" },
                    new Department { DepartmentName = "English", Description = "English and Literature Department" },
                    new Department { DepartmentName = "Filipino", Description = "Filipino Language Department" },
                    new Department { DepartmentName = "Science", Description = "Science Department" },
                    new Department { DepartmentName = "History", Description = "Social Studies and History Department" },
                    new Department { DepartmentName = "TLE", Description = "Technology and Livelihood Education" },
                    new Department { DepartmentName = "MAPEH", Description = "Music, Arts, PE, and Health Department" }
                );
                await context.SaveChangesAsync();
            }

            // ===== 3. Seed Admin User Only =====
            await CreateUserAsync(userManager, "admin@learnlink.edu", "Admin123", "SuperAdmin",
                "Admin", "User", "AU", "", "System Administrator");

            // ===== 4. Seed Resource Categories =====
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

            // ===== 5. Seed Tags =====
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

        private static async Task<ApplicationUser?> CreateUserAsync(
            UserManager<ApplicationUser> userManager,
            string email, string password, string role,
            string firstName, string lastName, string initials,
            string avatarColor, string gradeOrPosition,
            int? departmentId = null,
            string status = "Active",
            string? suspensionReason = null)
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
            context.SystemLogs.RemoveRange(context.SystemLogs);
            context.DiscussionPosts.RemoveRange(context.DiscussionPosts);

            // 2. Clear Main Content Tables
            context.Discussions.RemoveRange(context.Discussions);
            context.Resources.RemoveRange(context.Resources);
            context.Policies.RemoveRange(context.Policies);
            
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
