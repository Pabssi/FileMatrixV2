// FileMatrix: The System Bootstrap & Self-Healing Pipeline.
//
// STRATEGY: 
// 1. Dependency Injection: Registering core DMS services (Document, Email, Retention).
// 2. Identity Bridge: Configuring Cookie redirections to the Landing page.
// 3. Routing Unification: Mapping legacy link structures to new Area-based controllers.
// 4. Infrastructure-as-Startup: Running raw SQL backfills to ensure schema consistency 
//    without requiring manual migrations in legacy environments.
using FileMatrix_Pabiran_.Data;
using FileMatrix_Pabiran_.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Use default Identity with int keys to match the existing database
builder.Services.AddDefaultIdentity<IdentityUser<int>>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
    // Lockout: lock account for 10 minutes after 5 consecutive failed password attempts
    options.Lockout.DefaultLockoutTimeSpan  = TimeSpan.FromMinutes(10);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers      = true;
})
    .AddRoles<IdentityRole<int>>() // Add roles support if needed
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<FileMatrix_Pabiran_.Services.DocumentService>();
builder.Services.AddScoped<FileMatrix_Pabiran_.Services.EmailSenderService>();
builder.Services.AddScoped<FileMatrix_Pabiran_.Services.CloudinaryService>();
builder.Services.AddScoped<FileMatrix_Pabiran_.Services.GoogleDriveService>();
builder.Services.Configure<FileMatrix_Pabiran_.Models.CloudinarySettings>(builder.Configuration.GetSection("CloudinarySettings"));
builder.Services.AddHostedService<FileMatrix_Pabiran_.Services.RetentionWorker>();

// Session: used for staged sign-in when both email 2FA and app MFA are enabled (password → email code → TOTP).
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
});

// Configure Identity token lifespan (e.g. for Forgot Password)
builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
{
    options.TokenLifespan = TimeSpan.FromHours(2);
});

// Configure Identity to redirect to the landing page for unauthorized requests
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/";
    options.AccessDeniedPath = "/";
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

// LEGACY UNIFICATION: Redirects for old link structures. 
// Ensures that hardcoded links in external systems or older emails still 
// land on the correct secure Admin routes.
app.MapGet("/Documents/Details/{id}", (int id, string? token) => 
    Results.Redirect($"/Admin/Documents/Details/{id}{(string.IsNullOrEmpty(token) ? "" : $"?token={token}")}", true));
app.MapGet("/Documents", () => Results.Redirect("/Admin/Admin/Dashboard", true));
app.MapGet("/Documents/Index", () => Results.Redirect("/Admin/Admin/Dashboard", true));

app.MapStaticAssets();

// Area routes must be registered before the default route so area controllers
// (like Areas/Admin/Controllers/AdminController) can be matched by URLs like
// "/Admin/Admin". Register a general area route first.
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

app.MapControllers();

