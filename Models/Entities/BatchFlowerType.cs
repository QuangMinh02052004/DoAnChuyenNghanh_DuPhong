using Bloomie.Models.Entities;

public class BatchFlowerType
{
    public int BatchId { get; set; }
    public Batch Batch { get; set; }
    public int FlowerTypeId { get; set; }
    public FlowerType FlowerType { get; set; }
    public int InitialQuantity { get; set; } // Số lượng ban đầu của loại hoa trong lô
    public int CurrentQuantity { get; set; } // Số lượng hiện tại của loại hoa trong lô sau các thay đổi }
}