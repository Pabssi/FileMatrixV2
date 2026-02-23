using System;
using System.Collections.Generic;

namespace FileMatrix_Pabiran_.Areas.Admin.Models
{
    public class UserManagementViewModel
    {
        public List<WorkplaceMemberItemViewModel> Members { get; set; } = new();
        public int WorkplaceID { get; set; }
        public string WorkplaceName { get; set; } = "";
    }

    public class WorkplaceMemberItemViewModel
    {
        public int UserID { get; set; }
        public string Username { get; set; } = "";
        public string Email { get; set; } = "";
        public string RoleName { get; set; } = "Viewer";
        public DateTime JoinedAt { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
