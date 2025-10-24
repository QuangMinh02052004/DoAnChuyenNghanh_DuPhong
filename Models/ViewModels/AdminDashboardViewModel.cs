using Bloomie.Models.Entities;

namespace Bloomie.Models.ViewModels
{
    public class AdminDashboardViewModel
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int TotalProducts { get; set; }
        public int TotalOrders { get; set; }
        public int TotalSuppliers { get; set; }
        public int TotalUsers { get; set; }
        public int TotalCategories { get; set; }
        public int TotalPromotions { get; set; }
        public int TotalAccessCount { get; set; }
        public int LowStockProductCount { get; set; }
        public int OutOfStockProductCount { get; set; }
        public decimal DailyRevenue { get; set; }
        public decimal WeeklyRevenue { get; set; }
        public List<Product> LowStockProducts { get; set; }
        public List<Category> Categories { get; set; }
        public List<string> Notifications { get; set; }
        public List<RevenueSummary> RevenueSummaries { get; set; }
        public List<CategoryRevenueSummary> CategoryRevenueSummaries { get; set; }
        public List<OrderStatusTrend> OrderTrends { get; set; } = new List<OrderStatusTrend>();
        public List<MonthlyOrderTrend> MonthlyOrderTrends { get; set; } = new List<MonthlyOrderTrend>();
        public decimal TotalSalesRevenue { get; set; }
        public int TotalItemsSold { get; set; }
        public decimal TotalImportRevenue { get; set; }
        public int TotalItemsImported { get; set; }
        public int PendingOrders { get; set; }    // Đơn hàng đã đặt
        public int CancelledOrders { get; set; }  // Đơn hàng đã hủy
        public int DeliveredOrders { get; set; }
        //public int CodOrderCount { get; set; }
        //public int PaidOrderCount { get; set; }
        public List<FavoriteTrend> FavoriteTrends { get; set; }
    }

    public class RevenueSummary
    {
        public int Period { get; set; }
        public decimal TotalSalesRevenue { get; set; }
        public int TotalItemsSold { get; set; }
        public decimal TotalImportRevenue { get; set; }
        public int TotalItemsImported { get; set; }
    }

    public class CategoryRevenueSummary
    {
        public string CategoryName { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    public class OrderStatusTrend
    {
        public DateTime Date { get; set; }
        public int PendingOrders { get; set; }
        public int CancelledOrders { get; set; }
        public int DeliveredOrders { get; set; }
    }

    public class MonthlyOrderTrend
    {
        public int Month { get; set; }
        public int PendingOrders { get; set; }
        public int CancelledOrders { get; set; }
        public int DeliveredOrders { get; set; }
        public int ProcessingOrders { get; set; } 
        public int ShippedOrders { get; set; }
    }

    public class FavoriteTrend
    {
        public int Month { get; set; }
        public int TotalLikes { get; set; } 
        public string TopProductName { get; set; } 
        public int TopProductQuantity { get; set; } 
    }
}