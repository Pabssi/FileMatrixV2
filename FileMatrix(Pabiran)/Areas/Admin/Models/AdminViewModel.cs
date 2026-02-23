using System.Collections.Generic;
using FileMatrix_Pabiran_.Models;

namespace FileMatrix_Pabiran_.Areas.Admin.Models
{
    public class AdminViewModel
    {
        public string Message { get; set; } = "";
        public int TotalDocuments { get; set; }
        public long TotalSizeBytes { get; set; }
        public string TotalSizeFormatted { get; set; } = "0 B";
        public int TotalMembers { get; set; }
        public string WorkplaceName { get; set; } = "";
        public List<DocumentItemViewModel> RecentDocuments { get; set; } = new();
    }
}
