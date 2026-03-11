using LearnLink.Data;
using LearnLink.Models;
using LearnLink.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Multi-tenancy: School context service
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ISchoolContext, HttpSchoolContext>();

// Session support (for SuperAdmin school switcher)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

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
.AddDefaultTokenProviders()
.AddClaimsPrincipalFactory<SchoolClaimsPrincipalFactory>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Home/Login";
    options.AccessDeniedPath = "/Home/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.Cookie.MaxAge = options.ExpireTimeSpan;
    options.SlidingExpiration = true;
});

// Google Authentication
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
bool googleAuthEnabled = !string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret);
if (googleAuthEnabled)
{
    builder.Services.AddAuthentication()
        .AddGoogle(options =>
        {
            options.ClientId = googleClientId!;
            options.ClientSecret = googleClientSecret!;
            options.CallbackPath = "/signin-google";
        });
}
builder.Services.AddSingleton(new GoogleAuthFlag { IsEnabled = googleAuthEnabled });
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddScoped<IEmailService, SmtpEmailService>();

// Storage Configuration
var storageProvider = builder.Configuration["Storage:Provider"] ?? "Local";
builder.Services.AddHttpContextAccessor(); // Needed for LocalStorageService to build URLs

if (storageProvider == "GoogleDrive")
{
    builder.Services.Configure<GoogleDriveOptions>(builder.Configuration.GetSection("GoogleDrive"));
    builder.Services.AddScoped<IStorageService, GoogleDriveStorageService>();
}
else
{
    builder.Services.AddScoped<IStorageService, LocalStorageService>();
}

// KNN Recommendation Engine
builder.Services.AddScoped<IRecommendationService, KnnRecommendationService>();

