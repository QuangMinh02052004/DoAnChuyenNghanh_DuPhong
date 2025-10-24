using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.Entities;
using System;
using System.Linq;
using System.Threading.Tasks;
using Bloomie.Models.ViewModels;
using OfficeOpenXml;
using System.IO;
using Microsoft.Extensions.Logging;
using System.Globalization;
using OfficeOpenXml.Style;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminDashboardController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AdminDashboardController> _logger;

        public AdminDashboardController(ApplicationDbContext context, ILogger<AdminDashboardController> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IActionResult> Index(string searchQuery = null, int? categoryId = null, int year = 0, int month = 0)
        {
            const int lowStockThreshold = 10;

            var today = DateTime.UtcNow.AddHours(7).Date; // Điều chỉnh múi giờ +07:00 cho Việt Nam
            var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
            var endOfWeek = startOfWeek.AddDays(7);
            var currentYear = year == 0 ? DateTime.Now.Year : year;

            var dashboardViewModel = new AdminDashboardViewModel
            {
                Year = currentYear,
                Month = month,
                TotalProducts = await _context.Products.CountAsync(p => p.IsActive),
                TotalOrders = await _context.Orders.CountAsync(),
                TotalSuppliers = await _context.Suppliers.CountAsync(),
                TotalUsers = await _context.Users.CountAsync(),
                TotalCategories = await _context.Categories.CountAsync(),
                TotalPromotions = await _context.Promotions.CountAsync(),
                TotalAccessCount = await _context.UserAccessLogs
                    .CountAsync(log => log.UserId != null && (log.Url == "/" || log.Url == "/Home/Index")),
                LowStockProductCount = await _context.Products
                    .CountAsync(p => p.IsActive && p.Quantity > 0 && p.Quantity <= lowStockThreshold),
                OutOfStockProductCount = await _context.Products
                    .CountAsync(p => p.IsActive && p.Quantity == 0),
                DailyRevenue = await _context.Orders
                    .Include(o => o.Payment)
                    .Where(o => o.OrderDate.Date == today && o.Payment != null && o.Payment.PaymentStatus == "Đã thanh toán")
                    .SumAsync(o => o.TotalPrice),
                WeeklyRevenue = await _context.Orders
                    .Include(o => o.Payment)
                    .Where(o => o.OrderDate >= startOfWeek && o.OrderDate < endOfWeek && o.Payment != null && o.Payment.PaymentStatus == "Đã thanh toán")
                    .SumAsync(o => o.TotalPrice),
                TotalSalesRevenue = await _context.Orders
                    .Include(o => o.Payment)
                    .Where(o => o.Payment != null && o.Payment.PaymentStatus == "Đã thanh toán")
                    .SumAsync(o => o.TotalPrice),
                TotalItemsSold = await _context.OrderDetails
                    .Include(od => od.Order)
                    .ThenInclude(o => o.Payment)
                    .Where(od => od.Order != null && od.Order.Payment != null && od.Order.Payment.PaymentStatus == "Đã thanh toán")
                    .SumAsync(od => od.Quantity),
                TotalImportRevenue = await _context.InventoryTransactions
                    .Include(t => t.Batch)
                    .Where(t => t.TransactionType == TransactionType.Import && t.Batch != null)
                    .SumAsync(t => t.Quantity * t.Batch.UnitPrice),
                TotalItemsImported = await _context.InventoryTransactions
                    .Where(t => t.TransactionType == TransactionType.Import)
                    .SumAsync(t => t.Quantity)
            };

            // Lấy sản phẩm sắp hết hàng
            var lowStockQuery = _context.Products
                .Include(p => p.Category)
                .Where(p => p.IsActive && p.Quantity > 0 && p.Quantity <= lowStockThreshold);

            if (!string.IsNullOrEmpty(searchQuery))
            {
                lowStockQuery = lowStockQuery.Where(p => p.Name.Contains(searchQuery));
            }

            if (categoryId.HasValue)
            {
                lowStockQuery = lowStockQuery.Where(p => p.CategoryId == categoryId);
            }

            dashboardViewModel.LowStockProducts = await lowStockQuery
                .OrderBy(p => p.Quantity)
                .Take(5)
                .ToListAsync();

            dashboardViewModel.Categories = await _context.Categories.ToListAsync();

            dashboardViewModel.Notifications = new List<string>();
            var pendingOrders = await _context.Orders
                .CountAsync(o => o.OrderStatus == OrderStatus.Pending);
            if (pendingOrders > 0)
            {
                dashboardViewModel.Notifications.Add($"{pendingOrders} đơn hàng mới cần xử lý.");
            }
            if (dashboardViewModel.LowStockProductCount > 0)
            {
                dashboardViewModel.Notifications.Add($"{dashboardViewModel.LowStockProductCount} sản phẩm sắp hết hàng cần nhập thêm.");
            }

            // Dữ liệu cho biểu đồ
            dashboardViewModel.PendingOrders = await _context.Orders.CountAsync(o => o.OrderStatus == OrderStatus.Pending);
            dashboardViewModel.CancelledOrders = await _context.Orders.CountAsync(o => o.OrderStatus == OrderStatus.Cancelled);
            dashboardViewModel.DeliveredOrders = await _context.Orders.CountAsync(o => o.OrderStatus == OrderStatus.Delivered);

            // Tính toán doanh thu bán hàng và nhập hàng
            var salesQuery = _context.Orders
                .Include(o => o.Payment)
                .Where(o => o.Payment != null && o.Payment.PaymentStatus == "Đã thanh toán" && o.OrderDate.Year == currentYear);
            if (month > 0)
            {
                salesQuery = salesQuery.Where(o => o.OrderDate.Month == month);
            }

            var salesData = await salesQuery
                .GroupBy(o => month == 0 ? o.OrderDate.Month : o.OrderDate.Day)
                .Select(g => new RevenueSummary
                {
                    Period = g.Key,
                    TotalSalesRevenue = g.Sum(o => o.TotalPrice),
                    TotalItemsSold = g.Sum(o => o.OrderDetails.Sum(od => od.Quantity))
                })
                .OrderBy(g => g.Period)
                .ToListAsync();

            var importQuery = _context.InventoryTransactions
                .Include(t => t.Batch)
                .Where(t => t.TransactionType == TransactionType.Import && t.TransactionDate.Year == currentYear);
            if (month > 0)
            {
                importQuery = importQuery.Where(t => t.TransactionDate.Month == month);
            }

            var importData = await importQuery
                .GroupBy(t => month == 0 ? t.TransactionDate.Month : t.TransactionDate.Day)
                .Select(g => new RevenueSummary
                {
                    Period = g.Key,
                    TotalImportRevenue = g.Sum(t => t.Batch != null ? t.Quantity * t.Batch.UnitPrice : 0),
                    TotalItemsImported = g.Sum(t => t.Quantity)
                })
                .OrderBy(g => g.Period)
                .ToListAsync();

            // Tính toán xu hướng ưa thích
            var orderDetailsQuery = _context.OrderDetails
                .Include(od => od.Order)
                .Include(od => od.Product)
                .Where(od => od.Order != null && od.Order.OrderDate.Year == currentYear);
            if (month > 0)
            {
                orderDetailsQuery = orderDetailsQuery.Where(od => od.Order.OrderDate.Month == month);
            }

            var orderDetails = await orderDetailsQuery.ToListAsync();

            var favoriteTrends = orderDetails
                .GroupBy(od => month == 0 ? od.Order.OrderDate.Month : od.Order.OrderDate.Day)
                .Select(g => new
                {
                    Month = g.Key,
                    TotalQuantity = g.Sum(od => od.Quantity),
                    TopProduct = g
                        .GroupBy(od => od.Product)
                        .Select(pg => new { ProductName = pg.Key?.Name ?? "N/A", Quantity = pg.Sum(od => od.Quantity) })
                        .OrderByDescending(pg => pg.Quantity)
                        .FirstOrDefault()
                })
                .OrderBy(g => g.Month)
                .Select(g => new FavoriteTrend
                {
                    Month = g.Month,
                    TotalLikes = g.TotalQuantity,
                    TopProductName = g.TopProduct != null ? g.TopProduct.ProductName : "N/A",
                    TopProductQuantity = g.TopProduct != null ? g.TopProduct.Quantity : 0
                })
                .ToList();

            // Kết hợp dữ liệu
            var allPeriods = salesData.Select(s => s.Period)
                .Union(importData.Select(i => i.Period))
                .Union(favoriteTrends.Select(f => f.Month))
                .Distinct()
                .OrderBy(p => p)
                .ToList();

            dashboardViewModel.RevenueSummaries = allPeriods.Select(period => new RevenueSummary
            {
                Period = period,
                TotalSalesRevenue = salesData.FirstOrDefault(s => s.Period == period)?.TotalSalesRevenue ?? 0,
                TotalItemsSold = salesData.FirstOrDefault(s => s.Period == period)?.TotalItemsSold ?? 0,
                TotalImportRevenue = importData.FirstOrDefault(i => i.Period == period)?.TotalImportRevenue ?? 0,
                TotalItemsImported = importData.FirstOrDefault(i => i.Period == period)?.TotalItemsImported ?? 0
            }).ToList();

            // Đồng bộ FavoriteTrends
            dashboardViewModel.FavoriteTrends = allPeriods.Select(period => new FavoriteTrend
            {
                Month = period,
                TotalLikes = favoriteTrends.FirstOrDefault(f => f.Month == period)?.TotalLikes ?? 0,
                TopProductName = favoriteTrends.FirstOrDefault(f => f.Month == period)?.TopProductName ?? "N/A",
                TopProductQuantity = favoriteTrends.FirstOrDefault(f => f.Month == period)?.TopProductQuantity ?? 0
            }).ToList();

            // Doanh thu theo danh mục
            var categoryRevenueQuery = _context.Orders
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .ThenInclude(p => p.Category)
                .Include(o => o.Payment)
                .Where(o => o.Payment != null && o.Payment.PaymentStatus == "Đã thanh toán" && o.OrderDate.Year == currentYear);
            if (month > 0)
            {
                categoryRevenueQuery = categoryRevenueQuery.Where(o => o.OrderDate.Month == month);
            }

            dashboardViewModel.CategoryRevenueSummaries = await categoryRevenueQuery
                .SelectMany(o => o.OrderDetails)
                .GroupBy(od => od.Product.Category.Name)
                .Select(g => new CategoryRevenueSummary
                {
                    CategoryName = g.Key,
                    TotalRevenue = g.Sum(od => od.Quantity * od.Price)
                })
                .ToListAsync();

            // Đơn hàng theo tháng
            var monthlyOrderQuery = _context.Orders
                .Where(o => o.OrderDate.AddHours(7).Year == currentYear);
            if (month > 0)
            {
                monthlyOrderQuery = monthlyOrderQuery.Where(o => o.OrderDate.AddHours(7).Month == month);
            }

            var monthlyOrderTrends = await monthlyOrderQuery
                .GroupBy(o => o.OrderDate.AddHours(7).Month)
                .Select(g => new MonthlyOrderTrend
                {
                    Month = g.Key,
                    PendingOrders = g.Count(o => o.OrderStatus == OrderStatus.Pending),
                    CancelledOrders = g.Count(o => o.OrderStatus == OrderStatus.Cancelled),
                    DeliveredOrders = g.Count(o => o.OrderStatus == OrderStatus.Delivered),
                    ProcessingOrders = g.Count(o => o.OrderStatus == OrderStatus.Processing),
                    ShippedOrders = g.Count(o => o.OrderStatus == OrderStatus.Shipped)
                })
                .OrderBy(t => t.Month)
                .ToListAsync();

            dashboardViewModel.MonthlyOrderTrends = monthlyOrderTrends;

            return View(dashboardViewModel);
        }

        // Xuất báo cáo dưới dạng Excel
        public async Task<IActionResult> ExportReport()
        {
            try
            {
                const int lowStockThreshold = 10;

                var today = DateTime.UtcNow.AddHours(7).Date; // Điều chỉnh múi giờ +07:00 cho Việt Nam
                var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
                var endOfWeek = startOfWeek.AddDays(7);
                var currentYear = DateTime.Now.Year;

                // Tính toán xu hướng ưa thích cho báo cáo
                var orderDetails = await _context.OrderDetails
                    .Include(od => od.Order)
                    .Include(od => od.Product)
                    .Where(od => od.Order != null && od.Order.OrderDate.Year == currentYear)
                    .ToListAsync();

                var favoriteTrends = orderDetails
                    .GroupBy(od => od.Order.OrderDate.Month)
                    .Select(g => new
                    {
                        Month = g.Key,
                        TotalQuantity = g.Sum(od => od.Quantity),
                        TopProduct = g
                            .GroupBy(od => od.Product)
                            .Select(pg => new { ProductName = pg.Key?.Name ?? "N/A", Quantity = pg.Sum(od => od.Quantity) })
                            .OrderByDescending(pg => pg.Quantity)
                            .FirstOrDefault()
                    })
                    .OrderBy(g => g.Month)
                    .Select(g => new FavoriteTrend
                    {
                        Month = g.Month,
                        TotalLikes = g.TotalQuantity,
                        TopProductName = g.TopProduct != null ? g.TopProduct.ProductName : "N/A",
                        TopProductQuantity = g.TopProduct != null ? g.TopProduct.Quantity : 0
                    })
                    .ToList();

                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("Báo cáo Tổng quan");

                    // Tiêu đề chính
                    worksheet.Cells[1, 1].Value = "BÁO CÁO TỔNG QUAN";
                    worksheet.Cells[1, 1, 1, 6].Merge = true;
                    worksheet.Cells[1, 1].Style.Font.Size = 18;
                    worksheet.Cells[1, 1].Style.Font.Bold = true;
                    worksheet.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[1, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[1, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(200, 200, 200));

                    // Ngày tạo báo cáo
                    worksheet.Cells[2, 1].Value = $"Ngày: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
                    worksheet.Cells[2, 1, 2, 6].Merge = true;
                    worksheet.Cells[2, 1].Style.Font.Size = 12;
                    worksheet.Cells[2, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                    int row = 4;

                    // Thống kê tổng quan
                    worksheet.Cells[row, 1].Value = "Thống kê Tổng quan";
                    worksheet.Cells[row, 1, row, 6].Merge = true;
                    worksheet.Cells[row, 1].Style.Font.Size = 14;
                    worksheet.Cells[row, 1].Style.Font.Bold = true;
                    worksheet.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(220, 220, 220));
                    row += 2;

                    worksheet.Cells[row, 1].Value = "STT";
                    worksheet.Cells[row, 2].Value = "Thống kê";
                    worksheet.Cells[row, 3].Value = "Giá trị";
                    worksheet.Cells[row, 1, row, 3].Style.Font.Bold = true;
                    worksheet.Cells[row, 1, row, 3].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[row, 1, row, 3].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                    worksheet.Cells[row, 1, row, 3].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    row++;

                    int stt = 1;
                    worksheet.Cells[row, 1].Value = stt++;
                    worksheet.Cells[row, 2].Value = "Tổng số sản phẩm";
                    worksheet.Cells[row, 3].Value = await _context.Products.CountAsync(p => p.IsActive);
                    worksheet.Cells[row, 1, row, 3].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    row++;
                    worksheet.Cells[row, 1].Value = stt++;
                    worksheet.Cells[row, 2].Value = "Tổng số đơn hàng";
                    worksheet.Cells[row, 3].Value = await _context.Orders.CountAsync();
                    worksheet.Cells[row, 1, row, 3].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    row++;
                    worksheet.Cells[row, 1].Value = stt++;
                    worksheet.Cells[row, 2].Value = "Tổng số người dùng";
                    worksheet.Cells[row, 3].Value = await _context.Users.CountAsync();
                    worksheet.Cells[row, 1, row, 3].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    row++;
                    worksheet.Cells[row, 1].Value = stt++;
                    worksheet.Cells[row, 2].Value = "Tổng số danh mục";
                    worksheet.Cells[row, 3].Value = await _context.Categories.CountAsync();
                    worksheet.Cells[row, 1, row, 3].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    row++;
                    worksheet.Cells[row, 1].Value = stt++;
                    worksheet.Cells[row, 2].Value = "Tổng số khuyến mãi";
                    worksheet.Cells[row, 3].Value = await _context.Promotions.CountAsync();
                    worksheet.Cells[row, 1, row, 3].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    row++;
                    worksheet.Cells[row, 1].Value = stt++;
                    worksheet.Cells[row, 2].Value = "Tổng số lượt truy cập";
                    worksheet.Cells[row, 3].Value = await _context.UserAccessLogs.CountAsync(log => log.UserId != null && (log.Url == "/" || log.Url == "/Home/Index"));
                    worksheet.Cells[row, 1, row, 3].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    row++;
                    worksheet.Cells[row, 1].Value = stt++;
                    worksheet.Cells[row, 2].Value = "Tổng doanh thu hôm nay";
                    worksheet.Cells[row, 3].Value = (await _context.Orders
                        .Include(o => o.Payment)
                        .Where(o => o.OrderDate.Date == today && o.Payment != null && o.Payment.PaymentStatus == "Đã thanh toán")
                        .SumAsync(o => o.TotalPrice)).ToString("N0") + " VND";
                    worksheet.Cells[row, 1, row, 3].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    row++;
                    worksheet.Cells[row, 1].Value = stt++;
                    worksheet.Cells[row, 2].Value = "Tổng doanh thu tuần này";
                    worksheet.Cells[row, 3].Value = (await _context.Orders
                        .Include(o => o.Payment)
                        .Where(o => o.OrderDate >= startOfWeek && o.OrderDate < endOfWeek && o.Payment != null && o.Payment.PaymentStatus == "Đã thanh toán")
                        .SumAsync(o => o.TotalPrice)).ToString("N0") + " VND";
                    worksheet.Cells[row, 1, row, 3].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    row += 2;

                    // Danh sách tất cả sản phẩm
                    worksheet.Cells[row, 1].Value = "Danh sách tất cả sản phẩm";
                    worksheet.Cells[row, 1, row, 6].Merge = true;
                    worksheet.Cells[row, 1].Style.Font.Size = 14;
                    worksheet.Cells[row, 1].Style.Font.Bold = true;
                    worksheet.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(220, 220, 220));
                    row++;

                    var allProducts = await _context.Products
                        .Include(p => p.Category)
                        .Where(p => p.IsActive)
                        .ToListAsync();
                    if (allProducts.Any())
                    {
                        int startRow = row;
                        worksheet.Cells[row, 1].Value = "STT";
                        worksheet.Cells[row, 2].Value = "Tên sản phẩm";
                        worksheet.Cells[row, 3].Value = "Số lượng";
                        worksheet.Cells[row, 4].Value = "Danh mục";
                        worksheet.Cells[row, 5].Value = "Giá";
                        worksheet.Cells[row, 6].Value = "Trạng thái tồn kho";
                        worksheet.Cells[row, 1, row, 6].Style.Font.Bold = true;
                        worksheet.Cells[row, 1, row, 6].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        worksheet.Cells[row, 1, row, 6].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                        worksheet.Cells[row, 1, row, 6].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        row++;

                        stt = 1;
                        foreach (var product in allProducts)
                        {
                            worksheet.Cells[row, 1].Value = stt++;
                            worksheet.Cells[row, 2].Value = product.Name;
                            worksheet.Cells[row, 3].Value = product.Quantity;
                            worksheet.Cells[row, 4].Value = product.Category?.Name ?? "N/A";
                            worksheet.Cells[row, 5].Value = product.Price.ToString("N0") + " VND";
                            worksheet.Cells[row, 6].Value = product.Quantity > 0 ? "Còn hàng" : "Hết hàng";
                            worksheet.Cells[row, 1, row, 6].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                            row++;
                        }
                        worksheet.Cells[startRow, 1, row - 1, 6].AutoFitColumns();
                    }
                    else
                    {
                        worksheet.Cells[row, 1].Value = "Không có sản phẩm nào.";
                        worksheet.Cells[row, 1, row, 6].Merge = true;
                    }
                    row += 2;

                    // Danh sách đơn hàng
                    worksheet.Cells[row, 1].Value = "Danh sách đơn hàng";
                    worksheet.Cells[row, 1, row, 6].Merge = true;
                    worksheet.Cells[row, 1].Style.Font.Size = 14;
                    worksheet.Cells[row, 1].Style.Font.Bold = true;
                    worksheet.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(220, 220, 220));
                    row++;

                    var allOrders = await _context.Orders
                        .Include(o => o.User)
                        .Include(o => o.Payment)
                        .ToListAsync();
                    if (allOrders.Any())
                    {
                        int startRow = row;
                        worksheet.Cells[row, 1].Value = "STT";
                        worksheet.Cells[row, 2].Value = "Mã đơn hàng";
                        worksheet.Cells[row, 3].Value = "Người dùng";
                        worksheet.Cells[row, 4].Value = "Ngày đặt";
                        worksheet.Cells[row, 5].Value = "Tổng giá";
                        worksheet.Cells[row, 6].Value = "Trạng thái";
                        worksheet.Cells[row, 7].Value = "Số lượng sản phẩm";
                        worksheet.Cells[row, 1, row, 7].Style.Font.Bold = true;
                        worksheet.Cells[row, 1, row, 7].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        worksheet.Cells[row, 1, row, 7].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                        worksheet.Cells[row, 1, row, 7].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        row++;

                        stt = 1;
                        foreach (var order in allOrders)
                        {
                            worksheet.Cells[row, 1].Value = stt++;
                            worksheet.Cells[row, 2].Value = order.Id;
                            worksheet.Cells[row, 3].Value = order.User?.UserName ?? "N/A";
                            worksheet.Cells[row, 4].Value = order.OrderDate.ToString("dd/MM/yyyy HH:mm");
                            worksheet.Cells[row, 5].Value = order.TotalPrice.ToString("N0") + " VND";
                            worksheet.Cells[row, 6].Value = OrderStatusHelper.GetStatusDescription(order.OrderStatus);
                            worksheet.Cells[row, 7].Value = (await _context.OrderDetails.CountAsync(od => od.OrderId == order.Id));
                            worksheet.Cells[row, 1, row, 7].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                            row++;
                        }
                        worksheet.Cells[startRow, 1, row - 1, 7].AutoFitColumns();
                    }
                    else
                    {
                        worksheet.Cells[row, 1].Value = "Không có đơn hàng nào.";
                        worksheet.Cells[row, 1, row, 6].Merge = true;
                    }
                    row += 2;

                    // Doanh thu theo tháng
                    worksheet.Cells[row, 1].Value = "Doanh thu năm " + currentYear + " (Theo tháng)";
                    worksheet.Cells[row, 1, row, 6].Merge = true;
                    worksheet.Cells[row, 1].Style.Font.Size = 14;
                    worksheet.Cells[row, 1].Style.Font.Bold = true;
                    worksheet.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(220, 220, 220));
                    row++;

                    var revenueSummaries = await _context.Orders
                        .Include(o => o.Payment)
                        .Where(o => o.Payment != null && o.Payment.PaymentStatus == "Đã thanh toán" && o.OrderDate.Year == currentYear)
                        .GroupBy(o => o.OrderDate.Month)
                        .Select(g => new { Month = g.Key, TotalRevenue = g.Sum(o => o.TotalPrice) })
                        .OrderBy(g => g.Month)
                        .ToListAsync();
                    if (revenueSummaries.Any())
                    {
                        int startRow = row;
                        worksheet.Cells[row, 1].Value = "STT";
                        worksheet.Cells[row, 2].Value = "Tháng";
                        worksheet.Cells[row, 3].Value = "Doanh thu";
                        worksheet.Cells[row, 1, row, 3].Style.Font.Bold = true;
                        worksheet.Cells[row, 1, row, 3].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        worksheet.Cells[row, 1, row, 3].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                        worksheet.Cells[row, 1, row, 3].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        row++;

                        stt = 1;
                        foreach (var summary in revenueSummaries)
                        {
                            worksheet.Cells[row, 1].Value = stt++;
                            worksheet.Cells[row, 2].Value = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(summary.Month);
                            worksheet.Cells[row, 3].Value = summary.TotalRevenue.ToString("N0") + " VND";
                            worksheet.Cells[row, 1, row, 3].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                            row++;
                        }
                        worksheet.Cells[startRow, 1, row - 1, 3].AutoFitColumns();
                    }
                    else
                    {
                        worksheet.Cells[row, 1].Value = "Không có dữ liệu doanh thu.";
                        worksheet.Cells[row, 1, row, 6].Merge = true;
                    }
                    row += 2;

                    // Doanh thu theo danh mục
                    worksheet.Cells[row, 1].Value = "Doanh thu năm " + currentYear + " (Theo danh mục)";
                    worksheet.Cells[row, 1, row, 6].Merge = true;
                    worksheet.Cells[row, 1].Style.Font.Size = 14;
                    worksheet.Cells[row, 1].Style.Font.Bold = true;
                    worksheet.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(220, 220, 220));
                    row++;

                    var categoryRevenueSummaries = await _context.Orders
                        .Include(o => o.OrderDetails)
                        .ThenInclude(od => od.Product)
                        .ThenInclude(p => p.Category)
                        .Include(o => o.Payment)
                        .Where(o => o.Payment != null && o.Payment.PaymentStatus == "Đã thanh toán" && o.OrderDate.Year == currentYear)
                        .SelectMany(o => o.OrderDetails)
                        .GroupBy(od => od.Product.Category.Name)
                        .Select(g => new { CategoryName = g.Key, TotalRevenue = g.Sum(od => od.Quantity * od.Price) })
                        .ToListAsync();
                    if (categoryRevenueSummaries.Any())
                    {
                        int startRow = row;
                        worksheet.Cells[row, 1].Value = "STT";
                        worksheet.Cells[row, 2].Value = "Danh mục";
                        worksheet.Cells[row, 3].Value = "Doanh thu";
                        worksheet.Cells[row, 1, row, 3].Style.Font.Bold = true;
                        worksheet.Cells[row, 1, row, 3].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        worksheet.Cells[row, 1, row, 3].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                        worksheet.Cells[row, 1, row, 3].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        row++;

                        stt = 1;
                        foreach (var summary in categoryRevenueSummaries)
                        {
                            worksheet.Cells[row, 1].Value = stt++;
                            worksheet.Cells[row, 2].Value = summary.CategoryName ?? "N/A";
                            worksheet.Cells[row, 3].Value = summary.TotalRevenue.ToString("N0") + " VND";
                            worksheet.Cells[row, 1, row, 3].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                            row++;
                        }
                        worksheet.Cells[startRow, 1, row - 1, 3].AutoFitColumns();
                    }
                    else
                    {
                        worksheet.Cells[row, 1].Value = "Không có dữ liệu doanh thu theo danh mục.";
                        worksheet.Cells[row, 1, row, 6].Merge = true;
                    }
                    row += 2;

                    // Danh sách nhà cung cấp
                    worksheet.Cells[row, 1].Value = "Danh sách nhà cung cấp";
                    worksheet.Cells[row, 1, row, 6].Merge = true;
                    worksheet.Cells[row, 1].Style.Font.Size = 14;
                    worksheet.Cells[row, 1].Style.Font.Bold = true;
                    worksheet.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(220, 220, 220));
                    row++;

                    var suppliers = await _context.Suppliers.ToListAsync();
                    if (suppliers.Any())
                    {
                        int startRow = row;
                        worksheet.Cells[row, 1].Value = "STT";
                        worksheet.Cells[row, 2].Value = "Tên nhà cung cấp";
                        worksheet.Cells[row, 3].Value = "Email";
                        worksheet.Cells[row, 4].Value = "Số điện thoại";
                        worksheet.Cells[row, 5].Value = "Địa chỉ";
                        worksheet.Cells[row, 1, row, 5].Style.Font.Bold = true;
                        worksheet.Cells[row, 1, row, 5].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        worksheet.Cells[row, 1, row, 5].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                        worksheet.Cells[row, 1, row, 5].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        row++;

                        stt = 1;
                        foreach (var supplier in suppliers)
                        {
                            worksheet.Cells[row, 1].Value = stt++;
                            worksheet.Cells[row, 2].Value = supplier.Name;
                            worksheet.Cells[row, 3].Value = supplier.Email;
                            worksheet.Cells[row, 4].Value = supplier.Phone;
                            worksheet.Cells[row, 5].Value = supplier.Address;
                            worksheet.Cells[row, 1, row, 5].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                            row++;
                        }
                        worksheet.Cells[startRow, 1, row - 1, 5].AutoFitColumns();
                    }
                    else
                    {
                        worksheet.Cells[row, 1].Value = "Không có nhà cung cấp nào.";
                        worksheet.Cells[row, 1, row, 6].Merge = true;
                    }
                    row += 2;

                    // Xu hướng sản phẩm được mua
                    worksheet.Cells[row, 1].Value = "Xu hướng Sản Phẩm Được Mua Năm " + currentYear + " (Theo tháng)";
                    worksheet.Cells[row, 1, row, 6].Merge = true;
                    worksheet.Cells[row, 1].Style.Font.Size = 14;
                    worksheet.Cells[row, 1].Style.Font.Bold = true;
                    worksheet.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[row, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[row, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(220, 220, 220));
                    row++;

                    if (favoriteTrends.Any())
                    {
                        int startRow = row;
                        worksheet.Cells[row, 1].Value = "STT";
                        worksheet.Cells[row, 2].Value = "Tháng";
                        worksheet.Cells[row, 3].Value = "Sản Phẩm Hàng Đầu";
                        worksheet.Cells[row, 4].Value = "Số Lượng";
                        worksheet.Cells[row, 1, row, 4].Style.Font.Bold = true;
                        worksheet.Cells[row, 1, row, 4].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        worksheet.Cells[row, 1, row, 4].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                        worksheet.Cells[row, 1, row, 4].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        row++;

                        stt = 1;
                        foreach (var trend in favoriteTrends)
                        {
                            worksheet.Cells[row, 1].Value = stt++;
                            worksheet.Cells[row, 2].Value = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(trend.Month);
                            worksheet.Cells[row, 3].Value = trend.TopProductName;
                            worksheet.Cells[row, 4].Value = trend.TopProductQuantity.ToString("N0");
                            worksheet.Cells[row, 1, row, 4].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                            row++;
                        }
                        worksheet.Cells[startRow, 1, row - 1, 4].AutoFitColumns();
                    }
                    else
                    {
                        worksheet.Cells[row, 1].Value = "Không có dữ liệu xu hướng sản phẩm.";
                        worksheet.Cells[row, 1, row, 6].Merge = true;
                    }
                    row += 2;

                    // Footer: Địa điểm, ngày tháng năm, ký tên
                    int footerStartColumn = 4; // Bắt đầu từ cột D để căn phải
                    worksheet.Cells[row, footerStartColumn].Value = $"TP.Hồ Chí Minh, ngày {DateTime.Now.Day} tháng {DateTime.Now.Month} năm {DateTime.Now.Year}";
                    worksheet.Cells[row, footerStartColumn, row, 6].Merge = true;
                    worksheet.Cells[row, footerStartColumn].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    row++;

                    // Khoảng trống để ký tên (3 dòng trống)
                    row += 3;

                    // Dòng "Ký tên và ghi rõ họ tên"
                    worksheet.Cells[row, footerStartColumn].Value = "Ký tên và ghi rõ họ tên";
                    worksheet.Cells[row, footerStartColumn, row, 6].Merge = true;
                    worksheet.Cells[row, footerStartColumn].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    worksheet.Cells[row, footerStartColumn].Style.Font.Italic = true;

                    // Xuất file Excel
                    var stream = new MemoryStream(package.GetAsByteArray());
                    return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "BaoCaoTongQuan_ChiTiet.xlsx");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xuất báo cáo Excel tại: {Message}", ex.Message);
                return StatusCode(500, "Có lỗi xảy ra khi xuất báo cáo. Vui lòng thử lại sau.");
            }
        }
    }
}