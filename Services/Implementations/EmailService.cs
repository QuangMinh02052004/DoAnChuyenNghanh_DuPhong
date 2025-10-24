using Bloomie.Services.Interfaces;
using System.Net.Mail;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Bloomie.Services.Implementations
{
    public class EmailService : IEmailService
    {
        public Task SendEmailAsync(string email, string subject, string message)
        {
            // Cấu hình SMTP client cho Gmail
            var client = new SmtpClient("smtp.gmail.com", 587)
            {
                EnableSsl = true, // Kích hoạt SSL để bảo mật
                UseDefaultCredentials = false, // Không dùng thông tin đăng nhập mặc định
                Credentials = new NetworkCredential("duykhoa852004@gmail.com", "mnll swnm psnj irrf")
            };

            // Tạo email message
            var mailMessage = new MailMessage
            {
                From = new MailAddress("duykhoa852004@gmail.com", "BLOOMIESHOP"),
                Subject = subject,
                Body = message,
                IsBodyHtml = true,
            };
            mailMessage.To.Add(email);

            return client.SendMailAsync(mailMessage);
        }
    }
}