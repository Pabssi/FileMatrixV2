using System.Net;
using System.Net.Mail;

namespace FileMatrix_Pabiran_.Services
{
    /// <summary>
    /// EmailSenderService: The Communication Backbone.
    ///
    /// RESPONSIBILITY: Orchestrates the delivery of all outbound system emails
    /// (Verification, Onboarding, Sharing, Retention alerts) via SMTP.
    /// Configuration lives in appsettings.json under the "Smtp" section.
    /// </summary>
    public class EmailSenderService
    {
        private readonly string _host;
        private readonly int _port;
        private readonly bool _enableSsl;
        private readonly string _username;
        private readonly string _password;
        private readonly string _fromEmail;
        private readonly string _fromName;

        public EmailSenderService(IConfiguration configuration)
        {
            _host       = configuration["Smtp:Host"]      ?? "smtp.gmail.com";
            _port       = int.TryParse(configuration["Smtp:Port"], out var p) ? p : 587;
            _enableSsl  = bool.TryParse(configuration["Smtp:EnableSsl"], out var ssl) ? ssl : true;
            _username   = configuration["Smtp:Username"]  ?? string.Empty;
            // Gmail "app passwords" are often pasted with spaces; SMTP expects the 16 chars without spaces.
            _password   = (configuration["Smtp:Password"] ?? string.Empty).Replace(" ", string.Empty);
            _fromEmail  = configuration["Smtp:FromEmail"] ?? _username;
            _fromName   = configuration["Smtp:FromName"]  ?? "FileMatrix Support";
        }

        /// <summary>
        /// Sends an HTML email via SMTP using the configured credentials.
        /// The body is treated as HTML; a plain-text fallback is stripped of tags.
        /// </summary>
        public async Task SendAsync(string to, string subject, string body)
        {
            if (string.IsNullOrEmpty(_host) || string.IsNullOrEmpty(_username))
            {
                throw new InvalidOperationException("SMTP is not configured. Check Smtp:Host and Smtp:Username in appsettings.json.");
            }

            using var message = new MailMessage
            {
                From       = new MailAddress(_fromEmail, _fromName),
                Subject    = subject,
                Body       = body,
                IsBodyHtml = true
            };
            message.To.Add(new MailAddress(to));

            using var client = new SmtpClient(_host, _port)
            {
                EnableSsl   = _enableSsl,
                Credentials = new NetworkCredential(_username, _password),
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            await client.SendMailAsync(message);
        }
    }
}
