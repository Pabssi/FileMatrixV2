using System;
using System.Collections.Generic;

namespace FileMatrix_Pabiran_.Models
{
    // Users
    public class User
    {
        public int UserID { get; set; }
        public string? Username { get; set; }
        public string? Email { get; set; }
        public string? PasswordHash { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? DisplayName { get; set; }
        public int Role { get; set; } = 3; // 1 = Admin, 2 = Editor, 3 = Viewer
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLogin { get; set; }
    }

    // Workplaces
    public class Workplace
    {
        public int WorkplaceID { get; set; }
        public string? Name { get; set; }
        public string? Slug { get; set; }
        public int? CreatedByUserID { get; set; }
        public string? LogoURL { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; } = true;
        public string? IntegrationApiKey { get; set; }
    }

    public class WorkplaceMember
    {
        public int WorkplaceMemberID { get; set; }
        public int WorkplaceID { get; set; }
        public int UserID { get; set; }
        public int RoleID { get; set; } = 3; // 1 = Admin, 2 = Editor, 3 = Viewer
        public DateTime JoinedAt { get; set; }
        public virtual Workplace? Workplace { get; set; }
        public virtual User? User { get; set; }
    }

    public class WorkplaceInvitation
    {
        public int InvitationID { get; set; }
        public int WorkplaceID { get; set; }
        public int? InvitedByUserID { get; set; }
        public string? Email { get; set; }
        public string? Token { get; set; }
        public string? Code { get; set; }
        public int RoleID { get; set; } = 3; 
        public string? InvitePassword { get; set; }
        public int? UsageLimit { get; set; }
        public int UsageCount { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? Status { get; set; }
        public DateTime? UsedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // Categories
    public class Category
    {
        public int CategoryID { get; set; }
        public int WorkplaceID { get; set; }
        public string? Name { get; set; }
        public string? Icon { get; set; } // Emoji or CSS class
        public string? Color { get; set; } // Hex or Bootstrap name
        public DateTime CreatedAt { get; set; }
    }

    // Folders
    public class Folder
    {
        public int FolderID { get; set; }
        public int WorkplaceID { get; set; }
        public string? Name { get; set; }
        public int? ParentFolderID { get; set; }
        public int? CreatedByUserID { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // Documents
    public class Document
    {
        public int DocumentID { get; set; }
        public int WorkplaceID { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public int? FolderID { get; set; }
        public int? CategoryID { get; set; }
        public int? CurrentVersionID { get; set; }
        public int? CreatedByUserID { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? GoogleDriveFileID { get; set; }
        public string? GoogleDriveLink { get; set; }
        public string? BusinessEntityType { get; set; }
        public int? BusinessEntityID { get; set; }
        public string? ExternalRefID { get; set; }
        public bool IsFavorite { get; set; }
        public string Status { get; set; } = "Published"; // Draft, Published
        public string? PublicShareToken { get; set; }
        public string PublicAccessLevel { get; set; } = "Viewer"; // Viewer, Editor
        public DateTime? ArchivedAt { get; set; }
        public bool RetentionNoticeSent { get; set; } = false;
        public virtual Workplace? Workplace { get; set; }
        public virtual Folder? Folder { get; set; }
        public virtual Category? Category { get; set; }
        public virtual DocumentVersion? CurrentVersion { get; set; }
        public virtual User? CreatedBy { get; set; }
    }

    public class DocumentVersion
    {
        public int VersionID { get; set; }
        public int DocumentID { get; set; }
        public decimal VersionNumber { get; set; }
        public string? FileName { get; set; }
        public string? FilePath { get; set; }
        public long FileSizeBytes { get; set; }
        public string? MimeType { get; set; }
        public int? UploadedByUserID { get; set; }
        public DateTime UploadedAt { get; set; }
        public string? ChangeNote { get; set; }
        public int? RestoredFromID { get; set; }
        public virtual Document? Document { get; set; }
    }

    public class DocumentPermission
    {
        public int PermissionID { get; set; }
        public int DocumentID { get; set; }
        public string? RoleName { get; set; }
        public int? UserID { get; set; }
        public string? PermissionLevel { get; set; }
    }

    public class AuditLog
    {
        public long LogID { get; set; }
        public int WorkplaceID { get; set; }
        public string? Action { get; set; }
        public string? EntityType { get; set; }
        public int EntityID { get; set; }
        public int? UserID { get; set; }
        public virtual User? User { get; set; }
        public DateTime PerformedAt { get; set; }
        public string? Details { get; set; }
        public string? IpAddress { get; set; }
    }

    public class Notification
    {
        public int NotificationID { get; set; }
        public int WorkplaceID { get; set; }
        public int? RecipientUserID { get; set; }
        public string? Type { get; set; }
        public string? Message { get; set; }
        public int? DocumentID { get; set; }
        public bool IsSent { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
    }

    public class DocumentComment
    {
        public int CommentID { get; set; }
        public int DocumentID { get; set; }
        public int UserID { get; set; }
        public string Text { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class SystemSetting
    {
        public int ID { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class RetentionPolicy
    {
        public int ID { get; set; }
        public int WorkplaceID { get; set; }
        public int? AutoArchiveAfterDays { get; set; }
        public int? AutoDeleteAfterDays { get; set; }
        public bool IsEnabled { get; set; } = true;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Stores pending document share invitations sent to emails that may not yet have accounts.
    /// When the user registers or logs in with this email, the permission is automatically granted.
    /// </summary>
    public class DocumentShareInvitation
    {
        public int Id { get; set; }
        public int DocumentID { get; set; }
        public string Email { get; set; } = string.Empty;
        public string PermissionLevel { get; set; } = "Viewer"; // Viewer, Editor
        public string Token { get; set; } = string.Empty; // unique token for direct link
        public int InvitedByUserID { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsAccepted { get; set; } = false;
        public DateTime? AcceptedAt { get; set; }
    }
}
