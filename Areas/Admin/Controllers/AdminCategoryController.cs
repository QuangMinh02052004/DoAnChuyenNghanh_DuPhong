using Bloomie.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc;
using Bloomie.Areas.Admin.Models;
using Bloomie.Services.Interfaces;
using Bloomie.Models.ViewModels;
using OfficeOpenXml.Style;
using OfficeOpenXml;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminCategoryController : Controller
    {
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;

        public AdminCategoryController(IProductService productService, ICategoryService categoryService)
        {
            _productService = productService;
            _categoryService = categoryService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var categories = await _categoryService.GetAllCategoriesAsync();
            return View(categories);
        }

        [HttpGet]
        public async Task<IActionResult> Add()
        {
            // Lấy danh sách danh mục cấp cao nhất để làm danh mục cha
            var topLevelCategories = await _categoryService.GetTopLevelCategoriesAsync();
            ViewBag.ParentCategories = new SelectList(topLevelCategories, "Id", "Name");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Add(Category category)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    await _categoryService.AddCategoryAsync(category);
                    TempData["SuccessMessage"] = "Thêm danh mục thành công!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Lỗi khi thêm danh mục: {ex.Message}");
                }
            }

            var topLevelCategories = await _categoryService.GetTopLevelCategoriesAsync();
            ViewBag.ParentCategories = new SelectList(topLevelCategories, "Id", "Name", category.ParentCategoryId);
            return View(category);
        }

        [HttpGet]
        public async Task<IActionResult> Display(int id)
        {
            var category = await _categoryService.GetCategoryByIdAsync(id);
            if (category == null)
            {
                return NotFound();
            }
            return View(category);
        }

        [HttpGet]
        public async Task<IActionResult> Update(int id)
        {
            var category = await _categoryService.GetCategoryByIdAsync(id);
            if (category == null)
            {
                return NotFound();
            }

            // Lấy danh sách danh mục cấp cao nhất và sản phẩm thuộc danh mục
            var topLevelCategories = await _categoryService.GetTopLevelCategoriesAsync();
            ViewBag.ParentCategories = new SelectList(topLevelCategories, "Id", "Name", category.ParentCategoryId);

            var products = await _productService.GetAllProductsAsync();
            var categoryProducts = products.Where(p => p.CategoryId == id).ToList();
            ViewBag.CategoryProducts = categoryProducts;

            return View(category);
        }

        [HttpPost]
        public async Task<IActionResult> Update(int id, Category category)
        {
            if (id != category.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    await _categoryService.UpdateCategoryAsync(category);
                    TempData["SuccessMessage"] = "Cập nhật danh mục thành công!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Lỗi khi cập nhật danh mục: {ex.Message}");
                }
            }

            var topLevelCategories = await _categoryService.GetTopLevelCategoriesAsync();
            ViewBag.ParentCategories = new SelectList(topLevelCategories, "Id", "Name", category.ParentCategoryId);
            var products = await _productService.GetAllProductsAsync();
            var categoryProducts = products.Where(p => p.CategoryId == id).ToList();
            ViewBag.CategoryProducts = categoryProducts;

            return View(category);
        }

        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var category = await _categoryService.GetCategoryByIdAsync(id);
            if (category == null)
            {
                return NotFound();
            }
            return View(category);
        }

        [HttpPost, ActionName("DeleteConfirmed")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                await _categoryService.DeleteCategoryAsync(id);
                TempData["SuccessMessage"] = "Xóa danh mục thành công!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Lỗi khi xóa danh mục: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> AssignSubCategories()
        {
            var model = new AssignSubCategoriesViewModel
            {
                ParentCategories = (await _categoryService.GetTopLevelCategoriesAsync()).ToList(),
                SubCategories = (await _categoryService.GetAllCategoriesAsync())
                    .Where(c => c.ParentCategoryId == null)
                    .ToList()
            };
            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> AssignSubCategories(AssignSubCategoriesViewModel model)
        {
            if (model.ParentCategoryId == 0 || model.SubCategoryIds == null || !model.SubCategoryIds.Any())
            {
                ModelState.AddModelError("", "Vui lòng chọn danh mục cha và ít nhất một danh mục con.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var parentCategory = await _categoryService.GetCategoryByIdAsync(model.ParentCategoryId);
                    if (parentCategory == null)
                    {
                        return NotFound();
                    }

                    // Cập nhật ParentCategoryId cho các danh mục con
                    foreach (var subCategoryId in model.SubCategoryIds)
                    {
                        var subCategory = await _categoryService.GetCategoryByIdAsync(subCategoryId);
                        if (subCategory != null)
                        {
                            subCategory.ParentCategoryId = model.ParentCategoryId;
                            await _categoryService.UpdateCategoryAsync(subCategory);
                        }
                    }

                    TempData["SuccessMessage"] = "Gán danh mục con thành công!";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Lỗi khi gán danh mục con: {ex.Message}");
                }
            }

            // Khi validation thất bại, tải lại dữ liệu
            model.ParentCategories = (await _categoryService.GetTopLevelCategoriesAsync()).ToList();
            model.SubCategories = (await _categoryService.GetAllCategoriesAsync())
                .Where(c => c.ParentCategoryId == null && c.Id != model.ParentCategoryId)
                .ToList();

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> ExportToExcel()
        {
            try
            {
                var categories = await _categoryService.GetAllCategoriesAsync();

                ExcelPackage.LicenseContext = LicenseContext.NonCommercial; // Cần nếu không có license thương mại
                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("Danh sách danh mục");

                    // Tiêu đề
                    worksheet.Cells[1, 1].Value = "Danh sách danh mục";
                    worksheet.Cells[1, 1].Style.Font.Bold = true;
                    worksheet.Cells[1, 1].Style.Font.Size = 14;
                    worksheet.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[1, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[1, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(220, 220, 220));

                    // Header
                    worksheet.Cells[2, 1].Value = "Danh mục";
                    worksheet.Cells[2, 1].Style.Font.Bold = true;
                    worksheet.Cells[2, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[2, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                    worksheet.Cells[2, 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                    int row = 3;
                    foreach (var category in categories.Where(c => c.ParentCategoryId == null))
                    {
                        // Danh mục cha
                        worksheet.Cells[row, 1].Value = category.Name;
                        worksheet.Cells[row, 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        row++;

                        // Danh mục con
                        if (category.SubCategories != null && category.SubCategories.Any())
                        {
                            foreach (var subCategory in category.SubCategories)
                            {
                                worksheet.Cells[row, 1].Value = $"↳ {subCategory.Name}";
                                worksheet.Cells[row, 1].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                                row++;
                            }
                        }
                    }

                    // Auto fit columns
                    worksheet.Cells[2, 1, row - 1, 1].AutoFitColumns();

                    var stream = new MemoryStream(package.GetAsByteArray());
                    return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"DanhSachDanhMuc_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Lỗi khi xuất báo cáo Excel: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }
    }
}