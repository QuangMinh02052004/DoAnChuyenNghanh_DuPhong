namespace Bloomie.Models.Entities
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public int QuantitySold { get; set; }
        public int LowStockThreshold { get; set; } = 5;
        public bool IsNew { get; set; }
        public DateTime CreatedDate { get; set; }
        public decimal? DiscountPercentage { get; set; }
        public decimal? DiscountPrice => Price * (1 - (DiscountPercentage ?? 0) / 100);
        public int PresentationStyleId { get; set; } 
        public PresentationStyle? PresentationStyle { get; set; }
        public bool IsActive { get; set; } = true;
        public int CategoryId { get; set; }
        public Category? Category { get; set; }
        public string? ImageUrl { get; set; }
        public List<ProductImage>? Images { get; set; } = new List<ProductImage>();
        public List<PromotionProduct>? PromotionProducts { get; set; }
        public List<Rating> Ratings { get; set; } = new List<Rating>();
        public List<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();
        public ICollection<FlowerTypeProduct> FlowerTypeProducts { get; set; } = new List<FlowerTypeProduct>();
        public string Colors { get; set; }
    }
}
