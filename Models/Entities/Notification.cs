using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    public class Notification
    {
        public int Id { get; set; }
        public string Title { get; set; } // Tiêu đề thông báo
        public string Message { get; set; } // Nội dung
        public DateTime CreatedAt { get; set; }
        public bool IsRead { get; set; }
        public string UserId { get; set; } // ID của admin nhận thông báo
        public string Link { get; set; } // Liên kết đến trang chi tiết
        public ApplicationUser User { get; set; }
    }
}