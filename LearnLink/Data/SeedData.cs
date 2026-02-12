using Microsoft.AspNetCore.Identity;

namespace LearnLink.Data
{
    public static class SeedData
    {
        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<IdentityUser>>();

            // Define roles
            string[] roles = ["SuperAdmin", "Student", "Contributor", "Manager"];

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            // Seed Super Admin
            await CreateUserAsync(userManager,
                email: "admin@learnlink.edu",
                password: "Admin123",
                role: "SuperAdmin");

            // Seed Student
            await CreateUserAsync(userManager,
                email: "student@learnlink.edu",
                password: "Student123",
                role: "Student");

            // Seed Contributor
            await CreateUserAsync(userManager,
                email: "contributor@learnlink.edu",
                password: "Contributor123",
                role: "Contributor");

            // Seed Manager
            await CreateUserAsync(userManager,
                email: "manager@learnlink.edu",
                password: "Manager123",
                role: "Manager");
        }

        private static async Task CreateUserAsync(
            UserManager<IdentityUser> userManager,
            string email,
            string password,
            string role)
        {
            if (await userManager.FindByEmailAsync(email) == null)
            {
                var user = new IdentityUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(user, password);
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, role);
                }
            }
        }
    }
}
