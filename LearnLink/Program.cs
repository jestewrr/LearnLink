using LearnLink.Data;
using LearnLink.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using CloudinaryDotNet;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Configure EF Core + Identity with Roles
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null)));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Home/Login";
    options.AccessDeniedPath = "/Home/AccessDenied";
});

builder.Services.AddRazorPages();

// Register Cloudinary
var cloudinaryConfig = builder.Configuration.GetSection("Cloudinary");
var cloudinary = new Cloudinary(new Account(
    cloudinaryConfig["CloudName"],
    cloudinaryConfig["ApiKey"],
    cloudinaryConfig["ApiSecret"]));
cloudinary.Api.Secure = true;
builder.Services.AddSingleton(cloudinary);

var app = builder.Build();

// Auto-apply migrations and seed data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();

        // Apply any pending migrations (creates DB if it doesn't exist)
        try 
        {
            // Manually ensure the RejectionReason column exists to fix the immediate crash
            await context.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Resources') AND name = 'RejectionReason')
                BEGIN
                    ALTER TABLE [Resources] ADD [RejectionReason] nvarchar(500) NULL;
                END
                
                -- Fix Quarter column length for 'All Quarters' (Error 2714/TRUNCATED)
                ALTER TABLE [Resources] ALTER COLUMN [Quarter] nvarchar(50) NOT NULL;
            ");

            // Manually ensure the Notifications table exists
            await context.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Notifications')
                BEGIN
                    CREATE TABLE [Notifications] (
                        [NotificationId] int NOT NULL IDENTITY(1,1),
                        [UserId] nvarchar(450) NOT NULL,
                        [Title] nvarchar(100) NOT NULL,
                        [Message] nvarchar(500) NOT NULL,
                        [Type] nvarchar(30) NOT NULL,
                        [Icon] nvarchar(50) NOT NULL,
                        [IconBg] nvarchar(20) NOT NULL,
                        [Link] nvarchar(500) NULL,
                        [ResourceId] int NULL,
                        [IsRead] bit NOT NULL,
                        [CreatedAt] datetime2 NOT NULL,
                        CONSTRAINT [PK_Notifications] PRIMARY KEY ([NotificationId]),
                        CONSTRAINT [FK_Notifications_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_Notifications_Resources_ResourceId] FOREIGN KEY ([ResourceId]) REFERENCES [Resources] ([ResourceId])
                    );
                END
            ");
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogWarning(ex, "Manual SQL fix failed (this is expected if tables are missing), continuing to MigrateAsync...");
        }

        await context.Database.MigrateAsync();

        await SeedData.InitializeAsync(services);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating/seeding the database.");
    }
}

// Configure the HTTP request pipeline.
app.UseDeveloperExceptionPage(); // Temporarily enabled to show detailed errors in production

if (!app.Environment.IsDevelopment())
{
    // app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();


app.Run();
