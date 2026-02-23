using System;
using System.Collections.Generic;

namespace FileMatrix_Pabiran_.Models
{
    public class Users
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

    public class Workplaces
    {
        public int WorkplaceID { get; set; }
        public string? Name { get; set; }
        public string? Slug { get; set; }
        public int? CreatedByUserID { get; set; }
        public string? LogoURL { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class WorkplaceMembers
    {
        public int WorkplaceMemberID { get; set; }
        public int WorkplaceID { get; set; }
        public int UserID { get; set; }
        public int RoleID { get; set; } = 3; // 1 = Admin, 2 = Editor, 3 = Viewer
        public DateTime JoinedAt { get; set; }
    }

    public class WorkplaceInvitations
    {
        public int InvitationID { get; set; }
        public int WorkplaceID { get; set; }
        public int? InvitedByUserID { get; set; }
        public string? Email { get; set; }
        public string? Token { get; set; }
        public string? Code { get; set; }
        public string? InvitePassword { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? Status { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class Folders
    {
        public int FolderID { get; set; }
        public int WorkplaceID { get; set; }
        public string? Name { get; set; }
        public int? ParentFolderID { get; set; }
        public int? CreatedByUserID { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class Documents
    {
        public int DocumentID { get; set; }
        public int WorkplaceID { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public int? FolderID { get; set; }
        public int? CurrentVersionID { get; set; }
        public int? CreatedByUserID { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? GoogleDriveFileID { get; set; }
        public string? GoogleDriveLink { get; set; }
        public string? BusinessEntityType { get; set; }
        public int? BusinessEntityID { get; set; }
    }

    public class DocumentVersions
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
    }

    public class DocumentPermissions
    {
        public int PermissionID { get; set; }
        public int DocumentID { get; set; }
        public string? RoleName { get; set; }
        public int? UserID { get; set; }
        public string? PermissionLevel { get; set; }
    }

    public class AuditLogs
    {
        public long LogID { get; set; }
        public int WorkplaceID { get; set; }
        public string? Action { get; set; }
        public string? EntityType { get; set; }
        public int EntityID { get; set; }
        public int? UserID { get; set; }
        public DateTime PerformedAt { get; set; }
        public string? Details { get; set; }
        public string? IpAddress { get; set; }
    }

    public class Notifications
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
}
