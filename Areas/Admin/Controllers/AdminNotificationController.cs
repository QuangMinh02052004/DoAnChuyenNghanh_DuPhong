using System.Security.Claims;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    [Route("Admin/[controller]/[action]")]
    [ApiController]
    public class AdminNotificationController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AdminNotificationController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetNotifications()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier); // Lấy GUID của người dùng hiện tại
            if (string.IsNullOrEmpty(userId))
            {
                return Ok(new List<object>()); // Trả về mảng rỗng nếu không tìm thấy userId
            }

            // Lấy 10 thông báo mới nhất của người dùng
            var notifications = await _context.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new
                {
                    n.Id,
                    n.Message,
                    n.IsRead,
                    n.Link,
                    CreatedAt = n.CreatedAt.ToString("dd/MM/yyyy HH:mm")
                })
                .Take(10)
                .ToListAsync();

            return Ok(notifications);
        }

        [HttpGet]
        public async Task<IActionResult> GetMessages()
        {
            var userId = User.Identity.Name;
            // Lấy 10 tin nhắn mới nhất của người dùng
            var messages = await _context.Messages
                .Where(m => m.ReceiverId == userId)
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => new
                {
                    m.Id,
                    m.SenderName,
                    m.Content,
                    m.IsRead,
                    CreatedAt = m.CreatedAt.ToString("dd/MM/yyyy HH:mm")
                })
                .Take(10)
                .ToListAsync();

            return Ok(messages);
        }

        [HttpPost("MarkAsRead/{id}")]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            // Tìm thông báo theo ID
            var notification = await _context.Notifications.FindAsync(id);
            if (notification == null) return NotFound();

            // Cập nhật trạng thái IsRead
            notification.IsRead = true;
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost("MarkMessageAsRead/{id}")]
        public async Task<IActionResult> MarkMessageAsRead(int id)
        {
            var message = await _context.Messages.FindAsync(id);
            if (message == null) return NotFound();

            message.IsRead = true;
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpDelete("Delete/{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var notification = await _context.Notifications.FindAsync(id);
            if (notification == null) return NotFound();

            _context.Notifications.Remove(notification);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpDelete("DeleteMessage/{id}")]
        public async Task<IActionResult> DeleteMessage(int id)
        {
            var message = await _context.Messages.FindAsync(id);
            if (message == null) return NotFound();

            _context.Messages.Remove(message);
            await _context.SaveChangesAsync();
            return Ok();
        }
    }
}