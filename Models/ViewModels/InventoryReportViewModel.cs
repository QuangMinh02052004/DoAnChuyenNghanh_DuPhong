namespace Bloomie.Models.ViewModels
{
    public class InventoryReportViewModel
    {
        public List<CategoryInventorySummary> CategorySummaries { get; set; } = new List<CategoryInventorySummary>();
        public int TotalDamagedQuantity { get; set; }
        public ProductSummary TopSellingProduct { get; set; }
        public decimal TotalInventoryValue { get; set; }
    }

    public class CategoryInventorySummary
    {
        public string CategoryName { get; set; }
        public int TotalQuantity { get; set; }
    }

    public class ProductSummary
    {
        public string ProductName { get; set; }
        public int QuantitySold { get; set; }
    }
}