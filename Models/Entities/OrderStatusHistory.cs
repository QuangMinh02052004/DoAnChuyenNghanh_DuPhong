namespace Bloomie.Models.Entities;

public class OrderStatusHistory
{
    public int Id { get; set; }
    public string OrderId { get; set; }
    public Order Order { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime ChangeDate { get; set; }
    public string ChangedBy { get; set; } // UserId của người thay đổi (Admin hoặc hệ thống)
}