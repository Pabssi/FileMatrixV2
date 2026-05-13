using System.ComponentModel.DataAnnotations;

namespace FileMatrix_Pabiran_.Areas.SuperAdmin.Models
{
    /// <summary>
    /// Form payload for inviting a new platform Super Administrator.
    /// </summary>
    public class CreateSuperAdminViewModel
    {
        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [Display(Name = "Work email")]
        public string Email { get; set; } = string.Empty;

        [Display(Name = "Display name (optional)")]
        [StringLength(120)]
        public string? DisplayName { get; set; }

        /// <summary>Filled after email PIN confirmation in the modal (server validates against session).</summary>
        [Required(ErrorMessage = "Confirm with the verification code sent to your email.")]
        [StringLength(12, MinimumLength = 4)]
        [Display(Name = "Verification code")]
        public string? ActionConfirmationPin { get; set; }
    }
}
