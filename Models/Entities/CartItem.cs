namespace Bloomie.Models.Entities
{
    public class CartItem
    {
        public int ProductId { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; } // Số lượng sản phẩm
        public decimal DiscountedPrice { get; set; } // Giá sau khi áp dụng giảm giá theo sản phẩm
        public string ImageUrl { get; set; }
        public DateTime? DeliveryDate { get; set; } // Ngày giao hàng
        public string DeliveryTime { get; set; } // Thời gian giao hàng
    }
}
