using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    public class Rating
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public Product Product { get; set; }
        public string UserId { get; set; }
        public ApplicationUser User { get; set; }
        public int Star { get; set; } // Số sao (1 - 5 sao)
        public string Comment { get; set; } // Bình luận của người dùng
        public DateTime ReviewDate { get; set; } // Ngày đánh giá
        public string ImageUrl { get; set; } 
        public List<Reply> Replies { get; set; } = new List<Reply>(); // Danh sách các phản hồi
        public List<Report> Reports { get; set; } = new List<Report>(); // Danh sách các báo cáo vi phạm
        public int LikesCount { get; set; } = 0; // Số lượt thích, mặc định là 0
        public List<UserLike> UserLikes { get; set; } = new List<UserLike>();
        public bool IsVisible { get; set; } = true; // Ẩn/Hiển thị
        public string LastModifiedBy { get; set; } // Người chỉnh sửa cuối cùng
        public DateTime? LastModifiedDate { get; set; } // Thời gian chỉnh sửa cuối
    }
}
