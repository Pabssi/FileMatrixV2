using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using FileMatrix_Pabiran_.Models;

namespace FileMatrix_Pabiran_.Data
{
    /// <summary>
    /// ApplicationDbContext: The Data Persistence & Relationship Layer.
    /// 
    /// ROLE: This context bridges three distinct domain areas:
    /// 1. ASP.NET Identity (Authentication/Security).
    /// 2. Platform Governance (Global settings/Tasks).
    /// 3. Workplace/Tenant Data (Documents/Members/Permissions).
    /// </summary>
    public class ApplicationDbContext : IdentityDbContext<IdentityUser<int>, IdentityRole<int>, int>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // DbSets (plural names, matching entity classes)
        public new DbSet<User> Users { get; set; }
        public DbSet<Workplace> Workplaces { get; set; }
        public DbSet<WorkplaceMember> WorkplaceMembers { get; set; }
        public DbSet<WorkplaceInvitation> WorkplaceInvitations { get; set; }
        public DbSet<Folder> Folders { get; set; }
        public DbSet<Document> Documents { get; set; }
        public DbSet<DocumentVersion> DocumentVersions { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<DocumentPermission> DocumentPermissions { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<SystemSetting> SystemSettings { get; set; }
        public DbSet<DocumentComment> DocumentComments { get; set; }
        public DbSet<RetentionPolicy> RetentionPolicies { get; set; }
        public DbSet<DocumentShareInvitation> DocumentShareInvitations { get; set; }
        public DbSet<SystemInfrastructureTask> SystemInfrastructureTasks { get; set; }

        /// <summary>
        /// OnModelCreating: Configures the schema, constraints, and relationships using the Fluent API.
        /// </summary>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // === Users (DMS Profile Extension) ===
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("Users");
                entity.HasKey(e => e.UserID);
                entity.Property(e => e.Username).HasMaxLength(100).IsRequired();
                entity.Property(e => e.Email).HasMaxLength(255).IsRequired();
                entity.Property(e => e.PasswordHash).IsRequired();
                entity.Property(e => e.FirstName).HasMaxLength(100);
                entity.Property(e => e.LastName).HasMaxLength(100);
                entity.Property(e => e.DisplayName).HasMaxLength(150);
                entity.Property(e => e.Role).HasDefaultValue(3); // 1 = Admin, 2 = Editor, 3 = Viewer
                entity.Property(e => e.IsActive).HasDefaultValue(true);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.HasIndex(e => e.Username).IsUnique();
                entity.HasIndex(e => e.Email).IsUnique();
            });

