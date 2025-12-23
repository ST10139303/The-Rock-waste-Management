using MailKit.Net.Smtp;
using MimeKit;
using System.Threading.Tasks;

namespace TheRockWasteManagement.Services
{
    public class EmailService
    {
        public async Task SendWorkerWelcomeEmail(string toEmail, string workerName)
        {
            var email = new MimeMessage();
            email.From.Add(MailboxAddress.Parse("Mulongoniwashu3@gmail.com")); // Your Gmail
            email.To.Add(MailboxAddress.Parse(toEmail));
            email.Subject = "Welcome to The Rock Waste Management";

            email.Body = new TextPart("plain")
            {
                Text = $"Hi {workerName},\n\nYou’ve been added as a worker in The Rock Waste Management system. Please log in to get started.\n\nRegards,\nAdmin"
            };

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync("smtp.gmail.com", 587, MailKit.Security.SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync("Mulongoniwashu3@gmail.com", "rjlz fzsv gyrx ekio"); // Use Gmail App Password
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);
        }
    }
}
