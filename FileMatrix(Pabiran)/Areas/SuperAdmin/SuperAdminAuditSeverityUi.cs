using FileMatrix_Pabiran_.Models;

namespace FileMatrix_Pabiran_.Areas.SuperAdmin;

/// <summary>
/// Row-level severity labels for Super Admin audit tables (keeps UI aligned with controller filters).
/// </summary>
public static class SuperAdminAuditSeverityUi
{
    public static string CssClassForSecurity(string? action) =>
        TierForSecurity(action) switch
        {
            "critical" => "sa-severity-badge--critical",
            "high" => "sa-severity-badge--high",
            _ => "sa-severity-badge--normal"
        };

    public static string LabelForSecurity(string? action) =>
        TierForSecurity(action) switch
        {
            "critical" => "Critical",
            "high" => "High",
            _ => "Normal"
        };

    public static string CssClassForPlatform(AuditLog l) =>
        TierForPlatform(l) switch
        {
            "critical" => "sa-severity-badge--critical",
            "high" => "sa-severity-badge--high",
            _ => "sa-severity-badge--normal"
        };

    public static string LabelForPlatform(AuditLog l) =>
        TierForPlatform(l) switch
        {
            "critical" => "Critical",
            "high" => "High",
            _ => "Normal"
        };

    private static string TierForSecurity(string? action) =>
        action switch
        {
            "Login Locked" or "Account Locked" or "CAPTCHA Failure" or "MFA Failed" => "critical",
            "Login Failed" => "high",
            _ => "normal"
        };

    private static string TierForPlatform(AuditLog l)
    {
        if (l.EntityType == "Security" && l.Action is "Login Locked" or "Account Locked" or "CAPTCHA Failure" or "MFA Failed")
            return "critical";
        if (l.Action != null)
        {
            if (l.Action.Contains("Auto-Delete", StringComparison.OrdinalIgnoreCase)
                || l.Action == "API Key Revoked"
                || l.Action == "Share Revoked"
                || l.Action == "Invitation Deleted"
                || l.Action == "Folder Deleted")
                return "critical";
        }
        if (l.EntityType == "Security" && l.Action == "Login Failed")
            return "high";
        if (l.Action == "Document Archived" || l.Action == "User Role Changed" || l.Action == "User Status Toggled")
            return "high";
        return "normal";
    }
}
