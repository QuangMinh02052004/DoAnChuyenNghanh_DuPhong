namespace Bloomie.Models.Entities;

public class FlowerType
{
    public int Id { get; set; }
    public string Name { get; set; } // Tên loại hoa (hoa hồng, hoa cúc, v.v.)
    public int Quantity { get; set; } // Số lượng tồn kho của loại hoa
    public bool IsActive { get; set; } = true;
    public List<Supplier> Suppliers { get; set; } = new List<Supplier>();
    public List<BatchFlowerType> BatchFlowerTypes { get; set; } = new List<BatchFlowerType>();
    public ICollection<FlowerTypeProduct> FlowerTypeProducts { get; set; } = new List<FlowerTypeProduct>();
    public List<InventoryTransaction> Transactions { get; set; } = new List<InventoryTransaction>(); // Liên kết với giao dịch kho
}