            // === Workplaces (Multi-Tenant Containers) ===
            modelBuilder.Entity<Workplace>(entity =>
            {
                entity.ToTable("Workplaces");
                entity.HasKey(e => e.WorkplaceID);
                entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
                entity.Property(e => e.Slug).HasMaxLength(100);
                entity.Property(e => e.LogoURL).HasMaxLength(500);
                entity.Property(e => e.Description);
                entity.Property(e => e.IsActive).HasDefaultValue(true);
                entity.Property(e => e.IntegrationApiKey).HasMaxLength(100);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.HasIndex(e => e.Slug).IsUnique();
                entity.HasOne<User>()
                      .WithMany()
                      .HasForeignKey(e => e.CreatedByUserID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // === WorkplaceMembers ===
            modelBuilder.Entity<WorkplaceMember>(entity =>
            {
                entity.ToTable("WorkplaceMembers");
                entity.HasKey(e => e.WorkplaceMemberID);
                entity.HasIndex(e => new { e.WorkplaceID, e.UserID }).IsUnique();
                entity.Property(e => e.RoleID).HasDefaultValue(3); // 1 = Admin, 2 = Editor, 3 = Viewer
                entity.Property(e => e.JoinedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.HasOne(e => e.Workplace)
                      .WithMany()
                      .HasForeignKey(e => e.WorkplaceID)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserID)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // === WorkplaceInvitations ===
            modelBuilder.Entity<WorkplaceInvitation>(entity =>
            {
                entity.ToTable("WorkplaceInvitations");
                entity.HasKey(e => e.InvitationID);
                entity.Property(e => e.Email).HasMaxLength(255);
                entity.Property(e => e.Token).HasMaxLength(100).IsRequired();
                entity.Property(e => e.Code).HasMaxLength(20);
                entity.Property(e => e.RoleID).HasDefaultValue(3);
                entity.Property(e => e.InvitePassword).HasMaxLength(100);
                entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("pending");
                entity.Property(e => e.UsedAt);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.HasOne<Workplace>()
                      .WithMany()
                      .HasForeignKey(e => e.WorkplaceID)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<User>()
                      .WithMany()
                      .HasForeignKey(e => e.InvitedByUserID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // === Folders ===
            modelBuilder.Entity<Folder>(entity =>
            {
                entity.ToTable("Folders");
                entity.HasKey(e => e.FolderID);
                entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.HasOne<Workplace>()
                      .WithMany()
                      .HasForeignKey(e => e.WorkplaceID)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<Folder>()
                      .WithMany()
                      .HasForeignKey(e => e.ParentFolderID)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<User>()
                      .WithMany()
                      .HasForeignKey(e => e.CreatedByUserID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // === Documents (Core Assets) ===
            modelBuilder.Entity<Document>(entity =>
            {
                entity.ToTable("Documents");
                entity.HasKey(e => e.DocumentID);
                entity.Property(e => e.Title).HasMaxLength(300).IsRequired();
                entity.Property(e => e.Description);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(e => e.IsFavorite).HasDefaultValue(false);
                entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("Published");
                entity.Property(e => e.RetentionNoticeSent).HasDefaultValue(false);
                entity.Property(e => e.ExternalRefID).HasMaxLength(100);
                entity.Property(e => e.BusinessEntityType).HasMaxLength(100);
                entity.HasOne(e => e.Workplace)
                      .WithMany()
                      .HasForeignKey(e => e.WorkplaceID)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Folder)
                      .WithMany()
                      .HasForeignKey(e => e.FolderID)
                      .OnDelete(DeleteBehavior.SetNull);
                entity.HasOne(e => e.Category)
                      .WithMany()
                      .HasForeignKey(e => e.CategoryID)
                      .OnDelete(DeleteBehavior.SetNull);
                entity.HasOne(e => e.CurrentVersion)
                      .WithMany()
                      .HasForeignKey(e => e.CurrentVersionID)
                      .OnDelete(DeleteBehavior.SetNull);
                entity.HasOne(e => e.CreatedBy)
                      .WithMany()
                      .HasForeignKey(e => e.CreatedByUserID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // === DocumentVersions ===
            modelBuilder.Entity<DocumentVersion>(entity =>
            {
                entity.ToTable("DocumentVersions");
                entity.HasKey(e => e.VersionID);
                entity.Property(e => e.VersionNumber).HasColumnType("decimal(10,2)").IsRequired();
                entity.Property(e => e.FileName).HasMaxLength(300).IsRequired();
                entity.Property(e => e.FilePath).HasMaxLength(500).IsRequired();
                entity.Property(e => e.FileSizeBytes).IsRequired();
                entity.Property(e => e.MimeType).HasMaxLength(100).IsRequired();
                entity.Property(e => e.UploadedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(e => e.ChangeNote).HasMaxLength(500);
                entity.HasOne(e => e.Document)
                      .WithMany()
                      .HasForeignKey(e => e.DocumentID)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<User>()
                      .WithMany()
                      .HasForeignKey(e => e.UploadedByUserID)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.RestoredFrom)
                      .WithMany()
                      .HasForeignKey(e => e.RestoredFromID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // === DocumentPermissions ===
            modelBuilder.Entity<DocumentPermission>(entity =>
            {
                entity.ToTable("DocumentPermissions");
                entity.HasKey(e => e.PermissionID);
                entity.Property(e => e.PermissionLevel).HasMaxLength(20).IsRequired();
                entity.HasOne<Document>()
                      .WithMany()
                      .HasForeignKey(e => e.DocumentID)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<User>()
                      .WithMany()
                      .HasForeignKey(e => e.UserID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // === Categories ===
            modelBuilder.Entity<Category>(entity =>
            {
                entity.ToTable("Categories");
                entity.HasKey(e => e.CategoryID);
                entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.HasOne<Workplace>()
                      .WithMany()
                      .HasForeignKey(e => e.WorkplaceID)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // === AuditLogs ===
            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.ToTable("AuditLogs");
                entity.HasKey(e => e.LogID);
                entity.Property(e => e.Action).HasMaxLength(50).IsRequired();
                entity.Property(e => e.EntityType).HasMaxLength(50).IsRequired();
                entity.Property(e => e.PerformedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.HasOne<Workplace>()
                      .WithMany()
                      .HasForeignKey(e => e.WorkplaceID)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // === Notifications ===
            modelBuilder.Entity<Notification>(entity =>
            {
                entity.ToTable("Notifications");
                entity.HasKey(e => e.NotificationID);
                entity.Property(e => e.Type).HasMaxLength(50).IsRequired();
                entity.Property(e => e.Message).IsRequired();
                entity.Property(e => e.IsSent).HasDefaultValue(false);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.HasOne<Workplace>()
                      .WithMany()
                      .HasForeignKey(e => e.WorkplaceID)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<User>()
                      .WithMany()
                      .HasForeignKey(e => e.RecipientUserID)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<Document>()
                      .WithMany()
                      .HasForeignKey(e => e.DocumentID)
                      .OnDelete(DeleteBehavior.SetNull);
            });
            // === SystemSettings ===
            modelBuilder.Entity<SystemSetting>(entity =>
            {
                entity.ToTable("SystemSettings");
                entity.HasKey(e => e.ID);
                entity.Property(e => e.Key).HasMaxLength(100).IsRequired();
                entity.Property(e => e.Value).IsRequired();
                entity.HasIndex(e => e.Key).IsUnique();
            });

            // === DocumentComments ===
            modelBuilder.Entity<DocumentComment>(entity =>
            {
                entity.ToTable("DocumentComments");
                entity.HasKey(e => e.CommentID);
                entity.Property(e => e.Text).IsRequired();
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.HasOne<Document>()
                      .WithMany()
                      .HasForeignKey(e => e.DocumentID)
                      .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<User>()
                      .WithMany()
                      .HasForeignKey(e => e.UserID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            // === Identity Schema Cleanup ===
            // Identity keys (UserId, RoleId, LoginProvider, Name) default to 450 in EF Core,
            // but the early migrations in this project used 128 (default in some older templates).
            // Explicitly set them to 128 to avoid "flapping" and PK primary key alteration errors.
            modelBuilder.Entity<IdentityUserLogin<int>>(entity =>
            {
                entity.Property(e => e.LoginProvider).HasMaxLength(128);
                entity.Property(e => e.ProviderKey).HasMaxLength(128);
            });
            modelBuilder.Entity<IdentityUserToken<int>>(entity =>
            {
                entity.Property(e => e.LoginProvider).HasMaxLength(128);
                entity.Property(e => e.Name).HasMaxLength(128);
            });
            modelBuilder.Entity<IdentityRole<int>>(entity =>
            {
                entity.Property(e => e.Name).HasMaxLength(128);
                entity.Property(e => e.NormalizedName).HasMaxLength(128);
            });
            modelBuilder.Entity<IdentityRoleClaim<int>>(entity => entity.Property(e => e.RoleId));
            modelBuilder.Entity<IdentityUserClaim<int>>(entity => entity.Property(e => e.UserId));
            modelBuilder.Entity<IdentityUserRole<int>>(entity => 
            {
                entity.Property(e => e.UserId);
                entity.Property(e => e.RoleId);
            });
        }
    }
}
