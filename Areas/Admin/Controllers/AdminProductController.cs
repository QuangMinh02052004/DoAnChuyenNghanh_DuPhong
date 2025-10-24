using Bloomie.Models.Entities;
using Bloomie.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc;
using Bloomie.Areas.Admin.Models;
using Bloomie.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bloomie.Data;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using OfficeOpenXml.Style;
using OfficeOpenXml;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminProductController : Controller
    {
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly IInventoryService _inventoryService;
        private readonly ApplicationDbContext _context;

        public AdminProductController(
            IProductService productService,
            ICategoryService categoryService,
            IInventoryService inventoryService, ApplicationDbContext context)
        {
            _productService = productService;
            _categoryService = categoryService;
            _inventoryService = inventoryService;
            _context = context;
        }

        private async Task<(bool Success, List<string> Colors, string Presentation, string Message)> AnalyzeImage(string imagePath)
        {
            try
            {
                var scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "inference.py");
                if (!System.IO.File.Exists(scriptPath))
                {
                    return (false, new List<string>(), string.Empty, "Không tìm thấy file inference.py.");
                }

                if (!System.IO.File.Exists(imagePath))
                {
                    return (false, new List<string>(), string.Empty, "Hình ảnh không tồn tại.");
                }

                var pythonPath = Environment.GetEnvironmentVariable("PYTHON_PATH") ?? @"C:\Users\KHOA\AppData\Local\Programs\Python\Python313\python.exe";
                var processInfo = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = $"\"{scriptPath}\" \"{imagePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                string result, error;
                using (var process = new Process { StartInfo = processInfo })
                {
                    process.Start();
                    result = await process.StandardOutput.ReadToEndAsync();
                    error = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();
                }

                Console.WriteLine($"Phân tích hình ảnh: {imagePath}");
                Console.WriteLine($"Kết quả Python: {result}");
                Console.WriteLine($"Lỗi Python (nếu có): {error}");

                if (!string.IsNullOrEmpty(error))
                {
                    return (false, new List<string>(), string.Empty, $"Lỗi từ Python: {error}");
                }

                var jsonLines = result.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Where(line => line.Trim().StartsWith("{"))
                                     .ToArray();
                var jsonResult = string.Join("\n", jsonLines);

                var analysisResult = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonResult);
                if (analysisResult == null || !analysisResult.ContainsKey("success"))
                {
                    return (false, new List<string>(), string.Empty, "Kết quả từ Python không hợp lệ.");
                }

                if (analysisResult["success"] is System.Text.Json.JsonElement successElement && successElement.ValueKind == System.Text.Json.JsonValueKind.True)
                {
                    var colors = System.Text.Json.JsonSerializer.Deserialize<List<string>>(
                        analysisResult["colors"].ToString(), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    var presentation = analysisResult.ContainsKey("presentation") ? analysisResult["presentation"].ToString() : string.Empty;
                    return (true, colors ?? new List<string>(), presentation, string.Empty);
                }

                var message = analysisResult.ContainsKey("message") ? analysisResult["message"].ToString() : "Phân tích thất bại.";
                return (false, new List<string>(), string.Empty, message);
            }
            catch (Exception ex)
            {
                return (false, new List<string>(), string.Empty, $"Lỗi phân tích: {ex.Message}");
            }
        }

        public async Task<IActionResult> Index(string searchString, int pageNumber = 1, int pageSize = 20)
        {
            var products = await _productService.GetAllProductsAsync();
            if (!string.IsNullOrEmpty(searchString))
            {
                products = products.Where(p => p.Name.ToLower().Contains(searchString.ToLower())).ToList();
            }

            int totalItems = products.Count();
            int totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
            pageNumber = Math.Max(1, Math.Min(pageNumber, totalPages));

            var pagedProducts = products
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewData["CurrentPage"] = pageNumber;
            ViewData["TotalPages"] = totalPages;
            ViewData["PageSize"] = pageSize;
            ViewData["TotalItems"] = totalItems;
            ViewData["SearchString"] = searchString;

            return View(pagedProducts);
        }

        [HttpGet]
        public async Task<IActionResult> Add(int? currentPage)
        {
            var categories = await _categoryService.GetAllCategoriesAsync();
            var flowerTypes = await _inventoryService.GetAllFlowerTypesAsync();
            var presentationStyles = await _context.PresentationStyles.ToListAsync();

            ViewBag.Categories = new SelectList(categories, "Id", "Name");
            ViewBag.FlowerTypes = new SelectList(flowerTypes, "Id", "Name");
            ViewBag.PresentationStyles = new SelectList(presentationStyles, "Id", "Name");
            ViewData["CurrentPage"] = currentPage ?? 1;

            return View(new CreateProductViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> Add(CreateProductViewModel viewModel, IFormFile imageUrl, List<IFormFile> additionalImages, int currentPage)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    if (viewModel.Price <= 0)
                    {
                        ModelState.AddModelError("Price", "Giá sản phẩm phải lớn hơn 0.");
                        throw new Exception("Giá sản phẩm không hợp lệ.");
                    }

                    if (viewModel.CategoryId <= 0)
                    {
                        ModelState.AddModelError("CategoryId", "Vui lòng chọn một danh mục hợp lệ.");
                        throw new Exception("Danh mục không hợp lệ.");
                    }

                    if (!viewModel.FlowerTypes.Any())
                    {
                        ModelState.AddModelError("FlowerTypes", "Vui lòng chọn ít nhất một loại hoa.");
                        throw new Exception("Chưa chọn loại hoa nào.");
                    }

                    var product = new Product
                    {
                        Name = viewModel.Name,
                        Description = viewModel.Description,
                        Price = viewModel.Price,
                        Quantity = viewModel.Quantity,
                        PresentationStyleId = viewModel.PresentationStyleId, // Giữ giá trị do admin chọn
                        IsActive = viewModel.IsActive,
                        IsNew = viewModel.IsNew,
                        CategoryId = viewModel.CategoryId,
                        CreatedDate = DateTime.Now,
                        QuantitySold = 0,
                        Images = new List<ProductImage>(),
                        FlowerTypeProducts = new List<FlowerTypeProduct>()
                    };

                    if (imageUrl != null && imageUrl.Length > 0)
                    {
                        var imagePath = await SaveImage(imageUrl);
                        product.ImageUrl = imagePath;

                        // Phân tích hình ảnh để lấy màu sắc
                        var fullImagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", imagePath.TrimStart('/'));
                        var analysisResult = await AnalyzeImage(fullImagePath);
                        if (analysisResult.Success)
                        {
                            product.Colors = System.Text.Json.JsonSerializer.Serialize(analysisResult.Colors);
                        }
                        else
                        {
                            ModelState.AddModelError("imageUrl", $"Không thể phân tích hình ảnh: {analysisResult.Message}");
                            throw new Exception($"Phân tích hình ảnh thất bại: {analysisResult.Message}");
                        }
                    }
                    else
                    {
                        ModelState.AddModelError("imageUrl", "Vui lòng tải lên hình ảnh chính.");
                        throw new Exception("Chưa tải lên hình ảnh chính.");
                    }

                    if (additionalImages != null && additionalImages.Any())
                    {
                        foreach (var image in additionalImages)
                        {
                            if (image != null && image.Length > 0)
                            {
                                var imgPath = await SaveImage(image);
                                product.Images.Add(new ProductImage { Url = imgPath });
                            }
                        }
                    }

                    foreach (var flowerType in viewModel.FlowerTypes)
                    {
                        var existingFlowerType = await _context.FlowerTypes
                            .Include(ft => ft.BatchFlowerTypes)
                            .ThenInclude(bft => bft.Batch)
                            .FirstOrDefaultAsync(ft => ft.Id == flowerType.FlowerTypeId);
                        if (existingFlowerType == null)
                        {
                            throw new Exception($"Loại hoa với ID {flowerType.FlowerTypeId} không tồn tại.");
                        }

                        int totalFlowersNeeded = flowerType.Quantity * viewModel.Quantity;
                        if (existingFlowerType.Quantity < totalFlowersNeeded)
                        {
                            throw new Exception($"Số lượng tồn kho của {existingFlowerType.Name} không đủ. Cần {totalFlowersNeeded} bông, nhưng chỉ có {existingFlowerType.Quantity} trong kho.");
                        }

                        var batchFlowerTypes = existingFlowerType.BatchFlowerTypes?
                            .Where(bft => bft.CurrentQuantity > 0 && bft.Batch != null && bft.Batch.ExpiryDate > DateTime.Now)
                            .OrderBy(bft => bft.Batch.ExpiryDate)
                            .ToList() ?? new List<BatchFlowerType>();
                        if (!batchFlowerTypes.Any())
                        {
                            var debugInfo = existingFlowerType.BatchFlowerTypes?
                                .Select(bft => new
                                {
                                    bft.FlowerTypeId,
                                    bft.CurrentQuantity,
                                    BatchExists = bft.Batch != null,
                                    ExpiryDate = bft.Batch?.ExpiryDate
                                })
                                .ToList();
                            throw new Exception($"Không có lô hoa nào còn hạn sử dụng cho {existingFlowerType.Name}. Debug Info: {System.Text.Json.JsonSerializer.Serialize(debugInfo)}");
                        }

                        int remainingFlowersNeeded = totalFlowersNeeded;
                        foreach (var bft in batchFlowerTypes)
                        {
                            if (remainingFlowersNeeded <= 0) break;
                            int flowersToReduce = Math.Min(remainingFlowersNeeded, bft.CurrentQuantity);
                            bft.CurrentQuantity -= flowersToReduce;
                            remainingFlowersNeeded -= flowersToReduce;
                            _context.BatchFlowerTypes.Update(bft);
                        }

                        if (remainingFlowersNeeded > 0)
                        {
                            throw new Exception($"Không đủ hoa trong các lô còn hạn sử dụng cho {existingFlowerType.Name}.");
                        }

                        await _inventoryService.ExportInventoryAsync(
                            flowerType.FlowerTypeId,
                            totalFlowersNeeded,
                            $"Dùng để tạo {viewModel.Quantity} sản phẩm {viewModel.Name} ({flowerType.Quantity} bông/bó)",
                            User.Identity.Name ?? "Admin",
                            null
                        );

                        product.FlowerTypeProducts.Add(new FlowerTypeProduct
                        {
                            FlowerTypeId = flowerType.FlowerTypeId,
                            Quantity = flowerType.Quantity
                        });
                    }

                    await _productService.AddProductAsync(product);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Sản phẩm đã thêm thành công!";
                    return RedirectToAction(nameof(Index), new { pageNumber = currentPage });
                }
                catch (Exception ex)
                {
                    var innerExceptionMessage = ex.InnerException?.Message ?? ex.Message;
                    ModelState.AddModelError("", $"Đã xảy ra lỗi: {innerExceptionMessage}");
                }
            }

            var categories = await _categoryService.GetAllCategoriesAsync();
            var flowerTypes = await _inventoryService.GetAllFlowerTypesAsync();
            var presentationStyles = await _context.PresentationStyles.ToListAsync();
            ViewBag.Categories = new SelectList(categories, "Id", "Name", viewModel.CategoryId);
            ViewBag.FlowerTypes = new SelectList(flowerTypes, "Id", "Name");
            ViewBag.PresentationStyles = new SelectList(presentationStyles, "Id", "Name", viewModel.PresentationStyleId);
            ViewData["CurrentPage"] = currentPage;

            return View(viewModel);
        }

        private async Task<string> SaveImage(IFormFile image)
        {
            if (image == null || image.Length == 0)
            {
                return null;
            }

            var fileName = Guid.NewGuid().ToString() + Path.GetExtension(image.FileName);
            var savePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images", fileName);

            using (var fileStream = new FileStream(savePath, FileMode.Create))
            {
                await image.CopyToAsync(fileStream);
            }

            return "/images/" + fileName;
        }

        [HttpGet]
        public async Task<IActionResult> Display(int id, int? currentPage)
        {
            var product = await _productService.GetProductByIdAsync(id);
            if (product == null)
            {
                return NotFound();
            }
            ViewData["CurrentPage"] = currentPage ?? 1;
            return View(product);
        }

        [HttpGet]
        public async Task<IActionResult> Update(int id, int? currentPage)
        {
            var product = await _productService.GetProductByIdAsync(id);
            if (product == null)
            {
                return NotFound();
            }

            var viewModel = new CreateProductViewModel
            {
                Id = product.Id,
                Name = product.Name,
                Description = product.Description,
                Price = product.Price,
                Quantity = product.Quantity,
                PresentationStyleId = product.PresentationStyleId,
                IsActive = product.IsActive,
                IsNew = product.IsNew,
                CategoryId = product.CategoryId,
                ImageUrl = product.ImageUrl, // Ánh xạ ảnh chính
                AdditionalImageUrls = product.Images?.Select(img => img.Url).ToList() ?? new List<string>(), // Ánh xạ ảnh phụ
                FlowerTypes = product.FlowerTypeProducts?.Select(ftp => new FlowerTypeSelection
                {
                    FlowerTypeId = ftp.FlowerTypeId,
                    Quantity = ftp.Quantity
                }).ToList() ?? new List<FlowerTypeSelection>()
            };

            var categories = await _categoryService.GetAllCategoriesAsync();
            var flowerTypes = await _inventoryService.GetAllFlowerTypesAsync();
            var presentationStyles = await _context.PresentationStyles.ToListAsync();

            ViewBag.Categories = new SelectList(categories, "Id", "Name", viewModel.CategoryId);
            ViewBag.FlowerTypes = new SelectList(flowerTypes, "Id", "Name");
            ViewBag.PresentationStyles = new SelectList(presentationStyles, "Id", "Name", viewModel.PresentationStyleId);
            ViewData["CurrentPage"] = currentPage ?? 1;

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Update(int id, CreateProductViewModel viewModel, IFormFile imageUrl, List<IFormFile> additionalImages, int? currentPage)
        {
            ModelState.Remove("ImageUrl");
            if (id != viewModel.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var existingProduct = await _productService.GetProductByIdAsync(id);
                    if (existingProduct == null)
                    {
                        return NotFound();
                    }

                    if (viewModel.Price <= 0)
                    {
                        ModelState.AddModelError("Price", "Giá sản phẩm phải lớn hơn 0.");
                        throw new Exception("Giá sản phẩm không hợp lệ.");
                    }

                    if (viewModel.CategoryId <= 0)
                    {
                        ModelState.AddModelError("CategoryId", "Vui lòng chọn một danh mục hợp lệ.");
                        throw new Exception("Danh mục không hợp lệ.");
                    }

                    if (!viewModel.FlowerTypes.Any())
                    {
                        ModelState.AddModelError("FlowerTypes", "Vui lòng chọn ít nhất một loại hoa.");
                        throw new Exception("Chưa chọn loại hoa nào.");
                    }

                    existingProduct.Name = viewModel.Name;
                    existingProduct.Description = viewModel.Description;
                    existingProduct.Price = viewModel.Price;
                    existingProduct.Quantity = viewModel.Quantity;
                    existingProduct.PresentationStyleId = viewModel.PresentationStyleId;
                    existingProduct.IsActive = viewModel.IsActive;
                    existingProduct.IsNew = viewModel.IsNew;
                    existingProduct.CategoryId = viewModel.CategoryId;

                    if (imageUrl != null && imageUrl.Length > 0)
                    {
                        var imagePath = await SaveImage(imageUrl);
                        existingProduct.ImageUrl = imagePath;

                        // Phân tích hình ảnh để lấy màu sắc
                        var fullImagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", imagePath.TrimStart('/'));
                        var analysisResult = await AnalyzeImage(fullImagePath);
                        if (analysisResult.Success)
                        {
                            existingProduct.Colors = System.Text.Json.JsonSerializer.Serialize(analysisResult.Colors);
                        }
                        else
                        {
                            ModelState.AddModelError("imageUrl", $"Không thể phân tích hình ảnh: {analysisResult.Message}");
                            throw new Exception($"Phân tích hình ảnh thất bại: {analysisResult.Message}");
                        }
                    }

                    if (additionalImages != null && additionalImages.Any())
                    {
                        existingProduct.Images.Clear();
                        foreach (var image in additionalImages)
                        {
                            if (image != null && image.Length > 0)
                            {
                                var imgPath = await SaveImage(image);
                                existingProduct.Images.Add(new ProductImage { Url = imgPath });
                            }
                        }
                    }

                    var existingFlowerTypeProducts = await _context.FlowerTypeProducts
                        .Where(ftp => ftp.ProductId == id)
                        .ToListAsync();
                    var oldQuantities = existingFlowerTypeProducts.ToDictionary(ftp => ftp.FlowerTypeId, ftp => ftp.Quantity * existingProduct.Quantity);

                    _context.FlowerTypeProducts.RemoveRange(existingFlowerTypeProducts);

                    var newFlowerTypeProducts = new List<FlowerTypeProduct>();
                    foreach (var flowerType in viewModel.FlowerTypes)
                    {
                        var existingFlowerType = await _context.FlowerTypes
                            .Include(ft => ft.BatchFlowerTypes)
                            .ThenInclude(bft => bft.Batch)
                            .FirstOrDefaultAsync(ft => ft.Id == flowerType.FlowerTypeId);
                        if (existingFlowerType == null)
                        {
                            throw new Exception($"Loại hoa với ID {flowerType.FlowerTypeId} không tồn tại.");
                        }

                        int totalFlowersNeeded = flowerType.Quantity * viewModel.Quantity;
                        int oldTotalFlowers = oldQuantities.ContainsKey(flowerType.FlowerTypeId) ? oldQuantities[flowerType.FlowerTypeId] : 0;
                        int flowerDifference = totalFlowersNeeded - oldTotalFlowers;

                        if (flowerDifference > 0)
                        {
                            if (existingFlowerType.Quantity < flowerDifference)
                            {
                                throw new Exception($"Số lượng tồn kho của {existingFlowerType.Name} không đủ. Cần {flowerDifference} bông, nhưng chỉ có {existingFlowerType.Quantity} trong kho.");
                            }

                            var batchFlowerTypes = existingFlowerType.BatchFlowerTypes
                                .Where(bft => bft.CurrentQuantity > 0 && bft.Batch != null && bft.Batch.ExpiryDate > DateTime.Now)
                                .OrderBy(bft => bft.Batch.ExpiryDate)
                                .ToList();
                            int remainingFlowersNeeded = flowerDifference;
                            foreach (var bft in batchFlowerTypes)
                            {
                                if (remainingFlowersNeeded <= 0) break;
                                int flowersToReduce = Math.Min(remainingFlowersNeeded, bft.CurrentQuantity);
                                bft.CurrentQuantity -= flowersToReduce;
                                remainingFlowersNeeded -= flowersToReduce;
                                _context.BatchFlowerTypes.Update(bft);
                            }

                            if (remainingFlowersNeeded > 0)
                            {
                                throw new Exception($"Không đủ hoa trong các lô còn hạn sử dụng cho {existingFlowerType.Name}.");
                            }

                            await _inventoryService.ExportInventoryAsync(
                                flowerType.FlowerTypeId,
                                flowerDifference,
                                $"Cập nhật sản phẩm {viewModel.Name} ({flowerType.Quantity} bông/bó)",
                                User.Identity.Name ?? "Admin",
                                null
                            );
                        }
                        else if (flowerDifference < 0)
                        {
                            int flowersToAdd = -flowerDifference; // Số hoa cần cộng lại
                            var batchFlowerTypes = existingFlowerType.BatchFlowerTypes
                                .Where(bft => bft.Batch != null && bft.Batch.ExpiryDate > DateTime.Now)
                                .OrderBy(bft => bft.Batch.ExpiryDate)
                                .ToList();
                            int remainingFlowersToAdd = flowersToAdd;
                            foreach (var bft in batchFlowerTypes)
                            {
                                if (remainingFlowersToAdd <= 0) break;
                                int flowersToIncrease = Math.Min(remainingFlowersToAdd, flowersToAdd); // Tăng tối đa số hoa cần
                                bft.CurrentQuantity += flowersToIncrease;
                                remainingFlowersToAdd -= flowersToIncrease;
                                _context.BatchFlowerTypes.Update(bft);
                            }

                            if (remainingFlowersToAdd > 0)
                            {
                                throw new Exception($"Không thể cộng lại đủ hoa vào các lô còn hạn sử dụng cho {existingFlowerType.Name}.");
                            }

                            await _inventoryService.ImportFlowerTypeAsync(
                                flowerType.FlowerTypeId,
                                flowersToAdd,
                                $"Hoàn lại từ cập nhật sản phẩm {viewModel.Name} ({flowerType.Quantity} bông/bó)",
                                User.Identity.Name ?? "Admin",
                                null,
                                null
                            );
                        }

                        newFlowerTypeProducts.Add(new FlowerTypeProduct
                        {
                            ProductId = id,
                            FlowerTypeId = flowerType.FlowerTypeId,
                            Quantity = flowerType.Quantity
                        });
                    }

                    var duplicateFlowerTypes = newFlowerTypeProducts
                        .GroupBy(ftp => ftp.FlowerTypeId)
                        .Where(g => g.Count() > 1)
                        .Select(g => g.Key)
                        .ToList();
                    if (duplicateFlowerTypes.Any())
                    {
                        throw new Exception($"Danh sách loại hoa chứa loại hoa trùng lặp: {string.Join(", ", duplicateFlowerTypes)}.");
                    }

                    existingProduct.FlowerTypeProducts = newFlowerTypeProducts;

                    await _productService.UpdateProductAsync(existingProduct);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Sản phẩm đã được cập nhật thành công!";
                    return RedirectToAction(nameof(Index), new { pageNumber = currentPage });
                }
                catch (Exception ex)
                {
                    var innerExceptionMessage = ex.InnerException?.Message ?? ex.Message;
                    ModelState.AddModelError("", $"Đã xảy ra lỗi: {innerExceptionMessage}");
                }
            }

            var categories = await _categoryService.GetAllCategoriesAsync();
            var flowerTypes = await _inventoryService.GetAllFlowerTypesAsync();
            ViewBag.Categories = new SelectList(categories, "Id", "Name", viewModel.CategoryId);
            ViewBag.FlowerTypes = new SelectList(flowerTypes, "Id", "Name");
            ViewData["CurrentPage"] = currentPage;

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> Delete(int id, int? currentPage)
        {
            var product = await _productService.GetProductByIdAsync(id);
            if (product == null)
            {
                return NotFound();
            }
            ViewData["CurrentPage"] = currentPage ?? 1;
            return View(product);
        }

        [HttpPost, ActionName("DeleteConfirmed")]
        public async Task<IActionResult> DeleteConfirmed(int id, int currentPage)
        {
            var product = await _productService.GetProductByIdAsync(id);
            if (product == null)
            {
                return NotFound();
            }

            // Lấy danh sách FlowerTypeProducts của sản phẩm
            var flowerTypeProducts = await _context.FlowerTypeProducts
                .Where(ftp => ftp.ProductId == id)
                .Include(ftp => ftp.FlowerType)
                .ThenInclude(ft => ft.BatchFlowerTypes)
                .ThenInclude(bft => bft.Batch)
                .ToListAsync();

            // Cộng lại số lượng hoa vào kho
            foreach (var ftp in flowerTypeProducts)
            {
                int totalFlowersUsed = ftp.Quantity * product.Quantity;
                var existingFlowerType = ftp.FlowerType;
                if (existingFlowerType != null)
                {
                    var batchFlowerTypes = existingFlowerType.BatchFlowerTypes
                        .Where(bft => bft.Batch != null && bft.Batch.ExpiryDate > DateTime.Now)
                        .OrderBy(bft => bft.Batch.ExpiryDate)
                        .ToList();
                    int remainingFlowersToAdd = totalFlowersUsed;
                    foreach (var bft in batchFlowerTypes)
                    {
                        if (remainingFlowersToAdd <= 0) break;
                        int flowersToIncrease = Math.Min(remainingFlowersToAdd, totalFlowersUsed); // Tăng tối đa số hoa cần
                        bft.CurrentQuantity += flowersToIncrease;
                        remainingFlowersToAdd -= flowersToIncrease;
                        _context.BatchFlowerTypes.Update(bft);
                    }

                    if (remainingFlowersToAdd > 0)
                    {
                        // Log lỗi nếu không thể cộng đủ (có thể bỏ qua nếu không ảnh hưởng lớn)
                        Console.WriteLine($"Không thể cộng lại đủ {remainingFlowersToAdd} hoa cho {existingFlowerType.Name} khi xóa sản phẩm.");
                    }

                    await _inventoryService.ImportFlowerTypeAsync(
                        ftp.FlowerTypeId,
                        totalFlowersUsed,
                        $"Hoàn lại từ xóa sản phẩm {product.Name}",
                        User.Identity.Name ?? "Admin",
                        null,
                        null
                    );
                }
            }

            await _productService.DeleteProductAsync(id);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Sản phẩm đã được xóa thành công!";
            return RedirectToAction(nameof(Index), new { pageNumber = currentPage });
        }

        [HttpGet]
        public async Task<IActionResult> GetProductsByCategory(int categoryId)
        {
            var products = await _productService.GetAllProductsAsync();
            var filteredProducts = products.Where(p => p.CategoryId == categoryId).ToList();
            return PartialView("_ProductsByCategoryPartial", filteredProducts);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateProductColors()
        {
            var products = await _context.Products
                .Where(p => string.IsNullOrEmpty(p.Colors) && !string.IsNullOrEmpty(p.ImageUrl))
                .ToListAsync();

            foreach (var product in products)
            {
                try
                {
                    var fullImagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", product.ImageUrl.TrimStart('/'));
                    if (System.IO.File.Exists(fullImagePath))
                    {
                        var analysisResult = await AnalyzeImage(fullImagePath);
                        if (analysisResult.Success)
                        {
                            product.Colors = System.Text.Json.JsonSerializer.Serialize(analysisResult.Colors);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Lỗi khi cập nhật màu cho sản phẩm {product.Id}: {ex.Message}");
                }
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Đã cập nhật màu sắc cho các sản phẩm!";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> ExportToExcel(string searchString)
        {
            try
            {
                var products = await _productService.GetAllProductsAsync();
                if (!string.IsNullOrEmpty(searchString))
                {
                    products = products.Where(p => p.Name.ToLower().Contains(searchString.ToLower())).ToList();
                }

                ExcelPackage.LicenseContext = LicenseContext.NonCommercial; // Cần nếu không có license thương mại
                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("Danh sách sản phẩm");

                    // Tiêu đề
                    worksheet.Cells[1, 1].Value = "Danh sách sản phẩm";
                    worksheet.Cells[1, 1, 1, 7].Merge = true;
                    worksheet.Cells[1, 1].Style.Font.Bold = true;
                    worksheet.Cells[1, 1].Style.Font.Size = 14;
                    worksheet.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[1, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[1, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(220, 220, 220));

                    // Header
                    worksheet.Cells[2, 1].Value = "STT";
                    worksheet.Cells[2, 2].Value = "Sản phẩm";
                    worksheet.Cells[2, 3].Value = "Giá";
                    worksheet.Cells[2, 4].Value = "Kiểu";
                    worksheet.Cells[2, 5].Value = "Danh mục";
                    worksheet.Cells[2, 6].Value = "Số lượng";
                    worksheet.Cells[2, 7].Value = "Ảnh";
                    worksheet.Cells[2, 1, 2, 7].Style.Font.Bold = true;
                    worksheet.Cells[2, 1, 2, 7].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[2, 1, 2, 7].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                    worksheet.Cells[2, 1, 2, 7].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                    int row = 3;
                    int index = 1;
                    foreach (var product in products)
                    {
                        worksheet.Cells[row, 1].Value = index++;
                        worksheet.Cells[row, 2].Value = product.Name;
                        worksheet.Cells[row, 3].Value = product.Price.ToString("N0") + " đ";
                        worksheet.Cells[row, 4].Value = product.PresentationStyle?.Name ?? "";
                        worksheet.Cells[row, 5].Value = product.Category?.Name ?? "";
                        worksheet.Cells[row, 6].Value = product.Quantity;
                        worksheet.Cells[row, 7].Value = !string.IsNullOrEmpty(product.ImageUrl) ? "Có" : "Không";
                        worksheet.Cells[row, 1, row, 7].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        row++;
                    }

                    // Auto fit columns
                    worksheet.Cells[2, 1, row - 1, 7].AutoFitColumns();

                    var stream = new MemoryStream(package.GetAsByteArray());
                    return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"DanhSachSanPham_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Lỗi khi xuất báo cáo Excel: {ex.Message}";
                return RedirectToAction(nameof(Index), new { searchString });
            }
        }
    }
}