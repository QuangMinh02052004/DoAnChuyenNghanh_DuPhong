using Bloomie.Data;
using Bloomie.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml.Style;
using OfficeOpenXml;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminSupplierController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminSupplierController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Admin/AdminSupplier/Index
        [HttpGet]
        public async Task<IActionResult> Index(int pageNumber = 1, int pageSize = 10, bool showInactive = false, string searchString = null)
        {
            var suppliersQuery = _context.Suppliers.AsQueryable();

            // Lọc theo trạng thái
            if (!showInactive)
            {
                suppliersQuery = suppliersQuery.Where(s => s.IsActive);
            }

            // Lọc theo tìm kiếm
            if (!string.IsNullOrEmpty(searchString))
            {
                suppliersQuery = suppliersQuery.Where(s => s.Name.Contains(searchString) || s.Email.Contains(searchString) || s.Phone.Contains(searchString));
            }

            var suppliers = await suppliersQuery.ToListAsync();

            int totalItems = suppliers.Count;
            var pagedSuppliers = suppliers
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewData["CurrentPage"] = pageNumber;
            ViewData["TotalPages"] = (int)Math.Ceiling(totalItems / (double)pageSize);
            ViewData["PageSize"] = pageSize;
            ViewData["TotalItems"] = totalItems;
            ViewData["ShowInactive"] = showInactive;
            ViewData["SearchString"] = searchString;

            return View(pagedSuppliers);
        }

        // GET: /Admin/AdminSupplier/Add
        [HttpGet]
        public IActionResult Add()
        {
            return View();
        }

        // POST: /Admin/AdminSupplier/Add
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(Supplier supplier)
        {
            if (ModelState.IsValid)
            {
                supplier.IsActive = true; // Đặt mặc định là true (Hoạt động)
                _context.Add(supplier);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Đã thêm nhà cung cấp {supplier.Name}.";
                return RedirectToAction(nameof(Index));
            }
            return View(supplier);
        }

        // GET: /Admin/AdminSupplier/Update/5
        [HttpGet]
        public async Task<IActionResult> Update(int id)
        {
            var supplier = await _context.Suppliers.FindAsync(id);
            if (supplier == null) return NotFound();
            return View(supplier);
        }

        // POST: /Admin/AdminSupplier/Update/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(int id, Supplier supplier)
        {
            if (id != supplier.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(supplier);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Đã cập nhật nhà cung cấp {supplier.Name}.";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!SupplierExists(supplier.Id))
                    {
                        return NotFound();
                    }
                    throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(supplier);
        }

        // GET: /Admin/AdminSupplier/Delete/5
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var supplier = await _context.Suppliers.FindAsync(id);
            if (supplier == null) return NotFound();
            return View(supplier);
        }

        // POST: /Admin/AdminSupplier/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var supplier = await _context.Suppliers.FindAsync(id);
            if (supplier == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy nhà cung cấp để xóa.";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                _context.Suppliers.Remove(supplier); // Hard delete: xóa hoàn toàn bản ghi
                int rowsAffected = await _context.SaveChangesAsync();
                if (rowsAffected > 0)
                {
                    TempData["SuccessMessage"] = $"Đã xóa nhà cung cấp {supplier.Name} thành công.";
                }
                else
                {
                    TempData["ErrorMessage"] = "Không có bản ghi nào được xóa.";
                }
            }
            catch (DbUpdateException ex)
            {
                TempData["ErrorMessage"] = $"Lỗi khi xóa nhà cung cấp: {ex.InnerException?.Message ?? ex.Message}";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Lỗi không xác định khi xóa nhà cung cấp: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> ExportToExcel(bool showInactive = false, string searchString = null)
        {
            try
            {
                var suppliersQuery = _context.Suppliers.AsQueryable();

                // Lọc theo trạng thái
                if (!showInactive)
                {
                    suppliersQuery = suppliersQuery.Where(s => s.IsActive);
                }

                // Lọc theo tìm kiếm
                if (!string.IsNullOrEmpty(searchString))
                {
                    suppliersQuery = suppliersQuery.Where(s => s.Name.Contains(searchString) || s.Email.Contains(searchString) || s.Phone.Contains(searchString));
                }

                var suppliers = await suppliersQuery.ToListAsync();

                ExcelPackage.LicenseContext = LicenseContext.NonCommercial; 
                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("Danh sách nhà cung cấp");

                    // Tiêu đề
                    worksheet.Cells[1, 1].Value = "DANH SÁCH NHÀ CUNG CẤP";
                    worksheet.Cells[1, 1, 1, 6].Merge = true;
                    worksheet.Cells[1, 1].Style.Font.Bold = true;
                    worksheet.Cells[1, 1].Style.Font.Size = 14;
                    worksheet.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[1, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[1, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(220, 220, 220));

                    // Header
                    worksheet.Cells[2, 1].Value = "STT";
                    worksheet.Cells[2, 2].Value = "Tên nhà cung cấp";
                    worksheet.Cells[2, 3].Value = "Số điện thoại";
                    worksheet.Cells[2, 4].Value = "Email";
                    worksheet.Cells[2, 5].Value = "Địa chỉ";
                    worksheet.Cells[2, 6].Value = "Trạng thái";
                    worksheet.Cells[2, 1, 2, 6].Style.Font.Bold = true;
                    worksheet.Cells[2, 1, 2, 6].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[2, 1, 2, 6].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                    worksheet.Cells[2, 1, 2, 6].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                    int row = 3;
                    int index = 1;
                    foreach (var supplier in suppliers)
                    {
                        worksheet.Cells[row, 1].Value = index++;
                        worksheet.Cells[row, 2].Value = supplier.Name;
                        worksheet.Cells[row, 3].Value = supplier.Phone;
                        worksheet.Cells[row, 4].Value = supplier.Email;
                        worksheet.Cells[row, 5].Value = supplier.Address;
                        worksheet.Cells[row, 6].Value = supplier.IsActive ? "Hoạt động" : "Ngừng hoạt động";
                        worksheet.Cells[row, 1, row, 6].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        row++;
                    }

                    // Auto fit columns
                    worksheet.Cells[2, 1, row - 1, 6].AutoFitColumns();

                    var stream = new MemoryStream(package.GetAsByteArray());
                    return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"DanhSachNhaCungCap_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Lỗi khi xuất báo cáo Excel: {ex.Message}";
                return RedirectToAction(nameof(Index), new { showInactive, searchString });
            }
        }

        private bool SupplierExists(int id)
        {
            return _context.Suppliers.Any(e => e.Id == id);
        }
    }
}