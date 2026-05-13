using System;
using System.Collections.Generic;

namespace FileMatrix_Pabiran_.Models
{
    public class DocumentListViewModel
    {
        public List<DocumentItemViewModel> Documents { get; set; } = new();
        public List<Folder> SubFolders { get; set; } = new();
        public Folder? CurrentFolder { get; set; }
        public int WorkplaceID { get; set; }
        public string WorkplaceName { get; set; } = "";
        public string? SearchQuery { get; set; }
        public int? CategoryID { get; set; }
        public string? StatusFilter { get; set; }
    }

    public class DocumentItemViewModel
    {
        public int DocumentID { get; set; }
        public int? CategoryID { get; set; }
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public string CategoryName { get; set; } = "Uncategorized";
        public string CurrentVersionNumber { get; set; } = "1.0";
        public string FileName { get; set; } = "";
        public string FileSizeFormatted { get; set; } = "0 B";
        public DateTime UpdatedAt { get; set; }
        public string? GoogleDriveFileID { get; set; }
        public string? GoogleDriveLink { get; set; }
        public string UploadedBy { get; set; } = "System";
        public string Author { get; set; } = "Admin User";
        public List<string> Tags { get; set; } = new();
        public bool IsFavorite { get; set; }
        public string Status { get; set; } = "Published"; // Draft, Published
        public string? MimeType { get; set; }
        public string? PublicShareToken { get; set; }
        public string PublicAccessLevel { get; set; } = "Restricted";
        public bool IsImage => MimeType?.StartsWith("image/") == true;
        public bool IsVideo => MimeType?.StartsWith("video/") == true;
        public bool IsPdf => MimeType == "application/pdf";
        public bool IsOfficeDocument => 
            MimeType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document" ||
            MimeType == "application/msword" ||
            MimeType == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" ||
            MimeType == "application/vnd.ms-excel" ||
            MimeType == "application/vnd.openxmlformats-officedocument.presentationml.presentation" ||
            MimeType == "application/vnd.ms-powerpoint";
        public bool IsWord => MimeType == "application/vnd.openxmlformats-officedocument.wordprocessingml.document" || MimeType == "application/msword";
        public bool IsExcel => MimeType == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" || MimeType == "application/vnd.ms-excel";
        public bool IsPowerPoint => MimeType == "application/vnd.openxmlformats-officedocument.presentationml.presentation" || MimeType == "application/vnd.ms-powerpoint";
        public bool IsShared { get; set; }
        public string? SignedUrl { get; set; }
    }

    public class DocumentDetailsViewModel
    {
        public DocumentItemViewModel Document { get; set; } = new();
        public List<DocumentCommentViewModel> Comments { get; set; } = new();
        public int WorkplaceID { get; set; }
        public string WorkplaceName { get; set; } = "";
    }

    public class DocumentCommentViewModel
    {
        public int CommentID { get; set; }
        public string UserDisplayName { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }
}
