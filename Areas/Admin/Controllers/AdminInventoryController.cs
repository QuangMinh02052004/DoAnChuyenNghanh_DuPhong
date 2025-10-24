using Bloomie.Areas.Admin.Models;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Models.ViewModels;
using Bloomie.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml.Style;
using OfficeOpenXml;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminInventoryController : Controller
    {
        private readonly IInventoryService _inventoryService;
        private readonly IProductService _productService;
        private readonly ApplicationDbContext _context;

        public AdminInventoryController(IInventoryService inventoryService, IProductService productService, ApplicationDbContext context)
        {
            _inventoryService = inventoryService;
            _productService = productService;
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int pageNumber = 1, int pageSize = 10, string supplierName = null, string flowerTypeName = null)
        {
            // Tạo query ban đầu
            var batchesQuery = _context.Batches
                .Include(b => b.BatchFlowerTypes)
                .ThenInclude(bft => bft.FlowerType)
                .Include(b => b.Supplier)
                .AsQueryable();

            // Lọc theo nhà cung cấp
            if (!string.IsNullOrEmpty(supplierName))
            {
                batchesQuery = batchesQuery.Where(b => b.Supplier.Name.Contains(supplierName));
            }

            // Lọc theo tên loại hoa trước khi sắp xếp
            if (!string.IsNullOrEmpty(flowerTypeName))
            {
                batchesQuery = batchesQuery.Where(b => b.BatchFlowerTypes.Any(bft => bft.FlowerType.Name.Contains(flowerTypeName) && bft.FlowerType.IsActive));
            }

            // Sắp xếp sau khi lọc
            batchesQuery = batchesQuery.OrderByDescending(b => b.ImportDate);

            // Lấy dữ liệu
            var batches = await batchesQuery.ToListAsync();

            // Lọc các lô có ít nhất một loại hoa đang hoạt động
            var filteredBatches = batches
                .Where(b => b.BatchFlowerTypes.Any(bft => bft.FlowerType.IsActive))
                .ToList();

            // Lấy thông tin người nhập lô từ InventoryTransaction
            var batchIds = filteredBatches.Select(b => b.Id).ToList();
            var importTransactions = await _context.InventoryTransactions
                .Where(t => batchIds.Contains(t.BatchId.Value) && t.TransactionType == TransactionType.Import)
                .GroupBy(t => t.BatchId)
                .Select(g => new
                {
                    BatchId = g.Key,
                    CreatedBy = g.OrderBy(t => t.TransactionDate).FirstOrDefault().CreatedBy
                })
                .ToListAsync();

            // Tạo ViewModel để truyền dữ liệu
            var batchViewModels = filteredBatches.Select(b => new
            {
                Batch = b,
                CreatedBy = importTransactions.FirstOrDefault(t => t.BatchId == b.Id)?.CreatedBy ?? "N/A"
            }).ToList();

            int totalItems = batchViewModels.Count;
            var pagedBatchViewModels = batchViewModels
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewData["CurrentPage"] = pageNumber;
            ViewData["TotalPages"] = (int)Math.Ceiling(totalItems / (double)pageSize);
            ViewData["PageSize"] = pageSize;
            ViewData["SupplierName"] = supplierName;
            ViewData["FlowerTypeName"] = flowerTypeName;

            return View(pagedBatchViewModels);
        }

        [HttpGet]
        public async Task<IActionResult> ImportFlower(int flowerTypeId, int? currentPage)
        {
            try
            {
                // Kiểm tra FlowerType
                var flowerType = await _context.FlowerTypes
                    .FirstOrDefaultAsync(ft => ft.Id == flowerTypeId && ft.IsActive);
                if (flowerType == null)
                {
                    TempData["ErrorMessage"] = "Loại hoa không tồn tại.";
                    return RedirectToAction(nameof(FlowerTypes));
                }

                // Lấy danh sách nhà cung cấp và chuyển thành List<dynamic>
                var suppliers = await _context.Suppliers
                    .Where(s => s.IsActive)
                    .Select(s => new { s.Id, s.Name })
                    .ToListAsync();

                if (suppliers == null || !suppliers.Any())
                {
                    TempData["ErrorMessage"] = "Không có nhà cung cấp nào đang hoạt động. Vui lòng thêm nhà cung cấp trước khi nhập kho.";
                    return RedirectToAction(nameof(FlowerTypes));
                }

                var suppliersDynamic = suppliers
                    .Select(s => (dynamic)new { Id = s.Id, Name = s.Name })
                    .ToList();

                // Lấy danh sách lô chưa hết hạn từ nhà cung cấp
                var batchesList = await _context.Batches
                    .Include(b => b.Supplier)
                    .Where(b => b.ExpiryDate > DateTime.Now)
                    .Select(b => new
                    {
                        b.Id,
                        b.SupplierId,
                        SupplierName = b.Supplier.Name,
                        b.ImportDate,
                        b.ExpiryDate
                    })
                    .ToListAsync();

                var batchesDynamic = batchesList != null
                    ? batchesList.Select(b => (dynamic)new
                    {
                        Id = b.Id,
                        SupplierId = b.SupplierId,
                        SupplierName = b.SupplierName,
                        ImportDate = b.ImportDate,
                        ExpiryDate = b.ExpiryDate
                    }).ToList()
                    : new List<dynamic>();

                // Tạo view model
                var model = new ImportInventoryViewModel
                {
                    FlowerTypeId = flowerTypeId,
                    ProductName = flowerType.Name,
                    ImportDate = DateTime.Today,
                    ExpiryDate = DateTime.Today.AddDays(7),
                    UnitPrice = 0
                };

                ViewData["CurrentPage"] = currentPage ?? 1;
                ViewData["Suppliers"] = suppliersDynamic;
                ViewData["Batches"] = batchesDynamic;

                return View(model);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Có lỗi xảy ra: {ex.Message}";
                return RedirectToAction(nameof(FlowerTypes));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportFlower(ImportInventoryViewModel model, int currentPage)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    if (!await _context.Suppliers.AnyAsync(s => s.Id == model.SupplierId && s.IsActive))
                    {
                        ModelState.AddModelError("SupplierId", "Nhà cung cấp không hợp lệ.");
                    }

                    int? batchId = model.BatchId;
                    if (model.BatchId.HasValue && model.BatchId == -1) // Tạo lô mới
                    {
                        if (model.ImportDate > DateTime.Now)
                        {
                            ModelState.AddModelError("ImportDate", "Ngày nhập không được ở tương lai.");
                        }
                        if (model.ExpiryDate <= DateTime.Now)
                        {
                            ModelState.AddModelError("ExpiryDate", "Ngày hết hạn phải sau ngày hiện tại.");
                        }
                        if (model.UnitPrice < 0)
                        {
                            ModelState.AddModelError("UnitPrice", "Giá nhập không được âm.");
                        }

                        if (ModelState.IsValid)
                        {
                            var newBatch = new Batch
                            {
                                SupplierId = model.SupplierId.Value,
                                UnitPrice = model.UnitPrice,
                                ImportDate = model.ImportDate,
                                ExpiryDate = model.ExpiryDate
                            };
                            _context.Batches.Add(newBatch);
                            await _context.SaveChangesAsync();
                            batchId = newBatch.Id;

                            var batchFlowerType = new BatchFlowerType
                            {
                                BatchId = batchId.Value,
                                FlowerTypeId = model.FlowerTypeId,
                                InitialQuantity = model.Quantity,
                                CurrentQuantity = model.Quantity
                            };
                            _context.BatchFlowerTypes.Add(batchFlowerType);
                            await _context.SaveChangesAsync();
                        }
                    }
                    else if (model.BatchId.HasValue && model.BatchId != -1) // Chọn lô hiện có
                    {
                        var existingBatch = await _context.Batches
                            .Include(b => b.BatchFlowerTypes)
                            .FirstOrDefaultAsync(b => b.Id == model.BatchId);
                        if (existingBatch == null)
                        {
                            ModelState.AddModelError("BatchId", "Lô hàng không tồn tại.");
                        }
                        else
                        {
                            if (existingBatch.ExpiryDate <= DateTime.Now)
                            {
                                ModelState.AddModelError("BatchId", "Lô hàng đã hết hạn. Vui lòng chọn lô khác hoặc tạo lô mới.");
                            }
                            else
                            {
                                existingBatch.ImportDate = model.ImportDate;
                                existingBatch.ExpiryDate = model.ExpiryDate;
                                existingBatch.UnitPrice = model.UnitPrice;
                                _context.Batches.Update(existingBatch);

                                var batchFlowerType = existingBatch.BatchFlowerTypes
                                    .FirstOrDefault(bft => bft.FlowerTypeId == model.FlowerTypeId);
                                if (batchFlowerType != null)
                                {
                                    batchFlowerType.InitialQuantity += model.Quantity;
                                    batchFlowerType.CurrentQuantity += model.Quantity;
                                    _context.BatchFlowerTypes.Update(batchFlowerType);
                                }
                                else
                                {
                                    batchFlowerType = new BatchFlowerType
                                    {
                                        BatchId = model.BatchId.Value,
                                        FlowerTypeId = model.FlowerTypeId,
                                        InitialQuantity = model.Quantity,
                                        CurrentQuantity = model.Quantity
                                    };
                                    _context.BatchFlowerTypes.Add(batchFlowerType);
                                }
                                await _context.SaveChangesAsync();
                                batchId = existingBatch.Id;
                            }
                        }
                    }
                    else
                    {
                        ModelState.AddModelError("BatchId", "Vui lòng chọn lô hoặc tạo lô mới.");
                    }

                    if (ModelState.IsValid)
                    {
                        // Gọi service để cập nhật FlowerType.Quantity
                        await _inventoryService.ImportFlowerTypeAsync(
                            model.FlowerTypeId,
                            model.Quantity,
                            model.Reason,
                            User.Identity.Name,
                            model.SupplierId,
                            batchId
                        );
                        await _context.SaveChangesAsync(); // Đảm bảo lưu thay đổi vào cơ sở dữ liệu

                        TempData["SuccessMessage"] = $"Đã nhập kho {model.Quantity} {model.ProductName}.";
                        return RedirectToAction(nameof(FlowerTypes), new { pageNumber = currentPage });
                    }
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", $"Lỗi: {ex.Message}");
                }
            }

            var flowerType = await _context.FlowerTypes
                .FirstOrDefaultAsync(ft => ft.Id == model.FlowerTypeId && ft.IsActive);
            if (flowerType == null)
            {
                TempData["ErrorMessage"] = "Loại hoa không tồn tại.";
                return RedirectToAction(nameof(FlowerTypes));
            }
            model.ProductName = flowerType.Name;

            var suppliers = await _context.Suppliers
                .Where(s => s.IsActive)
                .Select(s => new { s.Id, s.Name })
                .ToListAsync();

            var suppliersDynamic = suppliers != null
                ? suppliers.Select(s => (dynamic)new { Id = s.Id, Name = s.Name }).ToList()
                : new List<dynamic>();

            var batchesList = await _context.Batches
                .Where(b => b.ExpiryDate > DateTime.Now)
                .Include(b => b.Supplier)
                .Select(b => new
                {
                    b.Id,
                    b.SupplierId,
                    SupplierName = b.Supplier.Name,
                    b.ImportDate,
                    b.ExpiryDate
                })
                .ToListAsync();

            var batchesDynamic = batchesList != null
                ? batchesList.Select(b => (dynamic)new
                {
                    Id = b.Id,
                    SupplierId = b.SupplierId,
                    SupplierName = b.SupplierName,
                    ImportDate = b.ImportDate,
                    ExpiryDate = b.ExpiryDate
                }).ToList()
                : new List<dynamic>();

            ViewData["CurrentPage"] = currentPage;
            ViewData["Suppliers"] = suppliersDynamic;
            ViewData["Batches"] = batchesDynamic;

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> FlowerTypes(int pageNumber = 1, int pageSize = 10, string searchName = null, string searchSupplier = null, string sortOrder = null)
        {
            var flowerTypesQuery = _context.FlowerTypes
                .Where(ft => ft.IsActive)
                .Include(ft => ft.Suppliers)
                .Include(ft => ft.BatchFlowerTypes)
                .AsQueryable();

            // Lọc theo tên loại hoa
            if (!string.IsNullOrEmpty(searchName))
            {
                flowerTypesQuery = flowerTypesQuery.Where(ft => ft.Name.Contains(searchName));
            }

            // Lọc theo nhà cung cấp
            if (!string.IsNullOrEmpty(searchSupplier))
            {
                flowerTypesQuery = flowerTypesQuery.Where(ft => ft.Suppliers.Any(s => s.Name.Contains(searchSupplier)));
            }

            // Sắp xếp
            switch (sortOrder)
            {
                case "quantity_asc":
                    flowerTypesQuery = flowerTypesQuery.OrderBy(ft => ft.Quantity);
                    break;
                case "quantity_desc":
                    flowerTypesQuery = flowerTypesQuery.OrderByDescending(ft => ft.Quantity);
                    break;
                default:
                    flowerTypesQuery = flowerTypesQuery.OrderBy(ft => ft.Name);
                    break;
            }

            int totalItems = await flowerTypesQuery.CountAsync();
            var pagedFlowerTypes = await flowerTypesQuery
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewData["CurrentPage"] = pageNumber;
            ViewData["TotalPages"] = (int)Math.Ceiling(totalItems / (double)pageSize);
            ViewData["PageSize"] = pageSize;
            ViewData["SearchName"] = searchName;
            ViewData["SearchSupplier"] = searchSupplier;
            ViewData["SortOrder"] = sortOrder;

            return View(pagedFlowerTypes);
        }

        [HttpGet]
        public IActionResult CreateFlowerType()
        {
            return View(new CreateFlowerTypeViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateFlowerType(CreateFlowerTypeViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var flowerType = new FlowerType
                {
                    Name = model.Name,
                    Quantity = 0,
                    IsActive = true
                };
                _context.FlowerTypes.Add(flowerType);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Đã thêm loại hoa {model.Name} thành công.";
                return RedirectToAction(nameof(FlowerTypes));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Lỗi: {ex.Message}");
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Adjust(int flowerTypeId, int? currentPage)
        {
            try
            {
                var flowerType = await _context.FlowerTypes
                    .FirstOrDefaultAsync(ft => ft.Id == flowerTypeId && ft.IsActive);
                if (flowerType == null)
                {
                    TempData["ErrorMessage"] = "Loại hoa không tồn tại.";
                    return RedirectToAction(nameof(FlowerTypes));
                }

                // Lấy danh sách lô hàng còn hạn sử dụng
                var availableBatches = await _context.Batches
                    .Include(b => b.BatchFlowerTypes)
                    .ThenInclude(bft => bft.FlowerType)
                    .Include(b => b.Supplier)
                    .Where(b => b.ExpiryDate > DateTime.Now && b.BatchFlowerTypes.Any(bft => bft.FlowerTypeId == flowerTypeId))
                    .ToListAsync();

                if (!availableBatches.Any())
                {
                    TempData["ErrorMessage"] = "Không có lô hàng nào còn hạn sử dụng cho loại hoa này.";
                    return RedirectToAction(nameof(FlowerTypes));
                }

                var model = new AdjustInventoryViewModel
                {
                    FlowerTypeId = flowerTypeId,
                    ProductName = flowerType.Name,
                    Quantity = 0
                };

                ViewData["CurrentPage"] = currentPage ?? 1;
                ViewData["CurrentStock"] = flowerType.Quantity;
                ViewData["AvailableBatches"] = availableBatches; // Truyền qua ViewData
                return View(model);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Có lỗi xảy ra: {ex.Message}";
                return RedirectToAction(nameof(FlowerTypes));
            }
        }

        [HttpGet]
        public async Task<IActionResult> DeleteFlowerType(int flowerTypeId, int pageNumber = 1)
        {
            var flowerType = await _context.FlowerTypes
                .FirstOrDefaultAsync(ft => ft.Id == flowerTypeId && ft.IsActive);
            if (flowerType == null)
            {
                TempData["ErrorMessage"] = "Loại hoa không tồn tại.";
                return RedirectToAction(nameof(FlowerTypes), new { pageNumber });
            }

            ViewData["CurrentPage"] = pageNumber;
            return View(flowerType);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteFlowerTypeConfirmed(int flowerTypeId, int pageNumber = 1)
        {
            var flowerType = await _context.FlowerTypes
                .Include(ft => ft.BatchFlowerTypes)
                .FirstOrDefaultAsync(ft => ft.Id == flowerTypeId && ft.IsActive);
            if (flowerType == null)
            {
                TempData["ErrorMessage"] = "Loại hoa không tồn tại.";
                return RedirectToAction(nameof(FlowerTypes), new { pageNumber });
            }

            // Kiểm tra ràng buộc khóa ngoại với InventoryTransactions
            var transactionsExist = await _context.InventoryTransactions
                .AnyAsync(t => t.BatchId.HasValue && t.Batch.BatchFlowerTypes.Any(bft => bft.FlowerTypeId == flowerTypeId));
            if (transactionsExist)
            {
                TempData["ErrorMessage"] = "Không thể xóa loại hoa vì đã có giao dịch liên quan.";
                return RedirectToAction(nameof(FlowerTypes), new { pageNumber });
            }

            try
            {
                // Xóa các bản ghi liên quan trong BatchFlowerType trước
                var batchFlowerTypes = await _context.BatchFlowerTypes
                    .Where(bft => bft.FlowerTypeId == flowerTypeId)
                    .ToListAsync();
                _context.BatchFlowerTypes.RemoveRange(batchFlowerTypes);

                // Xóa FlowerType
                _context.FlowerTypes.Remove(flowerType);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Đã xóa loại hoa {flowerType.Name} thành công.";
            }
            catch (DbUpdateException ex)
            {
                TempData["ErrorMessage"] = $"Lỗi khi xóa loại hoa: {ex.InnerException?.Message ?? ex.Message}";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Lỗi không xác định khi xóa loại hoa: {ex.Message}";
            }

            return RedirectToAction(nameof(FlowerTypes), new { pageNumber });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Adjust(AdjustInventoryViewModel model, int currentPage)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var flowerType = await _context.FlowerTypes
                        .Include(ft => ft.BatchFlowerTypes)
                        .FirstOrDefaultAsync(ft => ft.Id == model.FlowerTypeId && ft.IsActive);
                    if (flowerType == null)
                    {
                        TempData["ErrorMessage"] = "Loại hoa không tồn tại.";
                        return RedirectToAction(nameof(FlowerTypes));
                    }

                    if (flowerType.Quantity < model.Quantity && model.AdjustmentType == AdjustmentType.Damaged)
                    {
                        ModelState.AddModelError("Quantity", $"Số lượng tồn kho không đủ. Hiện tại chỉ có {flowerType.Quantity} {flowerType.Name}.");
                    }

                    var batch = await _context.Batches
                        .Include(b => b.BatchFlowerTypes)
                        .FirstOrDefaultAsync(b => b.Id == model.BatchId);
                    if (batch == null || batch.ExpiryDate <= DateTime.Now)
                    {
                        ModelState.AddModelError("BatchId", "Lô hàng không tồn tại hoặc đã hết hạn.");
                    }

                    if (ModelState.IsValid)
                    {
                        var batchFlowerType = batch.BatchFlowerTypes
                            .FirstOrDefault(bft => bft.FlowerTypeId == model.FlowerTypeId);
                        if (batchFlowerType != null)
                        {
                            if (model.AdjustmentType == AdjustmentType.Damaged)
                            {
                                if (batchFlowerType.CurrentQuantity < model.Quantity)
                                {
                                    ModelState.AddModelError("Quantity", $"Số lượng trong lô không đủ. Chỉ còn {batchFlowerType.CurrentQuantity} {flowerType.Name}.");
                                }
                                else
                                {
                                    batchFlowerType.CurrentQuantity -= model.Quantity;
                                }
                            }
                            else
                            {
                                batchFlowerType.CurrentQuantity += model.Quantity;
                            }
                            _context.BatchFlowerTypes.Update(batchFlowerType);
                        }

                        if (ModelState.IsValid)
                        {
                            await _inventoryService.AdjustFlowerTypeAsync(
                                model.FlowerTypeId,
                                model.Quantity,
                                model.AdjustmentType,
                                model.Reason,
                                User.Identity.Name,
                                model.BatchId.Value
                            );

                            TempData["SuccessMessage"] = $"Đã điều chỉnh kho: {(model.AdjustmentType == AdjustmentType.Damaged ? "Giảm" : "Tăng")} {model.Quantity} {model.ProductName}.";
                            return RedirectToAction(nameof(FlowerTypes), new { pageNumber = currentPage });
                        }
                    }
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Lỗi khi điều chỉnh kho: {ex.Message}";
                    ModelState.AddModelError("", $"Lỗi: {ex.Message}");
                }
            }

            var flowerTypeReload = await _context.FlowerTypes
                .FirstOrDefaultAsync(ft => ft.Id == model.FlowerTypeId && ft.IsActive);
            if (flowerTypeReload == null)
            {
                TempData["ErrorMessage"] = "Loại hoa không tồn tại.";
                return RedirectToAction(nameof(FlowerTypes));
            }

            var availableBatches = await _context.Batches
                .Include(b => b.BatchFlowerTypes)
                .ThenInclude(bft => bft.FlowerType)
                .Include(b => b.Supplier)
                .Where(b => b.ExpiryDate > DateTime.Now && b.BatchFlowerTypes.Any(bft => bft.FlowerTypeId == model.FlowerTypeId))
                .ToListAsync();

            if (!availableBatches.Any())
            {
                TempData["ErrorMessage"] = "Không có lô hàng nào còn hạn sử dụng cho loại hoa này.";
                return RedirectToAction(nameof(FlowerTypes));
            }

            model.ProductName = flowerTypeReload.Name;
            ViewData["CurrentPage"] = currentPage;
            ViewData["CurrentStock"] = flowerTypeReload.Quantity;
            ViewData["AvailableBatches"] = availableBatches;
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> History(int flowerTypeId, int? currentPage)
        {
            var flowerType = await _context.FlowerTypes.FindAsync(flowerTypeId);
            if (flowerType == null)
            {
                return NotFound();
            }

            var transactions = await _inventoryService.GetInventoryHistoryAsync(flowerTypeId);
            ViewData["ProductName"] = flowerType.Name;
            ViewData["CurrentPage"] = currentPage ?? 1;
            return View(transactions.OrderByDescending(t => t.TransactionDate).ToList());
        }

        [HttpGet]
        public async Task<IActionResult> Details(int batchId)
        {
            var batch = await _context.Batches
                .Include(b => b.Supplier)
                .Include(b => b.BatchFlowerTypes)
                .ThenInclude(bft => bft.FlowerType)
                .FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null)
            {
                TempData["ErrorMessage"] = "Lô hàng không tồn tại.";
                return RedirectToAction(nameof(Index));
            }

            return View(batch);
        }

        [HttpGet]
        public async Task<IActionResult> Update(int batchId, int pageNumber = 1)
        {
            var batch = await _context.Batches
                .Include(b => b.Supplier)
                .FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null)
            {
                TempData["ErrorMessage"] = "Lô hàng không tồn tại.";
                return RedirectToAction(nameof(Index), new { pageNumber });
            }

            ViewData["CurrentPage"] = pageNumber;
            return View(batch);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(Batch batch, int pageNumber = 1)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var existingBatch = await _context.Batches
                        .FirstOrDefaultAsync(b => b.Id == batch.Id);
                    if (existingBatch == null)
                    {
                        TempData["ErrorMessage"] = "Lô hàng không tồn tại.";
                        return RedirectToAction(nameof(Index), new { pageNumber });
                    }

                    existingBatch.ExpiryDate = batch.ExpiryDate;
                    existingBatch.UnitPrice = batch.UnitPrice;
                    _context.Batches.Update(existingBatch);
                    await _context.SaveChangesAsync();

                    TempData["SuccessMessage"] = "Đã cập nhật lô hàng thành công.";
                    return RedirectToAction(nameof(Index), new { pageNumber });
                }
                catch (Exception ex)
                {
                    TempData["ErrorMessage"] = $"Lỗi khi cập nhật lô hàng: {ex.Message}";
                }
            }
            ViewData["CurrentPage"] = pageNumber;
            return View(batch);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int batchId, int pageNumber = 1)
        {
            try
            {
                var batch = await _context.Batches
                    .Include(b => b.BatchFlowerTypes)
                    .FirstOrDefaultAsync(b => b.Id == batchId);

                if (batch == null)
                {
                    TempData["ErrorMessage"] = "Lô hàng không tồn tại.";
                    return RedirectToAction(nameof(Index), new { pageNumber });
                }

                var transactions = await _context.InventoryTransactions
                    .AnyAsync(t => t.BatchId == batchId);
                if (transactions)
                {
                    TempData["ErrorMessage"] = "Không thể xóa lô hàng vì đã có giao dịch liên quan.";
                    return RedirectToAction(nameof(Index), new { pageNumber });
                }

                foreach (var bft in batch.BatchFlowerTypes)
                {
                    var flowerType = await _context.FlowerTypes.FindAsync(bft.FlowerTypeId);
                    if (flowerType != null)
                    {
                        flowerType.Quantity -= bft.CurrentQuantity;
                        _context.FlowerTypes.Update(flowerType);
                    }
                }

                _context.Batches.Remove(batch);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Đã xóa lô hàng thành công.";
                return RedirectToAction(nameof(Index), new { pageNumber });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Lỗi khi xóa lô hàng: {ex.Message}";
                return RedirectToAction(nameof(Index), new { pageNumber });
            }
        }

        [HttpGet]
        public async Task<IActionResult> LowStock()
        {
            var flowerTypes = await _context.FlowerTypes
                .Where(ft => ft.IsActive && ft.Quantity <= 10) // Ngưỡng thấp là 10, có thể điều chỉnh
                .ToListAsync();
            return View(flowerTypes);
        }

        [HttpGet]
        public async Task<IActionResult> ExportToExcel(string supplierName = null, string flowerTypeName = null)
        {
            try
            {
                var batchesQuery = _context.Batches
                    .Include(b => b.BatchFlowerTypes)
                    .ThenInclude(bft => bft.FlowerType)
                    .Include(b => b.Supplier)
                    .AsQueryable();

                if (!string.IsNullOrEmpty(supplierName))
                {
                    batchesQuery = batchesQuery.Where(b => b.Supplier.Name.Contains(supplierName));
                }

                if (!string.IsNullOrEmpty(flowerTypeName))
                {
                    batchesQuery = batchesQuery.Where(b => b.BatchFlowerTypes.Any(bft => bft.FlowerType.Name.Contains(flowerTypeName) && bft.FlowerType.IsActive));
                }

                batchesQuery = batchesQuery.OrderByDescending(b => b.ImportDate);

                var batches = await batchesQuery.ToListAsync();

                var filteredBatches = batches
                    .Where(b => b.BatchFlowerTypes.Any(bft => bft.FlowerType.IsActive))
                    .ToList();

                var batchIds = filteredBatches.Select(b => b.Id).ToList();
                var importTransactions = await _context.InventoryTransactions
                    .Where(t => batchIds.Contains(t.BatchId.Value) && t.TransactionType == TransactionType.Import)
                    .GroupBy(t => t.BatchId)
                    .Select(g => new
                    {
                        BatchId = g.Key,
                        CreatedBy = g.OrderBy(t => t.TransactionDate).FirstOrDefault().CreatedBy
                    })
                    .ToListAsync();

                var batchViewModels = filteredBatches.Select(b => new
                {
                    Batch = b,
                    CreatedBy = importTransactions.FirstOrDefault(t => t.BatchId == b.Id)?.CreatedBy ?? "N/A"
                }).ToList();

                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("Danh sách tồn kho");

                    // Tiêu đề
                    worksheet.Cells[1, 1].Value = "DANH SÁCH TỒN KHO";
                    worksheet.Cells[1, 1, 1, 8].Merge = true;
                    worksheet.Cells[1, 1].Style.Font.Bold = true;
                    worksheet.Cells[1, 1].Style.Font.Size = 14;
                    worksheet.Cells[1, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[1, 1].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[1, 1].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(220, 220, 220));

                    // Header
                    worksheet.Cells[2, 1].Value = "STT";
                    worksheet.Cells[2, 2].Value = "Nhà cung cấp";
                    worksheet.Cells[2, 3].Value = "Loại hoa";
                    worksheet.Cells[2, 4].Value = "Ngày nhập";
                    worksheet.Cells[2, 5].Value = "Ngày hết hạn";
                    worksheet.Cells[2, 6].Value = "Đơn giá nhập";
                    worksheet.Cells[2, 7].Value = "Số lượng tồn";
                    worksheet.Cells[2, 8].Value = "Người nhập";
                    worksheet.Cells[2, 1, 2, 8].Style.Font.Bold = true;
                    worksheet.Cells[2, 1, 2, 8].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    worksheet.Cells[2, 1, 2, 8].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                    worksheet.Cells[2, 1, 2, 8].Style.Border.BorderAround(ExcelBorderStyle.Thin);

                    int row = 3;
                    int index = 1;
                    foreach (var batchVM in batchViewModels)
                    {
                        var batch = batchVM.Batch;
                        var flowerTypeNames = string.Join(", ", batch.BatchFlowerTypes
                            .Where(bft => bft.FlowerType.IsActive)
                            .Select(bft => bft.FlowerType.Name));
                        var totalQuantity = batch.BatchFlowerTypes
                            .Where(bft => bft.FlowerType.IsActive)
                            .Sum(bft => bft.CurrentQuantity); // Loại bỏ ?? 0 vì CurrentQuantity là int

                        worksheet.Cells[row, 1].Value = index++;
                        worksheet.Cells[row, 2].Value = batch.Supplier.Name;
                        worksheet.Cells[row, 3].Value = flowerTypeNames;
                        worksheet.Cells[row, 4].Value = batch.ImportDate.ToString("dd/MM/yyyy");
                        worksheet.Cells[row, 5].Value = batch.ExpiryDate.ToString("dd/MM/yyyy");
                        worksheet.Cells[row, 6].Value = batch.UnitPrice.ToString("#,##0") + " đ";
                        worksheet.Cells[row, 7].Value = totalQuantity.ToString("#,##0");
                        worksheet.Cells[row, 8].Value = batchVM.CreatedBy;
                        worksheet.Cells[row, 1, row, 8].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                        row++;
                    }

                    // Auto fit columns
                    worksheet.Cells[2, 1, row - 1, 8].AutoFitColumns();

                    var stream = new MemoryStream(package.GetAsByteArray());
                    return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"DanhSachLoHang_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Lỗi khi xuất báo cáo Excel: {ex.Message}";
                return RedirectToAction(nameof(Index), new { supplierName, flowerTypeName });
            }
        }
    }
}