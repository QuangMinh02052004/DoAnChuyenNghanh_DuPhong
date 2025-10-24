namespace Bloomie.Models.Entities;

public class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Phone { get; set; }
    public string Email { get; set; }
    public string Address { get; set; }
    public bool IsActive { get; set; } = true; // Trạng thái hoạt động
    public List<InventoryTransaction> Transactions { get; set; } = new List<InventoryTransaction>();
    public List<Batch> Batches { get; set; } = new List<Batch>(); // Liên kết với lô hàng
    public ICollection<FlowerType> FlowerTypes { get; set; } = new List<FlowerType>();
}