// INFRASTRUCTURE-AS-STARTUP: Self-Healing Logic.
// This block ensures the database schema matches the expected application state 
// and backfills missing metadata for legacy data.
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<FileMatrix_Pabiran_.Data.ApplicationDbContext>();
        // Ensure legacy Users table has Identity-normalized columns used by UserManager lookups.
        try
        {
            // --- ATOMIC GOOGLE DRIVE UPDATES ---
            db.Database.ExecuteSqlRaw("IF COL_LENGTH('Workplaces','GoogleDriveAccessToken') IS NULL ALTER TABLE [Workplaces] ADD [GoogleDriveAccessToken] NVARCHAR(MAX) NULL;");
            db.Database.ExecuteSqlRaw("IF COL_LENGTH('Workplaces','GoogleDriveRefreshToken') IS NULL ALTER TABLE [Workplaces] ADD [GoogleDriveRefreshToken] NVARCHAR(MAX) NULL;");
            db.Database.ExecuteSqlRaw("IF COL_LENGTH('Workplaces','GoogleDriveTokenExpiry') IS NULL ALTER TABLE [Workplaces] ADD [GoogleDriveTokenExpiry] DATETIME2 NULL;");
            db.Database.ExecuteSqlRaw("IF COL_LENGTH('Workplaces','GoogleBackupFolderID') IS NULL ALTER TABLE [Workplaces] ADD [GoogleBackupFolderID] NVARCHAR(100) NULL;");
            db.Database.ExecuteSqlRaw("IF COL_LENGTH('Documents','GoogleDriveFileID') IS NULL ALTER TABLE [Documents] ADD [GoogleDriveFileID] NVARCHAR(100) NULL;");
            db.Database.ExecuteSqlRaw("IF COL_LENGTH('Documents','GoogleDriveLink') IS NULL ALTER TABLE [Documents] ADD [GoogleDriveLink] NVARCHAR(MAX) NULL;");
            
            // --- DOCUMENT VERSION EXTENSIONS ---
            db.Database.ExecuteSqlRaw("IF COL_LENGTH('DocumentVersions','ExternalPublicID') IS NULL ALTER TABLE [DocumentVersions] ADD [ExternalPublicID] NVARCHAR(255) NULL;");
            db.Database.ExecuteSqlRaw("IF COL_LENGTH('DocumentVersions','RestoredFromID') IS NULL ALTER TABLE [DocumentVersions] ADD [RestoredFromID] INT NULL;");

            // Sign-in audit rows use WorkplaceID = NULL (platform scope). Legacy DBs had NOT NULL here, which blocked every insert.
            try
            {
                db.Database.ExecuteSqlRaw(@"
IF EXISTS (SELECT 1 FROM sys.tables t WHERE t.name = N'AuditLogs' AND t.schema_id = SCHEMA_ID(N'dbo'))
 AND EXISTS (
    SELECT 1 FROM sys.columns c
    INNER JOIN sys.tables t ON c.object_id = t.object_id
    WHERE t.name = N'AuditLogs' AND c.name = N'WorkplaceID' AND c.is_nullable = 0)
BEGIN
    ALTER TABLE [dbo].[AuditLogs] ALTER COLUMN [WorkplaceID] INT NULL;
END
");
            }
            catch { /* column may already be nullable or ALTER not permitted */ }

            // --- BATCH UPDATES ---
            db.Database.ExecuteSqlRaw(@"
IF COL_LENGTH('Users','NormalizedUserName') IS NULL
BEGIN
    ALTER TABLE [Users] ADD [NormalizedUserName] NVARCHAR(256) NULL;
END

IF COL_LENGTH('Users','NormalizedEmail') IS NULL
BEGIN
    ALTER TABLE [Users] ADD [NormalizedEmail] NVARCHAR(256) NULL;
END

UPDATE [Users]
SET [NormalizedUserName] = UPPER([Username])
WHERE [NormalizedUserName] IS NULL OR [NormalizedUserName] = '';

UPDATE [Users]
SET [NormalizedEmail] = UPPER([Email])
WHERE [NormalizedEmail] IS NULL OR [NormalizedEmail] = '';

IF OBJECT_ID('SystemSettings', 'U') IS NULL
BEGIN
    CREATE TABLE [SystemSettings] (
        [ID] INT IDENTITY(1,1) NOT NULL,
        [Key] NVARCHAR(100) NOT NULL,
        [Value] NVARCHAR(MAX) NOT NULL,
        [Description] NVARCHAR(MAX) NULL,
        [LastUpdated] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_SystemSettings] PRIMARY KEY ([ID])
    );
    CREATE UNIQUE INDEX [IX_SystemSettings_Key] ON [SystemSettings] ([Key]);
END

IF COL_LENGTH('Documents','IsFavorite') IS NULL
BEGIN
    ALTER TABLE [Documents] ADD [IsFavorite] BIT NOT NULL DEFAULT 0;
END

IF COL_LENGTH('Documents','Status') IS NULL
BEGIN
    ALTER TABLE [Documents] ADD [Status] NVARCHAR(20) NOT NULL DEFAULT 'Published';
END

IF COL_LENGTH('Documents','ArchivedAt') IS NULL
BEGIN
    ALTER TABLE [Documents] ADD [ArchivedAt] DATETIME2 NULL;
END

IF OBJECT_ID('DocumentComments', 'U') IS NULL
BEGIN
    CREATE TABLE [DocumentComments] (
        [CommentID] INT IDENTITY(1,1) NOT NULL,
        [DocumentID] INT NOT NULL,
        [UserID] INT NOT NULL,
        [Text] NVARCHAR(MAX) NOT NULL,
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_DocumentComments] PRIMARY KEY ([CommentID]),
        CONSTRAINT [FK_DocumentComments_Documents] FOREIGN KEY ([DocumentID]) REFERENCES [Documents] ([DocumentID]) ON DELETE CASCADE,
        CONSTRAINT [FK_DocumentComments_Users] FOREIGN KEY ([UserID]) REFERENCES [Users] ([UserID])
    );
END

IF OBJECT_ID('RetentionPolicies', 'U') IS NULL
BEGIN
    CREATE TABLE [RetentionPolicies] (
        [ID] INT IDENTITY(1,1) NOT NULL,
        [WorkplaceID] INT NOT NULL,
        [AutoArchiveAfterDays] INT NULL,
        [AutoDeleteAfterDays] INT NULL,
        [IsEnabled] BIT NOT NULL DEFAULT 1,
        [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        CONSTRAINT [PK_RetentionPolicies] PRIMARY KEY ([ID])
    );
END

IF OBJECT_ID('SystemInfrastructureTasks', 'U') IS NULL
BEGIN
    CREATE TABLE [SystemInfrastructureTasks] (
        [ID] INT IDENTITY(1,1) NOT NULL,
        [Key] NVARCHAR(100) NOT NULL,
        [Name] NVARCHAR(200) NOT NULL,
        [Description] NVARCHAR(MAX) NULL,
        [Status] NVARCHAR(50) NOT NULL DEFAULT 'Pending',
        [LastRun] DATETIME2 NULL,
        [LastResult] NVARCHAR(MAX) NULL,
        CONSTRAINT [PK_SystemInfrastructureTasks] PRIMARY KEY ([ID])
    );
    CREATE UNIQUE INDEX [IX_SystemInfrastructureTasks_Key] ON [SystemInfrastructureTasks] ([Key]);
END

IF COL_LENGTH('WorkplaceInvitations','UsageLimit') IS NULL
BEGIN
    ALTER TABLE [WorkplaceInvitations] ADD [UsageLimit] INT NULL;
END

IF COL_LENGTH('WorkplaceInvitations','UsageCount') IS NULL
BEGIN
    ALTER TABLE [WorkplaceInvitations] ADD [UsageCount] INT NOT NULL DEFAULT 0;
END

-- Ensure Email column is nullable
ALTER TABLE [WorkplaceInvitations] ALTER COLUMN [Email] NVARCHAR(256) NULL;

IF COL_LENGTH('Workplaces','IntegrationApiKey') IS NULL
BEGIN
    ALTER TABLE [Workplaces] ADD [IntegrationApiKey] NVARCHAR(100) NULL;
END

IF COL_LENGTH('Documents','ExternalRefID') IS NULL
BEGIN
    ALTER TABLE [Documents] ADD [ExternalRefID] NVARCHAR(100) NULL;
END

IF COL_LENGTH('Documents','BusinessEntityType') IS NULL
BEGIN
    ALTER TABLE [Documents] ADD [BusinessEntityType] NVARCHAR(100) NULL;
END

IF COL_LENGTH('Documents','BusinessEntityID') IS NULL
BEGIN
    ALTER TABLE [Documents] ADD [BusinessEntityID] INT NULL;
END

-- Change default PublicAccessLevel to 'Restricted' and backfill existing documents
-- 1. Remove old default constraint if it exists (usually it has a random name, we try to find it or just run the change)
DECLARE @default_name nvarchar(255);
SELECT @default_name = name 
FROM sys.default_constraints 
WHERE parent_object_id = OBJECT_ID('[Documents]') 
AND parent_column_id = COLUMNPROPERTY(OBJECT_ID('[Documents]'), 'PublicAccessLevel', 'ColumnId');

IF @default_name IS NOT NULL
BEGIN
    EXEC('ALTER TABLE [Documents] DROP CONSTRAINT ' + @default_name);
END

-- 2. Add new default
ALTER TABLE [Documents] ADD CONSTRAINT DF_Documents_PublicAccessLevel DEFAULT 'Restricted' FOR [PublicAccessLevel];

-- 3. Update existing 'Viewer' documents to 'Restricted' to ensure secure-by-default posture
UPDATE [Documents] SET [PublicAccessLevel] = 'Restricted' WHERE [PublicAccessLevel] = 'Viewer' OR [PublicAccessLevel] IS NULL;
");
        }
        catch { /* infrastructure backfills skipped if error, continue to seed */ }

         // Initialize roles and default admin
        try 
        {
            await DbInitializer.InitializeAsync(scope.ServiceProvider);
        }
        catch (Exception ex)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "An error occurred while seeding the database.");
        }

        // === Backfill: ensure organizations have a CreatedByUserID where possible ===
            try
            {
                // Find workplaces without a recorded creator
                var orphanOrgs = db.Workplaces.Where(o => o.CreatedByUserID == null).ToList();
                foreach (var org in orphanOrgs)
                {
                    int? resolvedCreator = null;

                    // Prefer an audit log entry indicating who created the workplace
                    try
                    {
                        var audit = db.AuditLogs
                            .Where(a => a.WorkplaceID == org.WorkplaceID && a.UserID != null && a.Action != null && a.Action.ToLower().Contains("create"))
                            .OrderBy(a => a.PerformedAt)
                            .FirstOrDefault();
                        if (audit != null)
                        {
                            resolvedCreator = audit.UserID;
                        }
                    }
                    catch { /* ignore auditing lookup errors */ }

                    // If no audit info, fall back to earliest workplace member if present
                    if (resolvedCreator == null)
                    {
                        try
                        {
                            var member = db.WorkplaceMembers
                                .Where(m => m.WorkplaceID == org.WorkplaceID)
                                .OrderBy(m => m.JoinedAt)
                                .FirstOrDefault();
                            if (member != null)
                            {
                                resolvedCreator = member.UserID;
                            }
                        }
                        catch { /* ignore */ }
                    }

                    if (resolvedCreator != null)
                    {
                        org.CreatedByUserID = resolvedCreator;
                        // ensure the creator is present as a WorkplaceMember with owner role
                        try
                        {
                            var exists = db.WorkplaceMembers.Any(m => m.WorkplaceID == org.WorkplaceID && m.UserID == resolvedCreator);
                            if (!exists)
                            {
                                db.WorkplaceMembers.Add(new WorkplaceMember
                                {
                                    WorkplaceID = org.WorkplaceID,
                                    UserID = resolvedCreator.Value,
                                    RoleID = 1, // 1 = Admin/Owner
                                    JoinedAt = DateTime.UtcNow
                                });
                            }
                        }
                        catch { }
                    }
                }

                // Persist any changes from backfill
                try
                {
                    db.SaveChanges();
                }
                catch { /* swallow to avoid blocking startup */ }
            }
        catch { }
    }
    catch
    {
        // Do not stop app startup; failures here will show up in logs and can be addressed
        // via a proper EF migration. Swallow exceptions to avoid crashing the app during dev.
    }
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FileMatrix_Pabiran_.Data.ApplicationDbContext>();
    try
    {
        var count = db.AuditLogs.Count();
        Console.WriteLine("AUDIT LOGS COUNT: " + count);
    }
    catch (Exception ex)
    {
        Console.WriteLine("AUDIT LOGS ERROR: " + ex.Message);
    }
}

app.Run();