builder.Services.AddRazorPages();
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

            // Manually ensure the policy columns exist on the Resources table
            await context.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Resources') AND name = 'AccessLevel')
                    ALTER TABLE [Resources] ADD [AccessLevel] nvarchar(20) NOT NULL DEFAULT 'Registered';
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Resources') AND name = 'AccessDuration')
                    ALTER TABLE [Resources] ADD [AccessDuration] nvarchar(30) NOT NULL DEFAULT 'Unlimited';
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Resources') AND name = 'AllowDownloads')
                    ALTER TABLE [Resources] ADD [AllowDownloads] bit NOT NULL DEFAULT 1;
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Resources') AND name = 'EnableVersionHistory')
                    ALTER TABLE [Resources] ADD [EnableVersionHistory] bit NOT NULL DEFAULT 1;
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Resources') AND name = 'AllowComments')
                    ALTER TABLE [Resources] ADD [AllowComments] bit NOT NULL DEFAULT 1;
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Resources') AND name = 'AllowRatings')
                    ALTER TABLE [Resources] ADD [AllowRatings] bit NOT NULL DEFAULT 1;
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Resources') AND name = 'ModerateComments')
                    ALTER TABLE [Resources] ADD [ModerateComments] bit NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Resources') AND name = 'RequireVersionNotes')
                    ALTER TABLE [Resources] ADD [RequireVersionNotes] bit NOT NULL DEFAULT 0;
            ");

            // Manually ensure the policy columns exist on the ResourceVersions table
            await context.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ResourceVersions') AND name = 'VersionNotes')
                    ALTER TABLE [ResourceVersions] ADD [VersionNotes] nvarchar(1000) NULL;
                
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ResourceVersions') AND name = 'FileFormat')
                    ALTER TABLE [ResourceVersions] ADD [FileFormat] nvarchar(10) NOT NULL DEFAULT '';

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ResourceVersions') AND name = 'FileSize')
                    ALTER TABLE [ResourceVersions] ADD [FileSize] nvarchar(20) NOT NULL DEFAULT '';

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ResourceVersions') AND name = 'Title')
                    ALTER TABLE [ResourceVersions] ADD [Title] nvarchar(100) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ResourceVersions') AND name = 'Description')
                    ALTER TABLE [ResourceVersions] ADD [Description] nvarchar(500) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ResourceVersions') AND name = 'Subject')
                    ALTER TABLE [ResourceVersions] ADD [Subject] nvarchar(50) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ResourceVersions') AND name = 'GradeLevel')
                    ALTER TABLE [ResourceVersions] ADD [GradeLevel] nvarchar(30) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ResourceVersions') AND name = 'ResourceType')
                    ALTER TABLE [ResourceVersions] ADD [ResourceType] nvarchar(30) NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('ResourceVersions') AND name = 'Quarter')
                    ALTER TABLE [ResourceVersions] ADD [Quarter] nvarchar(50) NULL;
            ");

            // Manually ensure multi-tenancy tables and columns exist
            // (Fixes SchoolId error when AddMultiSchoolTenancy migration fails due to column conflicts)
            await context.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Schools')
                BEGIN
                    CREATE TABLE [Schools] (
                        [SchoolId] int NOT NULL IDENTITY(1,1),
                        [Name] nvarchar(150) NOT NULL,
                        [Code] nvarchar(20) NOT NULL,
                        [Description] nvarchar(500) NOT NULL DEFAULT '',
                        [Address] nvarchar(500) NOT NULL DEFAULT '',
                        [ContactEmail] nvarchar(100) NOT NULL DEFAULT '',
                        [LogoPath] nvarchar(500) NULL,
                        [IsActive] bit NOT NULL DEFAULT 1,
                        [AllowCrossSchoolSharing] bit NOT NULL DEFAULT 0,
                        [DateCreated] datetime2 NOT NULL DEFAULT GETDATE(),
                        CONSTRAINT [PK_Schools] PRIMARY KEY ([SchoolId])
                    );
                    CREATE UNIQUE INDEX [IX_Schools_Code] ON [Schools] ([Code]);
                END
            ");

            await context.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'SchoolSettings')
                BEGIN
                    CREATE TABLE [SchoolSettings] (
                        [Id] int NOT NULL IDENTITY(1,1),
                        [SchoolId] int NOT NULL,
                        [InstitutionName] nvarchar(150) NOT NULL DEFAULT '',
                        [AdminEmail] nvarchar(100) NOT NULL DEFAULT '',
                        [TimeZone] nvarchar(50) NOT NULL DEFAULT 'Asia/Manila',
                        [DateFormat] nvarchar(20) NOT NULL DEFAULT 'MM/dd/yyyy',
                        [Language] nvarchar(20) NOT NULL DEFAULT 'English',
                        [Subjects] nvarchar(max) NOT NULL DEFAULT '[]',
                        [GradeLevels] nvarchar(max) NOT NULL DEFAULT '[]',
                        [ResourceTypes] nvarchar(max) NOT NULL DEFAULT '[]',
                        [Quarters] nvarchar(max) NOT NULL DEFAULT '[]',
                        CONSTRAINT [PK_SchoolSettings] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_SchoolSettings_Schools_SchoolId] FOREIGN KEY ([SchoolId]) REFERENCES [Schools] ([SchoolId]) ON DELETE CASCADE
                    );
                    CREATE UNIQUE INDEX [IX_SchoolSettings_SchoolId] ON [SchoolSettings] ([SchoolId]);
                END
            ");

            await context.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AspNetUsers') AND name = 'SchoolId')
                    ALTER TABLE [AspNetUsers] ADD [SchoolId] int NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Departments') AND name = 'SchoolId')
                    ALTER TABLE [Departments] ADD [SchoolId] int NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Resources') AND name = 'SchoolId')
                    ALTER TABLE [Resources] ADD [SchoolId] int NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Resources') AND name = 'IsSharedCrossSchool')
                    ALTER TABLE [Resources] ADD [IsSharedCrossSchool] bit NOT NULL DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Discussions') AND name = 'SchoolId')
                    ALTER TABLE [Discussions] ADD [SchoolId] int NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('LessonsLearned') AND name = 'SchoolId')
                    ALTER TABLE [LessonsLearned] ADD [SchoolId] int NULL;
            ");

            // Ensure optional MiddleName column exists on AspNetUsers (fixes deployments with older schemas)
            await context.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('AspNetUsers') AND name = 'MiddleName')
                    ALTER TABLE [AspNetUsers] ADD [MiddleName] nvarchar(100) NULL;
                    
                -- Fix missing AccessExpiresAt column
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Resources') AND name = 'AccessExpiresAt')
                    ALTER TABLE [Resources] ADD [AccessExpiresAt] datetime2 NULL;

                -- Fix missing PendingReviewPreviewedAt column
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Resources') AND name = 'PendingReviewPreviewedAt')
                    ALTER TABLE [Resources] ADD [PendingReviewPreviewedAt] datetime2 NULL;

                -- Fix missing ResourceAccessGrants table
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('ResourceAccessGrants') AND type in ('U'))
                BEGIN
                    CREATE TABLE [ResourceAccessGrants] (
                        [GrantId] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
                        [ResourceId] int NOT NULL,
                        [UserId] nvarchar(450) NOT NULL,
                        [GrantedAt] datetime2 NOT NULL
                    );
                END
                
                -- Fix missing ResourceComments table
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('ResourceComments') AND type in ('U'))
                BEGIN
                    CREATE TABLE [ResourceComments] (
                        [CommentId] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
                        [ResourceId] int NOT NULL,
                        [UserId] nvarchar(450) NOT NULL,
                        [Content] nvarchar(2000) NOT NULL,
                        [DatePosted] datetime2 NOT NULL DEFAULT GETDATE(),
                        [DateUpdated] datetime2 NULL,
                        [ParentCommentId] int NULL,
                        [LikeCount] int NOT NULL DEFAULT 0,
                        CONSTRAINT [FK_ResourceComments_Resources_ResourceId] FOREIGN KEY ([ResourceId]) REFERENCES [Resources] ([ResourceId]) ON DELETE CASCADE,
                        CONSTRAINT [FK_ResourceComments_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_ResourceComments_ResourceComments_ParentCommentId] FOREIGN KEY ([ParentCommentId]) REFERENCES [ResourceComments] ([CommentId])
                    );
                END
                ELSE
                BEGIN
                    -- Fix missing columns in ResourceComments if table exists but columns are missing
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('ResourceComments') AND name = 'DateUpdated')
                        ALTER TABLE [ResourceComments] ADD [DateUpdated] datetime2 NULL;
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('ResourceComments') AND name = 'LikeCount')
                        ALTER TABLE [ResourceComments] ADD [LikeCount] int NOT NULL DEFAULT 0;
                END

                -- Fix missing Likes table
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('Likes') AND type in ('U'))
                BEGIN
                    CREATE TABLE [Likes] (
                        [LikeId] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
                        [UserId] nvarchar(450) NOT NULL,
                        [TargetType] nvarchar(20) NOT NULL,
                        [TargetId] int NOT NULL,
                        [CreatedAt] datetime2 NOT NULL DEFAULT GETDATE(),
                        CONSTRAINT [FK_Likes_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_Likes_UserId] ON [Likes] ([UserId]);
                END

                -- Fix missing LessonComments table
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('LessonComments') AND type in ('U'))
                BEGIN
                    CREATE TABLE [LessonComments] (
                        [LessonCommentId] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
                        [LessonId] int NOT NULL,
                        [UserId] nvarchar(450) NOT NULL,
                        [Content] nvarchar(2000) NOT NULL,
                        [DatePosted] datetime2 NOT NULL DEFAULT GETDATE(),
                        CONSTRAINT [FK_LessonComments_LessonsLearned_LessonId] FOREIGN KEY ([LessonId]) REFERENCES [LessonsLearned] ([LessonId]) ON DELETE CASCADE,
                        CONSTRAINT [FK_LessonComments_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id])
                    );
                    CREATE INDEX [IX_LessonComments_LessonId] ON [LessonComments] ([LessonId]);
                    CREATE INDEX [IX_LessonComments_UserId] ON [LessonComments] ([UserId]);
                END

                -- Fix missing AccountDeletionFeedbacks table
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('AccountDeletionFeedbacks') AND type in ('U'))
                BEGIN
                    CREATE TABLE [AccountDeletionFeedbacks] (
                        [FeedbackId] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
                        [Reason] nvarchar(500) NOT NULL,
                        [Feedback] nvarchar(2000) NULL,
                        [UserEmail] nvarchar(100) NULL,
                        [UserName] nvarchar(100) NULL,
                        [UserRole] nvarchar(50) NULL,
                        [DeletedAt] datetime2 NOT NULL DEFAULT GETDATE()
                    );
                END

                -- Fix missing ResourceVersions table
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('ResourceVersions') AND type in ('U'))
                BEGIN
                    CREATE TABLE [ResourceVersions] (
                        [VersionId] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
                        [ResourceId] int NOT NULL,
                        [VersionNumber] nvarchar(50) NOT NULL,
                        [VersionNotes] nvarchar(1000) NULL,
                        [FilePath] nvarchar(500) NOT NULL,
                        [FileFormat] nvarchar(10) NOT NULL,
                        [FileSize] nvarchar(20) NOT NULL,
                        [DateUpdated] datetime2 NOT NULL DEFAULT GETDATE(),
                        CONSTRAINT [FK_ResourceVersions_Resources_ResourceId] FOREIGN KEY ([ResourceId]) REFERENCES [Resources] ([ResourceId]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_ResourceVersions_ResourceId] ON [ResourceVersions] ([ResourceId]);
                END

                -- Fix missing Tag tables
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('Tags') AND type in ('U'))
                BEGIN
                    CREATE TABLE [Tags] (
                        [TagId] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
                        [TagName] nvarchar(50) NOT NULL
                    );
                END

                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('ResourceTags') AND type in ('U'))
                BEGIN
                    CREATE TABLE [ResourceTags] (
                        [ResourceTagId] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
                        [ResourceId] int NOT NULL,
                        [TagId] int NOT NULL,
                        CONSTRAINT [FK_ResourceTags_Resources_ResourceId] FOREIGN KEY ([ResourceId]) REFERENCES [Resources] ([ResourceId]) ON DELETE CASCADE,
                        CONSTRAINT [FK_ResourceTags_Tags_TagId] FOREIGN KEY ([TagId]) REFERENCES [Tags] ([TagId]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_ResourceTags_ResourceId] ON [ResourceTags] ([ResourceId]);
                END

                -- Fix missing Category tables
                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('ResourceCategories') AND type in ('U'))
                BEGIN
                    CREATE TABLE [ResourceCategories] (
                        [CategoryId] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
                        [CategoryName] nvarchar(50) NOT NULL,
                        [Description] nvarchar(100) NOT NULL DEFAULT ''
                    );
                END

                IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('ResourceCategoryMaps') AND type in ('U'))
                BEGIN
                    CREATE TABLE [ResourceCategoryMaps] (
                        [MapId] int NOT NULL IDENTITY(1,1) PRIMARY KEY,
                        [ResourceId] int NOT NULL,
                        [CategoryId] int NOT NULL,
                        CONSTRAINT [FK_ResourceCategoryMaps_Resources_ResourceId] FOREIGN KEY ([ResourceId]) REFERENCES [Resources] ([ResourceId]) ON DELETE CASCADE,
                        CONSTRAINT [FK_ResourceCategoryMaps_ResourceCategories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [ResourceCategories] ([CategoryId]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_ResourceCategoryMaps_ResourceId] ON [ResourceCategoryMaps] ([ResourceId]);
                END

            ");

            // Add indexes and foreign keys for SchoolId columns (safe: checks for existence)
            await context.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AspNetUsers_SchoolId')
                    CREATE INDEX [IX_AspNetUsers_SchoolId] ON [AspNetUsers] ([SchoolId]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Departments_SchoolId')
                    CREATE INDEX [IX_Departments_SchoolId] ON [Departments] ([SchoolId]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Resources_SchoolId')
                    CREATE INDEX [IX_Resources_SchoolId] ON [Resources] ([SchoolId]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Discussions_SchoolId')
                    CREATE INDEX [IX_Discussions_SchoolId] ON [Discussions] ([SchoolId]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LessonsLearned_SchoolId')
                    CREATE INDEX [IX_LessonsLearned_SchoolId] ON [LessonsLearned] ([SchoolId]);
            ");

            await context.Database.ExecuteSqlRawAsync(@"
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_AspNetUsers_Schools_SchoolId')
                    ALTER TABLE [AspNetUsers] ADD CONSTRAINT [FK_AspNetUsers_Schools_SchoolId] FOREIGN KEY ([SchoolId]) REFERENCES [Schools] ([SchoolId]) ON DELETE SET NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Departments_Schools_SchoolId')
                    ALTER TABLE [Departments] ADD CONSTRAINT [FK_Departments_Schools_SchoolId] FOREIGN KEY ([SchoolId]) REFERENCES [Schools] ([SchoolId]) ON DELETE SET NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Resources_Schools_SchoolId')
                    ALTER TABLE [Resources] ADD CONSTRAINT [FK_Resources_Schools_SchoolId] FOREIGN KEY ([SchoolId]) REFERENCES [Schools] ([SchoolId]) ON DELETE SET NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Discussions_Schools_SchoolId')
                    ALTER TABLE [Discussions] ADD CONSTRAINT [FK_Discussions_Schools_SchoolId] FOREIGN KEY ([SchoolId]) REFERENCES [Schools] ([SchoolId]) ON DELETE SET NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_LessonsLearned_Schools_SchoolId')
                    ALTER TABLE [LessonsLearned] ADD CONSTRAINT [FK_LessonsLearned_Schools_SchoolId] FOREIGN KEY ([SchoolId]) REFERENCES [Schools] ([SchoolId]) ON DELETE SET NULL;
            ");
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogWarning(ex, "Manual SQL fix failed (this is expected if tables are missing), continuing to MigrateAsync...");
        }

        await context.Database.MigrateAsync();

        // Run schema fixes AFTER migration to ensure they override anything in the migration
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                -- Fix Quarter column length for 'All Quarters' (Error 2714/TRUNCATED)
                -- We do this AFTER migration because migration might recreate it as nvarchar(10)
                ALTER TABLE [Resources] ALTER COLUMN [Quarter] nvarchar(50) NOT NULL;

                -- Fix Resource FilePath length for external links imported from Google Books / Open Library.
                ALTER TABLE [Resources] ALTER COLUMN [FilePath] nvarchar(500) NOT NULL;
                ALTER TABLE [ResourceVersions] ALTER COLUMN [FilePath] nvarchar(500) NOT NULL;
                
                -- Also fix VersionNumber in case it truncates
                ALTER TABLE [ResourceVersions] ALTER COLUMN [VersionNumber] nvarchar(50) NOT NULL;
            ");
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogWarning(ex, "Post-migration SQL fix failed.");
        }

        await SeedData.InitializeAsync(services);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating/seeding the database.");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseSession(); // Must be before Authentication for school switcher
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();


app.Run();

public class GoogleAuthFlag { public bool IsEnabled { get; set; } }
