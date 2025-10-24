namespace Bloomie.Models.Entities;

public class Batch
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; }
    public decimal UnitPrice { get; set; } // Giá nhập
    public DateTime ImportDate { get; set; } // Ngày nhập
    public DateTime ExpiryDate { get; set; } // Ngày hết hạn (hoa tươi)
    public List<BatchFlowerType> BatchFlowerTypes { get; set; } = new List<BatchFlowerType>();
    public List<InventoryTransaction> Transactions { get; set; } = new List<InventoryTransaction>();
}