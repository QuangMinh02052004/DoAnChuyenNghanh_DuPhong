using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    public class Message
    {
        public int Id { get; set; }
        public string SenderId { get; set; } // ID người gửi (user hoặc admin)
        public string SenderName { get; set; } // Tên người gửi
        public string ReceiverId { get; set; } // ID người nhận (admin hoặc user)
        public string Content { get; set; } // Nội dung của tin nhắn
        public DateTime CreatedAt { get; set; }
        public bool IsRead { get; set; } // Trạng thái đọc
        public ApplicationUser Sender { get; set; }
        public ApplicationUser Receiver { get; set; }
    }
}