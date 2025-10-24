using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    public enum OrderStatus
    {
        Pending, // Chờ xác nhận
        Processing, // Đang xử lý
        Shipped, // Đã vận chuyển
        Delivered, // Đã giao hàng
        Cancelled // Đã hủy
    }

    public static class OrderStatusHelper
    {
        public static readonly Dictionary<OrderStatus, string> OrderStatusDescriptions = new Dictionary<OrderStatus, string>
    {
        { OrderStatus.Pending, "Chờ xác nhận" },
        { OrderStatus.Processing, "Đang xử lý" },
        { OrderStatus.Shipped, "Đã vận chuyển" },
        { OrderStatus.Delivered, "Đã giao hàng" },
        { OrderStatus.Cancelled, "Đã hủy" }
    };

        public static string GetStatusDescription(OrderStatus status)
        {
            return OrderStatusDescriptions.TryGetValue(status, out var description) ? description : status.ToString();
        }
    }

    public class Order
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public ApplicationUser User { get; set; }
        public DateTime OrderDate { get; set; }
        public decimal TotalPrice { get; set; }
        public string ShippingAddress { get; set; }
        public string PhoneNumber { get; set; }
        public string? Notes { get; set; }
        public OrderStatus OrderStatus { get; set; } = OrderStatus.Pending;
        public List<OrderDetail> OrderDetails { get; set; } = new();

        // Thông tin người gửi
        public string SenderName { get; set; }
        public string SenderEmail { get; set; }
        public string SenderPhoneNumber { get; set; }

        // Thông tin người nhận
        public string ReceiverName { get; set; }
        public string ReceiverEmail { get; set; }
        public string ReceiverPhoneNumber { get; set; }

        // Tùy chọn
        public bool IsSenderReceiverSame { get; set; }
        public bool IsAnonymousSender { get; set; }

        public Payment? Payment { get; set; }
        public int? PromotionId { get; set; }
        public Promotion? Promotion { get; set; }
        public string ShippingMethod { get; set; }
        public List<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();
        public List<InventoryTransaction> Transactions { get; set; } = new List<InventoryTransaction>();
    }
}
