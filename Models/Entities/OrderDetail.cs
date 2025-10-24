namespace Bloomie.Models.Entities
{
    public class OrderDetail
    {
        public int Id { get; set; }
        public string OrderId { get; set; }
        public Order Order { get; set; }
        public int ProductId { get; set; }
        public Product Product { get; set; }
        public int Quantity { get; set; } // Số lượng sản phẩm trong đơn hàng
        public decimal Price { get; set; } // Giá sản phẩm tại thời điểm đặt hàng
        public DateTime? DeliveryDate { get; set; } // Ngày giao hàng
        public string DeliveryTime { get; set; } // Thời gian giao hàng
    }
}
