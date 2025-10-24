namespace Bloomie.Models.Entities
{
    public class FlowerTypeProduct
    {
        public int FlowerTypeId { get; set; }
        public FlowerType FlowerType { get; set; }
        public int ProductId { get; set; }
        public Product Product { get; set; }
        public int Quantity { get; set; } // Số lượng hoa tươi cần cho 1 sản phẩm
    }
}