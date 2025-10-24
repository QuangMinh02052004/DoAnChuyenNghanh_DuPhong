using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Bloomie.Models;
using System;
using System.Linq;
using Bloomie.Data;
using Bloomie.Areas.Admin.Models;
using Bloomie.Models.Entities;
using System.Globalization;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminRevenueController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminRevenueController(ApplicationDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }

        // API: Lấy dữ liệu doanh thu và nhập hàng theo năm & tháng, bao gồm tuần
        [HttpGet]
        public IActionResult GetRevenue(int year, int month = 0)
        {
            // Doanh thu bán hàng và số lượng bán ra từ Orders
            var salesQuery = _context.Orders
                .Where(o => o.OrderDate.Year == year);

            if (month > 0)
            {
                salesQuery = salesQuery.Where(o => o.OrderDate.Month == month);
            }

            var salesData = salesQuery
                .GroupBy(o => month == 0 ? o.OrderDate.Month : o.OrderDate.Day)
                .Select(g => new
                {
                    Period = g.Key,
                    TotalSalesRevenue = g.Sum(o => o.TotalPrice),
                    TotalItemsSold = g.Sum(o => o.OrderDetails.Sum(od => od.Quantity))
                })
                .ToList();

            // Doanh thu nhập hàng và số lượng nhập từ InventoryTransactions
            var importQuery = _context.InventoryTransactions
                .Include(t => t.Batch)
                .Where(t => t.TransactionType == TransactionType.Import && t.TransactionDate.Year == year);

            if (month > 0)
            {
                importQuery = importQuery.Where(t => t.TransactionDate.Month == month);
            }

            var importData = importQuery
                .GroupBy(t => month == 0 ? t.TransactionDate.Month : t.TransactionDate.Day)
                .Select(g => new
                {
                    Period = g.Key,
                    TotalImportRevenue = g.Sum(t => t.Batch != null ? t.Quantity * t.Batch.UnitPrice : 0),
                    TotalItemsImported = g.Sum(t => t.Quantity)
                })
                .ToList();

            // Tính doanh thu theo tuần
            var weeklySalesData = _context.Orders
                .Where(o => o.OrderDate.Year == year)
                .AsEnumerable()
                .GroupBy(o => CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(o.OrderDate, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Sunday))
                .Select(g => new
                {
                    Week = g.Key,
                    SalesRevenue = g.Sum(o => o.TotalPrice),
                    ItemsSold = g.Sum(o => o.OrderDetails.Sum(od => od.Quantity)),
                    ImportRevenue = 0m,
                    ItemsImported = 0
                })
                .ToList();

            var weeklyImportData = _context.InventoryTransactions
                .Include(t => t.Batch)
                .Where(t => t.TransactionType == TransactionType.Import && t.TransactionDate.Year == year)
                .AsEnumerable()
                .GroupBy(t => CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(t.TransactionDate, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Sunday))
                .Select(g => new
                {
                    Week = g.Key,
                    SalesRevenue = 0m,
                    ItemsSold = 0,
                    ImportRevenue = g.Sum(t => t.Batch != null ? t.Quantity * t.Batch.UnitPrice : 0),
                    ItemsImported = g.Sum(t => t.Quantity)
                })
                .ToList();

            // Kết hợp dữ liệu tuần
            var weeklyData = weeklySalesData
                .Concat(weeklyImportData)
                .GroupBy(x => x.Week)
                .Select(g => new
                {
                    Week = g.Key,
                    TotalWeeklyRevenue = g.Sum(x => x.SalesRevenue + x.ImportRevenue),
                    TotalWeeklyItems = g.Sum(x => x.ItemsSold + x.ItemsImported)
                })
                .ToList();

            // Kết hợp dữ liệu
            var allPeriods = salesData.Select(s => s.Period)
                .Union(importData.Select(i => i.Period))
                .Distinct()
                .OrderBy(p => p)
                .ToList();

            var revenueData = allPeriods.Select(period => new
            {
                Month = month == 0 ? period : 0,
                Date = month > 0 ? period : 0,
                TotalSalesRevenue = salesData.FirstOrDefault(s => s.Period == period)?.TotalSalesRevenue ?? 0,
                TotalItemsSold = salesData.FirstOrDefault(s => s.Period == period)?.TotalItemsSold ?? 0,
                TotalImportRevenue = importData.FirstOrDefault(i => i.Period == period)?.TotalImportRevenue ?? 0,
                TotalItemsImported = importData.FirstOrDefault(i => i.Period == period)?.TotalItemsImported ?? 0,
                TotalWeeklyRevenue = weeklyData
                    .Where(w => month == 0 ? true : w.Week == CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(
                        new DateTime(year, month > 0 ? month : 1, month > 0 ? period : 1), CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Sunday))
                    .Sum(w => w.TotalWeeklyRevenue),
                TotalWeeklyItems = weeklyData
                    .Where(w => month == 0 ? true : w.Week == CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(
                        new DateTime(year, month > 0 ? month : 1, month > 0 ? period : 1), CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Sunday))
                    .Sum(w => w.TotalWeeklyItems)
            }).ToList();

            return Json(revenueData);
        }
    }
}