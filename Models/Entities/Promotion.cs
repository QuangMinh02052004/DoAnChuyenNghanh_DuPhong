namespace Bloomie.Models.Entities
{
    public enum PromotionType
    {
        Product, // Áp dụng cho sản phẩm cụ thể
        Order    // Áp dụng cho toàn hóa đơn
    }

    public class Promotion
    {
        public int Id { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }
        public decimal DiscountPercentage { get; set; } // Phần trăm giảm giá
        public decimal? MinimumOrderValue { get; set; } // Giá trị đơn hàng tối thiểu để áp dụng khuyến mãi
        public decimal? MaximumDiscountValue { get; set; } // Giá trị giảm tối đa
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsActive { get; set; }
        public PromotionType PromotionType { get; set; }
        public List<PromotionProduct> PromotionProducts { get; set; } = new List<PromotionProduct>();
    }
}
