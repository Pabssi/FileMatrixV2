using SendGrid;
using SendGrid.Helpers.Mail;

namespace FileMatrix_Pabiran_.Services
{
    public class EmailSenderService
    {
        private readonly string _apiKey;
        private readonly string _fromEmail;
        private readonly string _fromName;

        public EmailSenderService(IConfiguration configuration)
        {
            _apiKey = configuration["SendGrid:ApiKey"] ?? string.Empty;
            _fromEmail = configuration["SendGrid:FromEmail"] ?? string.Empty;
            _fromName = configuration["SendGrid:FromName"] ?? "FileMatrix Support";
        }

        public async Task SendAsync(string to, string subject, string body)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                throw new InvalidOperationException("SendGrid API Key is not configured.");
            }

            var client = new SendGridClient(_apiKey);
            var from = new EmailAddress(_fromEmail, _fromName);
            var toEmail = new EmailAddress(to);
            
            // Create the email message (using body for both plain text and html for simplicity)
            var msg = MailHelper.CreateSingleEmail(from, toEmail, subject, body, body);
            
            // Send the email
            var response = await client.SendEmailAsync(msg);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Body.ReadAsStringAsync();
                throw new Exception($"SendGrid Error ({response.StatusCode}): {error}");
            }
        }
    }
}
