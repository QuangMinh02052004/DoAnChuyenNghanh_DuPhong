using Bloomie.Areas.Admin.Models;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bloomie.Areas.Admin
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminPromotionController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminPromotionController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(int pageNumber = 1, int pageSize = 10)
        {
            var promotions = await _context.Promotions
                .Include(p => p.PromotionProducts)
                .ThenInclude(pp => pp.Product)
                .ToListAsync();

            // Tự động cập nhật trạng thái IsActive dựa trên ngày
            foreach (var promotion in promotions)
            {
                if (promotion.EndDate.Date < DateTime.Now.Date)
                {
                    promotion.IsActive = false; // Hết hạn thì đặt IsActive = false
                }
                else if (promotion.StartDate.Date <= DateTime.Now.Date && promotion.EndDate.Date >= DateTime.Now.Date)
                {
                    promotion.IsActive = true; // Đang trong thời gian hiệu lực thì đặt IsActive = true
                }
                _context.Update(promotion);
            }
            await _context.SaveChangesAsync();

            int totalItems = promotions.Count();
            int totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
            pageNumber = Math.Max(1, Math.Min(pageNumber, totalPages));

            var pagedPromotions = promotions
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewData["CurrentPage"] = pageNumber;
            ViewData["TotalPages"] = totalPages;
            ViewData["PageSize"] = pageSize;
            ViewData["TotalItems"] = totalItems;

            return View(pagedPromotions);
        }

        [HttpGet]
        public async Task<IActionResult> Add(int? currentPage)
        {
            ViewBag.Products = await _context.Products.ToListAsync();
            ViewData["CurrentPage"] = currentPage ?? 1;
            return View(new Promotion
            {
                StartDate = DateTime.Now.Date,
                EndDate = DateTime.Now.Date.AddDays(7).AddHours(23).AddMinutes(59).AddSeconds(59),
                PromotionType = PromotionType.Order // Mặc định là áp dụng cho hóa đơn
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(Promotion promotion, List<int> productIds, int currentPage)
        {
            if (string.IsNullOrWhiteSpace(promotion.Code))
            {
                ModelState.AddModelError("Code", "Mã khuyến mãi không được để trống.");
            }

            if (await _context.Promotions.AnyAsync(p => p.Code == promotion.Code))
            {
                ModelState.AddModelError("Code", "Mã khuyến mãi đã tồn tại.");
            }

            if (promotion.StartDate >= promotion.EndDate)
            {
                ModelState.AddModelError("EndDate", "Ngày kết thúc phải sau ngày bắt đầu.");
            }

            if (promotion.PromotionType == PromotionType.Product && (productIds == null || !productIds.Any()))
            {
                ModelState.AddModelError("PromotionProducts", "Vui lòng chọn ít nhất một sản phẩm khi áp dụng khuyến mãi cho sản phẩm.");
            }

            if (promotion.MinimumOrderValue.HasValue && promotion.MinimumOrderValue <= 0)
            {
                ModelState.AddModelError("MinimumOrderValue", "Giá trị đơn hàng tối thiểu phải lớn hơn 0.");
            }

            if (promotion.MaximumDiscountValue.HasValue && promotion.MaximumDiscountValue <= 0)
            {
                ModelState.AddModelError("MaximumDiscountValue", "Số tiền giảm tối đa phải lớn hơn 0.");
            }

            if (ModelState.IsValid)
            {
                // Cập nhật trạng thái dựa trên ngày
                promotion.IsActive = promotion.StartDate.Date <= DateTime.Now.Date && promotion.EndDate.Date >= DateTime.Now.Date;

                // Chỉ gán PromotionProducts nếu là khuyến mãi cho sản phẩm
                if (promotion.PromotionType == PromotionType.Product)
                {
                    promotion.PromotionProducts = productIds.Select(productId => new PromotionProduct
                    {
                        ProductId = productId,
                        Promotion = promotion
                    }).ToList();
                }
                else
                {
                    promotion.PromotionProducts = new List<PromotionProduct>(); // Để trống nếu là khuyến mãi cho hóa đơn
                }

                _context.Promotions.Add(promotion);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Chương trình khuyến mãi đã được tạo thành công!";
                return RedirectToAction(nameof(Index), new { pageNumber = currentPage });
            }

            ViewBag.Products = await _context.Products.ToListAsync();
            ViewData["CurrentPage"] = currentPage;
            return View(promotion);
        }

        [HttpGet]
        public async Task<IActionResult> Update(int id, int? currentPage)
        {
            var promotion = await _context.Promotions
                .Include(p => p.PromotionProducts)
                .ThenInclude(pp => pp.Product)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (promotion == null)
            {
                return NotFound();
            }

            // Cập nhật trạng thái trước khi hiển thị
            if (promotion.EndDate.Date < DateTime.Now.Date)
            {
                promotion.IsActive = false;
            }
            else if (promotion.StartDate.Date <= DateTime.Now.Date && promotion.EndDate.Date >= DateTime.Now.Date)
            {
                promotion.IsActive = true;
            }
            _context.Update(promotion);
            await _context.SaveChangesAsync();

            ViewBag.Products = await _context.Products.ToListAsync();
            ViewData["CurrentPage"] = currentPage ?? 1;
            return View(promotion);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(int id, Promotion promotion, List<int> productIds, int? currentPage)
        {
            if (id != promotion.Id)
            {
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(promotion.Code))
            {
                ModelState.AddModelError("Code", "Mã khuyến mãi không được để trống.");
            }

            if (await _context.Promotions.AnyAsync(p => p.Code == promotion.Code && p.Id != id))
            {
                ModelState.AddModelError("Code", "Mã khuyến mãi đã tồn tại.");
            }

            if (promotion.StartDate >= promotion.EndDate)
            {
                ModelState.AddModelError("EndDate", "Ngày kết thúc phải sau ngày bắt đầu.");
            }

            if (promotion.PromotionType == PromotionType.Product && (productIds == null || !productIds.Any()))
            {
                ModelState.AddModelError("PromotionProducts", "Vui lòng chọn ít nhất một sản phẩm khi áp dụng khuyến mãi cho sản phẩm.");
            }

            if (promotion.MinimumOrderValue.HasValue && promotion.MinimumOrderValue <= 0)
            {
                ModelState.AddModelError("MinimumOrderValue", "Giá trị đơn hàng tối thiểu phải lớn hơn 0.");
            }

            if (promotion.MaximumDiscountValue.HasValue && promotion.MaximumDiscountValue <= 0)
            {
                ModelState.AddModelError("MaximumDiscountValue", "Số tiền giảm tối đa phải lớn hơn 0.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var existingPromotion = await _context.Promotions
                        .Include(p => p.PromotionProducts)
                        .FirstOrDefaultAsync(p => p.Id == id);

                    if (existingPromotion == null)
                    {
                        return NotFound();
                    }

                    existingPromotion.Code = promotion.Code;
                    existingPromotion.DiscountPercentage = promotion.DiscountPercentage;
                    existingPromotion.StartDate = promotion.StartDate;
                    existingPromotion.EndDate = promotion.EndDate;
                    existingPromotion.PromotionType = promotion.PromotionType;
                    existingPromotion.MinimumOrderValue = promotion.MinimumOrderValue;
                    existingPromotion.MaximumDiscountValue = promotion.MaximumDiscountValue;

                    // Cập nhật trạng thái dựa trên ngày
                    existingPromotion.IsActive = promotion.StartDate.Date <= DateTime.Now.Date && promotion.EndDate.Date >= DateTime.Now.Date;

                    // Cập nhật PromotionProducts
                    _context.PromotionProducts.RemoveRange(existingPromotion.PromotionProducts);
                    if (promotion.PromotionType == PromotionType.Product)
                    {
                        existingPromotion.PromotionProducts = productIds.Select(productId => new PromotionProduct
                        {
                            ProductId = productId,
                            PromotionId = existingPromotion.Id
                        }).ToList();
                    }
                    else
                    {
                        existingPromotion.PromotionProducts = new List<PromotionProduct>();
                    }

                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Chương trình khuyến mãi đã được cập nhật thành công!";
                    return RedirectToAction(nameof(Index), new { pageNumber = currentPage });
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _context.Promotions.AnyAsync(p => p.Id == id))
                    {
                        return NotFound();
                    }
                    throw;
                }
            }

            ViewBag.Products = await _context.Products.ToListAsync();
            ViewData["CurrentPage"] = currentPage;
            return View(promotion);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, int? currentPage)
        {
            var promotion = await _context.Promotions
                .Include(p => p.PromotionProducts)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (promotion == null)
            {
                return NotFound();
            }

            _context.PromotionProducts.RemoveRange(promotion.PromotionProducts);
            _context.Promotions.Remove(promotion);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Chương trình khuyến mãi đã được xóa thành công!";
            return RedirectToAction(nameof(Index), new { pageNumber = currentPage });
        }
    }
}