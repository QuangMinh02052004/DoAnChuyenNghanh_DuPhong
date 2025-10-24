using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Bloomie.Models.Entities;
using Bloomie.Services.Interfaces;
using System.Security.Claims;
using Bloomie.Data;
using Bloomie.Models.ViewModels;
using Python.Runtime;
using System.Diagnostics;
using System.Text.Json;

namespace Bloomie.Controllers
{
    public class ProductController : Controller
    {
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly IPromotionService _promotionService;
        private readonly ApplicationDbContext _context;

        public static class HtmlHelpers
        {
            public static string GetStarRatingHtml(double rating)
            {
                return $"<i class='bi bi-star-fill'></i> x {rating}";
            }

            public static string GetColorHex(string colorName)
            {
                return colorName.ToLower() switch
                {
                    "đỏ" => "#FF0000",
                    "xanh" => "#00FF00",
                    "vàng" => "#FFFF00",
                    _ => "#CCCCCC"
                };
            }
        }

        public ProductController(IProductService productService, ICategoryService categoryService, IPromotionService promotionService, ApplicationDbContext context)
        {
            _productService = productService;
            _categoryService = categoryService;
            _promotionService = promotionService;
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? categoryId, string searchString, decimal? minPrice, decimal? maxPrice, string sortOrder, bool? isNew, string priceRange, decimal? customMinPrice, decimal? customMaxPrice, int skipCount = 0, string[] occasions = null, string[] objects = null, string[] presentations = null, string[] colors = null, string[] flowerTypes = null)
        {
            // Lấy danh sách danh mục và danh mục con
            var allCategories = await _categoryService.GetAllCategoriesAsync();
            var parentCategories = allCategories.Where(c => c.ParentCategoryId == null)
                                               .OrderBy(c => c.Name)
                                               .ToList();

            foreach (var category in parentCategories)
            {
                category.SubCategories = allCategories.Where(c => c.ParentCategoryId == category.Id)
                                                     .OrderBy(c => c.Name)
                                                     .ToList();
            }

            // Lấy danh sách kiểu trình bày
            var presentationStyles = await _context.PresentationStyles
                .Select(ps => ps.Name)
                .Distinct()
                .OrderBy(name => name)
                .ToListAsync();

            // Lưu thông tin bộ lọc vào ViewBag để hiển thị trên giao diện
            ViewBag.Categories = parentCategories ?? new List<Category>();
            ViewBag.CategoryId = categoryId?.ToString();
            ViewBag.SearchString = searchString;
            ViewBag.MinPrice = minPrice?.ToString();
            ViewBag.MaxPrice = maxPrice?.ToString();
            ViewBag.SortOrder = sortOrder;
            ViewBag.IsNew = isNew?.ToString();
            ViewBag.PriceRange = priceRange;
            ViewBag.CustomMinPrice = customMinPrice?.ToString();
            ViewBag.CustomMaxPrice = customMaxPrice?.ToString();
            ViewBag.Presentations = presentationStyles.Any() ? presentationStyles : new List<string> { "Không có kiểu trình bày" };
            ViewBag.SelectedPresentations = presentations?.Distinct().ToArray() ?? new string[] { };

            // Lấy danh mục "Chủ đề" và "Đối tượng"
            var themeParentCategory = parentCategories.FirstOrDefault(c => c.Name == "Chủ đề" || c.Name == "Occasion");
            var objectParentCategory = parentCategories.FirstOrDefault(c => c.Name == "Đối tượng" || c.Name == "Object");

            var themeSubCategories = themeParentCategory?.SubCategories?.Select(c => c.Name).ToList() ?? new List<string>();
            var objectSubCategories = objectParentCategory?.SubCategories?.Select(c => c.Name).ToList() ?? new List<string>();

            // Lấy danh sách sản phẩm và tính điểm đánh giá trung bình
            var products = await _productService.GetAllProductsAsync();
            products = products.Where(p => p.IsActive).ToList();

            var productsWithRating = new List<ProductViewModel.ProductWithRating>();
            foreach (var product in products)
            {
                var averageRating = (await _context.Ratings
                    .Where(r => r.ProductId == product.Id)
                    .Select(r => (decimal?)r.Star)
                    .ToListAsync()).Average() ?? 0;

                // Gán dịp và đối tượng dựa trên danh mục
                var productCategory = allCategories.FirstOrDefault(c => c.Id == product.CategoryId);
                string occasion = "Không xác định";
                string objectValue = "Không xác định";

                if (productCategory != null)
                {
                    var parentCategory = productCategory.ParentCategoryId.HasValue
                        ? allCategories.FirstOrDefault(c => c.Id == productCategory.ParentCategoryId.Value)
                        : productCategory;

                    if (parentCategory != null && parentCategory.Name == "Chủ đề")
                    {
                        occasion = productCategory.Name;
                    }
                    if (parentCategory != null && parentCategory.Name == "Đối tượng")
                    {
                        objectValue = productCategory.Name;
                    }
                }

                var flowerTypesForProduct = await GetFlowerTypesForProduct(product.Id);

                var productWithRating = new ProductViewModel.ProductWithRating
                {
                    Product = product,
                    Rating = averageRating,
                    Occasion = occasion,
                    Object = objectValue,
                    PresentationStyle = product.PresentationStyle?.Name ?? "Không xác định",
                    Colors = GetColorsForProduct(product.Id),
                    FlowerTypes = flowerTypesForProduct
                };
                productsWithRating.Add(productWithRating);
            }

            // Áp dụng bộ lọc danh mục
            var selectedOccasionsList = occasions?.ToList() ?? new List<string>();
            var selectedObjectsList = objects?.ToList() ?? new List<string>();

            if (categoryId.HasValue)
            {
                var selectedCategory = allCategories.FirstOrDefault(c => c.Id == categoryId.Value);
                if (selectedCategory != null)
                {
                    var parentCategory = selectedCategory.ParentCategoryId.HasValue
                        ? allCategories.FirstOrDefault(c => c.Id == selectedCategory.ParentCategoryId.Value)
                        : null;

                    if (parentCategory != null && parentCategory.Name == "Chủ đề" && !selectedOccasionsList.Contains(selectedCategory.Name))
                    {
                        selectedOccasionsList.Add(selectedCategory.Name);
                    }
                    if (parentCategory != null && parentCategory.Name == "Đối tượng" && !selectedObjectsList.Contains(selectedCategory.Name))
                    {
                        selectedObjectsList.Add(selectedCategory.Name);
                    }
                }
            }

            selectedOccasionsList = selectedOccasionsList.Distinct().ToList();
            selectedObjectsList = selectedObjectsList.Distinct().ToList();

            // Lưu thông tin bộ lọc vào ViewBag
            ViewBag.Occasions = themeSubCategories.Any() ? themeSubCategories : new List<string> { "Không có danh mục con" };
            ViewBag.Objects = objectSubCategories.Any() ? objectSubCategories : new List<string> { "Không có danh mục con" };
            ViewBag.Colors = productsWithRating.SelectMany(p => p.Colors)
                                              .Distinct()
                                              .Where(c => !string.IsNullOrEmpty(c) && c != "không xác định")
                                              .OrderBy(c => c)
                                              .ToList();
            ViewBag.FlowerTypes = await _context.FlowerTypes
                .Select(ft => ft.Name)
                .Distinct()
                .Where(name => !string.IsNullOrEmpty(name))
                .OrderBy(name => name)
                .ToListAsync();

            ViewBag.SelectedOccasions = selectedOccasionsList.ToArray();
            ViewBag.SelectedObjects = selectedObjectsList.ToArray();
            ViewBag.SelectedPresentations = presentations?.Distinct().ToArray() ?? new string[] { };
            ViewBag.SelectedColors = colors?.Distinct().ToArray() ?? new string[] { };
            ViewBag.SelectedFlowerTypes = flowerTypes?.Distinct().ToArray() ?? new string[] { };

            if (products == null)
            {
                products = new List<Product>();
            }

            // Áp dụng khuyến mãi
            var currentDate = DateTime.Now;
            var promotions = await _promotionService.GetAllPromotionsAsync();
            promotions = promotions.Where(p => p.IsActive).ToList();

            foreach (var product in products)
            {
                var applicablePromotions = promotions
                    .Where(p => p.PromotionProducts.Any(pp => pp.ProductId == product.Id))
                    .ToList();

                if (applicablePromotions.Any())
                {
                    var bestPromotion = applicablePromotions.OrderByDescending(p => p.DiscountPercentage).First();
                    product.DiscountPercentage = bestPromotion.DiscountPercentage;
                }
                else
                {
                    product.DiscountPercentage = 0;
                }
            }

            // Lọc sản phẩm
            if (isNew.HasValue && isNew.Value)
            {
                productsWithRating = productsWithRating.Where(p => p.Product.IsNew).ToList();
                ViewBag.CategoryName = "Sản phẩm mới";
                ViewBag.ParentCategory = null;
                ViewBag.SubCategory = null;
            }
            else if (categoryId.HasValue)
            {
                var category = await _categoryService.GetCategoryByIdAsync(categoryId.Value);
                if (category != null)
                {
                    var categoryIds = new List<int> { categoryId.Value };
                    if (category.SubCategories != null)
                    {
                        categoryIds.AddRange(category.SubCategories.Select(c => c.Id));
                    }
                    productsWithRating = productsWithRating.Where(p => categoryIds.Contains(p.Product.CategoryId)).ToList();
                    ViewBag.CategoryName = category.Name;
                    ViewBag.SubCategory = category;
                    ViewBag.ParentCategory = allCategories.FirstOrDefault(c => c.Id == category.ParentCategoryId);
                }
                else
                {
                    ViewBag.CategoryName = "Không tìm thấy danh mục";
                    ViewBag.ParentCategory = null;
                    ViewBag.SubCategory = null;
                }
            }
            else
            {
                ViewBag.CategoryName = "Tất cả sản phẩm";
                ViewBag.ParentCategory = null;
                ViewBag.SubCategory = null;
            }

            if (!string.IsNullOrEmpty(searchString))
            {
                productsWithRating = productsWithRating.Where(p => p.Product.Name.ToLower().Contains(searchString.ToLower())).ToList();
            }

            // Lọc theo giá
            if (!string.IsNullOrEmpty(priceRange))
            {
                switch (priceRange)
                {
                    case "duoi250000":
                        productsWithRating = productsWithRating.Where(p => (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) < 250000).ToList();
                        break;
                    case "250000-500000":
                        productsWithRating = productsWithRating.Where(p => (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) >= 250000 && (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) <= 500000).ToList();
                        break;
                    case "500000-1000000":
                        productsWithRating = productsWithRating.Where(p => (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) >= 500000 && (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) <= 1000000).ToList();
                        break;
                    case "1000000-2000000":
                        productsWithRating = productsWithRating.Where(p => (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) >= 1000000 && (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) <= 2000000).ToList();
                        break;
                    case "tren2000000":
                        productsWithRating = productsWithRating.Where(p => (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) > 2000000).ToList();
                        break;
                }
            }
            else if (customMinPrice.HasValue || customMaxPrice.HasValue)
            {
                decimal min = customMinPrice ?? 0;
                decimal max = customMaxPrice ?? decimal.MaxValue;
                productsWithRating = productsWithRating.Where(p => (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) >= min && (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) <= max).ToList();
            }

            // Lọc theo các tiêu chí khác
            if (minPrice.HasValue)
            {
                productsWithRating = productsWithRating.Where(p => (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) >= minPrice.Value).ToList();
            }
            if (maxPrice.HasValue)
            {
                productsWithRating = productsWithRating.Where(p => (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) <= maxPrice.Value).ToList();
            }

            if (selectedOccasionsList.Any())
            {
                productsWithRating = productsWithRating.Where(p => selectedOccasionsList.Contains(p.Occasion)).ToList();
            }

            if (selectedObjectsList.Any())
            {
                productsWithRating = productsWithRating.Where(p => selectedObjectsList.Contains(p.Object)).ToList();
            }

            if (presentations != null && presentations.Any())
            {
                productsWithRating = productsWithRating.Where(p => presentations.Contains(p.PresentationStyle)).ToList();
            }

            if (colors != null && colors.Any())
            {
                productsWithRating = productsWithRating.Where(p => p.Colors.Any(c => colors.Contains(c))).ToList();
            }

            if (flowerTypes != null && flowerTypes.Any())
            {
                productsWithRating = productsWithRating.Where(p => p.FlowerTypes.Any(ft => flowerTypes.Contains(ft))).ToList();
            }

            // Sắp xếp sản phẩm
            switch (sortOrder)
            {
                case "price_desc":
                    productsWithRating = productsWithRating.OrderByDescending(p => (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price)).ToList();
                    break;
                case "price_asc":
                    productsWithRating = productsWithRating.OrderBy(p => (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price)).ToList();
                    break;
                case "newest":
                    productsWithRating = productsWithRating.OrderByDescending(p => p.Product.CreatedDate).ToList();
                    break;
                default:
                    break;
            }

            var totalItems = productsWithRating.Count();
            var initialCount = 32;
            var loadMoreCount = 8;

            var displayedItems = initialCount + (skipCount * loadMoreCount);
            var productsToShow = productsWithRating.Take(displayedItems).ToList();
            var remainingItems = totalItems - displayedItems;

            ViewBag.SkipCount = skipCount;
            ViewBag.TotalItems = totalItems;
            ViewBag.RemainingItems = Math.Max(0, remainingItems);
            ViewBag.HasMoreProducts = remainingItems > 0;

            if (isNew.HasValue && isNew.Value)
            {
                ViewBag.CategoryName = "Sản phẩm mới";
                ViewBag.ParentCategory = null;
                ViewBag.SubCategory = null;
            }
            else if (categoryId.HasValue)
            {
                var category = await _categoryService.GetCategoryByIdAsync(categoryId.Value);
                if (category != null)
                {
                    ViewBag.CategoryName = category.Name;
                    ViewBag.SubCategory = category;
                    ViewBag.ParentCategory = allCategories.FirstOrDefault(c => c.Id == category.ParentCategoryId);
                }
                else
                {
                    ViewBag.CategoryName = "Không tìm thấy danh mục";
                    ViewBag.ParentCategory = null;
                    ViewBag.SubCategory = null;
                }
            }
            else
            {
                ViewBag.CategoryName = "Tất cả sản phẩm";
                ViewBag.ParentCategory = null;
                ViewBag.SubCategory = null;
            }

            return View(productsToShow);
        }

        [HttpGet]
        public async Task<IActionResult> Display(int id)
        {
            // Lấy danh mục
            var allCategories = await _categoryService.GetAllCategoriesAsync();
            var parentCategories = allCategories.Where(c => c.ParentCategoryId == null)
                                               .OrderBy(c => c.Name)
                                               .ToList();

            foreach (var currentParentCategory in parentCategories)
            {
                currentParentCategory.SubCategories = allCategories.Where(c => c.ParentCategoryId == currentParentCategory.Id)
                                                                 .OrderBy(c => c.Name)
                                                                 .ToList();
            }

            ViewBag.Categories = parentCategories ?? new List<Category>();

            // Lấy sản phẩm
            var product = await _productService.GetProductByIdAsync(id);
            if (product == null)
            {
                return NotFound();
            }

            // Áp dụng khuyến mãi
            var currentDate = DateTime.Now;
            var promotions = await _promotionService.GetAllPromotionsAsync();
            var applicablePromotions = promotions
                .Where(p => p.IsActive && p.StartDate <= currentDate && p.EndDate >= currentDate)
                .Where(p => p.PromotionProducts.Any(pp => pp.ProductId == product.Id))
                .ToList();

            if (applicablePromotions.Any())
            {
                var bestPromotion = applicablePromotions.OrderByDescending(p => p.DiscountPercentage).First();
                product.DiscountPercentage = bestPromotion.DiscountPercentage;
            }
            else
            {
                product.DiscountPercentage = 0;
            }

            // Lấy danh mục và sản phẩm tương tự
            var category = allCategories.FirstOrDefault(c => c.Id == product.CategoryId);
            ViewBag.CategoryName = category?.Name ?? "Không xác định";

            Category parentCategory = null;
            if (category != null && category.ParentCategoryId.HasValue)
            {
                parentCategory = allCategories.FirstOrDefault(c => c.Id == category.ParentCategoryId.Value);
            }
            ViewBag.ParentCategory = parentCategory;
            ViewBag.SubCategory = category;

            var similarProducts = await _productService.GetProductsByCategoryIdAsync(product.CategoryId);
            similarProducts = similarProducts.Where(p => p.Id != product.Id).Take(4).ToList();
            foreach (var similarProduct in similarProducts)
            {
                var similarPromotions = promotions
                    .Where(p => p.IsActive && p.StartDate <= currentDate && p.EndDate >= currentDate)
                    .Where(p => p.PromotionProducts.Any(pp => pp.ProductId == similarProduct.Id))
                    .ToList();
                if (similarPromotions.Any())
                {
                    var bestPromo = similarPromotions.OrderByDescending(p => p.DiscountPercentage).First();
                    similarProduct.DiscountPercentage = bestPromo.DiscountPercentage;
                }
                else
                {
                    similarProduct.DiscountPercentage = 0;
                }
            }
            ViewBag.SimilarProducts = similarProducts;

            // Lấy đánh giá
            var ratings = await _context.Ratings
                .Where(r => r.ProductId == id)
                .Include(r => r.User)
                .Include(r => r.Replies) 
                .ThenInclude(reply => reply.User)
                .Include(r => r.UserLikes)
                .OrderByDescending(r => r.ReviewDate)
                .Where(r => r.IsVisible == true)
                .ToListAsync();

            ViewBag.Ratings = ratings;
            ViewBag.AverageRating = ratings.Any() ? ratings.Average(r => r.Star) : 0;

            return View(product);
        }

        // Gửi đánh giá sản phẩm
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> SubmitRating(int productId, int star, string comment, IFormFile ratingImage)
        {
            // Kiểm tra số sao hợp lệ
            if (star < 1 || star > 5)
            {
                TempData["error"] = "Số sao phải từ 1 đến 5.";
                return RedirectToAction("Display", new { id = productId });
            }

            // Kiểm tra người dùng đăng nhập
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["error"] = "Bạn cần đăng nhập để đánh giá sản phẩm.";
                return RedirectToAction("Display", new { id = productId });
            }

            // Kiểm tra sản phẩm có tồn tại không
            var product = await _productService.GetProductByIdAsync(productId);
            if (product == null)
            {
                return NotFound();
            }

            // Kiêmr tra đánh giá sản phẩm
            var existingRating = await _context.Ratings
                .FirstOrDefaultAsync(r => r.ProductId == productId && r.UserId == userId);

            if (existingRating != null)
            {
                TempData["error"] = "Bạn đã đánh giá sản phẩm này rồi.";
                return RedirectToAction("Display", new { id = productId });
            }

            var rating = new Rating
            {
                ProductId = productId,
                UserId = userId,
                Star = star,
                Comment = comment,
                ReviewDate = DateTime.Now,
                IsVisible = true,
                LastModifiedBy = "System", 
                LastModifiedDate = DateTime.Now 
            };

            // Lưu hình ảnh (nếu có)
            if (ratingImage != null && ratingImage.Length > 0)
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(ratingImage.FileName);
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await ratingImage.CopyToAsync(stream);
                }

                rating.ImageUrl = $"/uploads/{fileName}";
            }

            _context.Ratings.Add(rating);
            await _context.SaveChangesAsync();

            TempData["success"] = "Đánh giá của bạn đã được gửi thành công.";
            return RedirectToAction("Display", new { id = productId });
        }

        // Gửi trả lời cho đánh giá
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> SubmitReply(int ratingId, string comment)
        {
            // Kiểm tra bình luận
            if (string.IsNullOrEmpty(comment))
            {
                TempData["error"] = "Nội dung trả lời không được để trống.";
                var rating = await _context.Ratings.FirstOrDefaultAsync(r => r.Id == ratingId);
                return RedirectToAction("Display", new { id = rating?.ProductId });
            }

            // Kiểm tra người dùng
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["error"] = "Bạn cần đăng nhập để trả lời.";
                var rating = await _context.Ratings.FirstOrDefaultAsync(r => r.Id == ratingId);
                return RedirectToAction("Display", new { id = rating?.ProductId });
            }

            // Kiểm tra đánh giá 
            var ratingExists = await _context.Ratings.FirstOrDefaultAsync(r => r.Id == ratingId);
            if (ratingExists == null)
            {
                return NotFound();
            }

            // Tạo trả lời mới
            var reply = new Reply
            {
                RatingId = ratingId,
                UserId = userId,
                Comment = comment,
                ReplyDate = DateTime.Now,
                IsVisible = true, 
                LastModifiedBy = "System", 
                LastModifiedDate = DateTime.Now 
            };

            _context.Replies.Add(reply);
            await _context.SaveChangesAsync();

            TempData["success"] = "Trả lời của bạn đã được gửi thành công.";
            return RedirectToAction("Display", new { id = ratingExists.ProductId });
        }

        // Báo cáo đánh giá
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> SubmitReport(int ratingId, string reason)
        {
            // Kiểm tra lý do
            if (string.IsNullOrEmpty(reason))
            {
                TempData["error"] = "Lý do báo cáo không được để trống.";
                var rating = await _context.Ratings.FirstOrDefaultAsync(r => r.Id == ratingId);
                return RedirectToAction("Display", new { id = rating?.ProductId });
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["error"] = "Bạn cần đăng nhập để báo cáo.";
                var rating = await _context.Ratings.FirstOrDefaultAsync(r => r.Id == ratingId);
                return RedirectToAction("Display", new { id = rating?.ProductId });
            }

            // Kiểm tra đánh giá có tồn tại không
            var ratingExists = await _context.Ratings.FirstOrDefaultAsync(r => r.Id == ratingId);
            if (ratingExists == null)
            {
                return NotFound();
            }

            // Kiểm tra tự báo cáo
            if (ratingExists.UserId == userId)
            {
                TempData["error"] = "Bạn không thể báo cáo đánh giá của chính mình.";
                return RedirectToAction("Display", new { id = ratingExists.ProductId });
            }

            var existingReport = await _context.Reports
                .FirstOrDefaultAsync(r => r.RatingId == ratingId && r.ReporterId == userId);
            if (existingReport != null)
            {
                TempData["error"] = "Bạn đã báo cáo đánh giá này rồi.";
                return RedirectToAction("Display", new { id = ratingExists.ProductId });
            }

            var report = new Report
            {
                RatingId = ratingId,
                ReporterId = userId,
                Reason = reason,
                ReportDate = DateTime.Now,
                IsResolved = false,
                ResolvedBy = userId, 
                ResolvedDate = null 
            };

            _context.Reports.Add(report);
            await _context.SaveChangesAsync();

            TempData["success"] = "Báo cáo của bạn đã được gửi thành công và đang chờ xử lý.";
            return RedirectToAction("Display", new { id = ratingExists.ProductId });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteRating(int ratingId)
        {
            // Lấy ID của người dùng hiện tại
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(currentUserId))
            {
                return Json(new { success = false, message = "Bạn cần đăng nhập để xóa đánh giá." });
            }

            // Tìm đánh giá trong cơ sở dữ liệu
            var rating = await _context.Ratings
                .FirstOrDefaultAsync(r => r.Id == ratingId);
            if (rating == null)
            {
                return Json(new { success = false, message = "Không tìm thấy đánh giá." });
            }

            // Kiểm tra xem người dùng có quyền xóa đánh giá này không
            if (rating.UserId != currentUserId)
            {
                return Json(new { success = false, message = "Bạn không có quyền xóa đánh giá này." });
            }

            // Xóa các trả lời liên quan trước khi xóa đánh giá
            var replies = await _context.Replies
                .Where(r => r.RatingId == ratingId)
                .ToListAsync();
            _context.Replies.RemoveRange(replies);

            // Xóa đánh giá
            _context.Ratings.Remove(rating);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LikeRating(int itemId, bool unlike = false)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(currentUserId))
            {
                return Json(new { success = false, message = "Bạn cần đăng nhập để đánh dấu hữu ích." });
            }

            var rating = await _context.Ratings
                .FirstOrDefaultAsync(r => r.Id == itemId);
            if (rating == null)
            {
                return Json(new { success = false, message = "Không tìm thấy đánh giá." });
            }

            var existingLike = await _context.UserLikes
                .FirstOrDefaultAsync(ul => ul.UserId == currentUserId && ul.RatingId == itemId);

            if (unlike)
            {
                if (existingLike == null)
                {
                    return Json(new { success = false, message = "Bạn chưa đánh dấu hữu ích cho đánh giá này." });
                }

                _context.UserLikes.Remove(existingLike);
                rating.LikesCount = Math.Max(0, rating.LikesCount - 1);
                await _context.SaveChangesAsync();
                return Json(new { success = true, newLikesCount = rating.LikesCount });
            }
            else
            {
                if (existingLike != null)
                {
                    return Json(new { success = false, message = "Bạn đã đánh dấu hữu ích cho đánh giá này rồi." });
                }

                var userLike = new UserLike
                {
                    UserId = currentUserId,
                    RatingId = itemId,
                    LikedAt = DateTime.Now
                };
                _context.UserLikes.Add(userLike);
                rating.LikesCount++;
                await _context.SaveChangesAsync();
                return Json(new { success = true, newLikesCount = rating.LikesCount });
            }
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnlikeRating(int itemId)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(currentUserId))
            {
                return Json(new { success = false, message = "Bạn cần đăng nhập để bỏ đánh dấu hữu ích." });
            }

            var rating = await _context.Ratings
                .FirstOrDefaultAsync(r => r.Id == itemId);
            if (rating == null)
            {
                return Json(new { success = false, message = "Không tìm thấy đánh giá." });
            }

            var existingLike = await _context.UserLikes
                .FirstOrDefaultAsync(ul => ul.UserId == currentUserId && ul.RatingId == itemId);

            if (existingLike == null)
            {
                return Json(new { success = false, message = "Bạn chưa đánh dấu hữu ích cho đánh giá này." });
            }

            _context.UserLikes.Remove(existingLike);
            rating.LikesCount = Math.Max(0, rating.LikesCount - 1);
            await _context.SaveChangesAsync();

            return Json(new { success = true, newLikesCount = rating.LikesCount });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LikeReply(int itemId)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(currentUserId))
            {
                return Json(new { success = false, message = "Bạn cần đăng nhập để đánh dấu hữu ích." });
            }

            var reply = await _context.Replies
                .FirstOrDefaultAsync(r => r.Id == itemId);
            if (reply == null)
            {
                return Json(new { success = false, message = "Không tìm thấy trả lời." });
            }

            // Kiểm tra xem người dùng đã thích chưa
            var existingLike = await _context.UserLikes
                .FirstOrDefaultAsync(ul => ul.UserId == currentUserId && ul.ReplyId == itemId);
            if (existingLike != null)
            {
                return Json(new { success = false, message = "Bạn đã đánh dấu hữu ích cho trả lời này rồi." });
            }

            // Thêm lượt thích mới
            var userLike = new UserLike
            {
                UserId = currentUserId,
                ReplyId = itemId,
                LikedAt = DateTime.Now
            };
            _context.UserLikes.Add(userLike);

            // Tăng số lượt thích
            reply.LikesCount++;
            await _context.SaveChangesAsync();

            return Json(new { success = true, newLikesCount = reply.LikesCount });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnlikeReply(int itemId)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(currentUserId))
            {
                return Json(new { success = false, message = "Bạn cần đăng nhập để bỏ đánh dấu hữu ích." });
            }

            var reply = await _context.Replies
                .FirstOrDefaultAsync(r => r.Id == itemId);
            if (reply == null)
            {
                return Json(new { success = false, message = "Không tìm thấy trả lời." });
            }

            var existingLike = await _context.UserLikes
                .FirstOrDefaultAsync(ul => ul.UserId == currentUserId && ul.ReplyId == itemId);

            if (existingLike == null)
            {
                return Json(new { success = false, message = "Bạn chưa đánh dấu hữu ích cho trả lời này." });
            }

            _context.UserLikes.Remove(existingLike);
            reply.LikesCount = Math.Max(0, reply.LikesCount - 1);
            await _context.SaveChangesAsync();

            return Json(new { success = true, newLikesCount = reply.LikesCount });
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteReply(int replyId)
        {
            // Lấy ID của người dùng hiện tại
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(currentUserId))
            {
                return Json(new { success = false, message = "Bạn cần đăng nhập để xóa trả lời." });
            }

            // Tìm trả lời trong cơ sở dữ liệu
            var reply = await _context.Replies
                .FirstOrDefaultAsync(r => r.Id == replyId);
            if (reply == null)
            {
                return Json(new { success = false, message = "Không tìm thấy trả lời." });
            }

            // Kiểm tra xem người dùng có quyền xóa trả lời này không
            if (reply.UserId != currentUserId)
            {
                return Json(new { success = false, message = "Bạn không có quyền xóa trả lời này." });
            }

            // Xóa trả lời
            _context.Replies.Remove(reply);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        private List<string> GetColorsForProduct(int productId)
        {
            var product = _context.Products
                .AsNoTracking()
                .FirstOrDefault(p => p.Id == productId);

            if (product == null || string.IsNullOrEmpty(product.Colors))
            {
                return new List<string> { "không xác định" };
            }

            try
            {
                var colors = System.Text.Json.JsonSerializer.Deserialize<List<string>>(product.Colors);
                return colors.Any() ? colors : new List<string> { "không xác định" };
            }
            catch (Exception)
            {
                return new List<string> { "không xác định" };
            }
        }

        // Lấy danh sách loại hoa của sản phẩm
        private async Task<List<string>> GetFlowerTypesForProduct(int productId)
        {
            var flowerTypeProducts = await _context.FlowerTypeProducts
                .Include(ftp => ftp.FlowerType)
                .Where(ftp => ftp.ProductId == productId)
                .ToListAsync();

            return flowerTypeProducts
                .Select(ftp => ftp.FlowerType?.Name)
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct()
                .ToList();
        }

        // Tải thêm sản phẩm qua AJAX
        [HttpGet]
        public async Task<IActionResult> LoadMoreProducts(int? categoryId, string searchString, decimal? minPrice, decimal? maxPrice, string sortOrder, bool? isNew, string priceRange, decimal? customMinPrice, decimal? customMaxPrice, int skipCount = 0, string[] occasions = null, string[] objects = null, string[] presentations = null, string[] colors = null, string[] flowerTypes = null)
        {
            var allCategories = await _categoryService.GetAllCategoriesAsync();
            var products = await _productService.GetAllProductsAsync();
            products = products.Where(p => p.IsActive).ToList();

            var productsWithRating = new List<ProductViewModel.ProductWithRating>();
            foreach (var product in products)
            {
                var averageRating = (await _context.Ratings
                    .Where(r => r.ProductId == product.Id)
                    .Select(r => (decimal?)r.Star)
                    .ToListAsync()).Average() ?? 0;

                var productCategory = allCategories.FirstOrDefault(c => c.Id == product.CategoryId);
                string occasion = "Không xác định";
                string objectValue = "Không xác định";

                if (productCategory != null)
                {
                    var parentCategory = productCategory.ParentCategoryId.HasValue
                        ? allCategories.FirstOrDefault(c => c.Id == productCategory.ParentCategoryId.Value)
                        : productCategory;

                    if (parentCategory != null && parentCategory.Name == "Chủ đề")
                    {
                        occasion = productCategory.Name;
                    }
                    if (parentCategory != null && parentCategory.Name == "Đối tượng")
                    {
                        objectValue = productCategory.Name;
                    }
                }

                var flowerTypesForProduct = await GetFlowerTypesForProduct(product.Id);

                var productWithRating = new ProductViewModel.ProductWithRating
                {
                    Product = product,
                    Rating = averageRating,
                    Occasion = occasion,
                    Object = objectValue,
                    PresentationStyle = product.PresentationStyle?.Name ?? "Không xác định",
                    Colors = GetColorsForProduct(product.Id),
                    FlowerTypes = flowerTypesForProduct
                };
                productsWithRating.Add(productWithRating);
            }

            var selectedOccasionsList = occasions?.ToList() ?? new List<string>();
            var selectedObjectsList = objects?.ToList() ?? new List<string>();

            if (categoryId.HasValue)
            {
                var selectedCategory = allCategories.FirstOrDefault(c => c.Id == categoryId.Value);
                if (selectedCategory != null)
                {
                    var parentCategory = selectedCategory.ParentCategoryId.HasValue
                        ? allCategories.FirstOrDefault(c => c.Id == selectedCategory.ParentCategoryId.Value)
                        : null;

                    if (parentCategory != null && parentCategory.Name == "Chủ đề" && !selectedOccasionsList.Contains(selectedCategory.Name))
                    {
                        selectedOccasionsList.Add(selectedCategory.Name);
                    }
                    if (parentCategory != null && parentCategory.Name == "Đối tượng" && !selectedObjectsList.Contains(selectedCategory.Name))
                    {
                        selectedObjectsList.Add(selectedCategory.Name);
                    }
                }
            }

            selectedOccasionsList = selectedOccasionsList.Distinct().ToList();
            selectedObjectsList = selectedObjectsList.Distinct().ToList();

            if (products == null)
            {
                products = new List<Product>();
            }

            var currentDate = DateTime.Now;
            var promotions = await _promotionService.GetAllPromotionsAsync();
            promotions = promotions.Where(p => p.IsActive).ToList();

            foreach (var product in products)
            {
                var applicablePromotions = promotions
                    .Where(p => p.PromotionProducts.Any(pp => pp.ProductId == product.Id))
                    .ToList();

                if (applicablePromotions.Any())
                {
                    var bestPromotion = applicablePromotions.OrderByDescending(p => p.DiscountPercentage).First();
                    product.DiscountPercentage = bestPromotion.DiscountPercentage;
                }
                else
                {
                    product.DiscountPercentage = 0;
                }
            }

            if (isNew.HasValue && isNew.Value)
            {
                productsWithRating = productsWithRating.Where(p => p.Product.IsNew).ToList();
            }
            else if (categoryId.HasValue)
            {
                var category = await _categoryService.GetCategoryByIdAsync(categoryId.Value);
                if (category != null)
                {
                    var categoryIds = new List<int> { categoryId.Value };
                    if (category.SubCategories != null)
                    {
                        categoryIds.AddRange(category.SubCategories.Select(c => c.Id));
                    }
                    productsWithRating = productsWithRating.Where(p => categoryIds.Contains(p.Product.CategoryId)).ToList();
                }
            }

            if (!string.IsNullOrEmpty(searchString))
            {
                productsWithRating = productsWithRating.Where(p => p.Product.Name.ToLower().Contains(searchString.ToLower())).ToList();
            }

            if (!string.IsNullOrEmpty(priceRange))
            {
                switch (priceRange)
                {
                    case "duoi250000":
                        productsWithRating = productsWithRating.Where(p => (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) < 250000).ToList();
                        break;
                    case "250000-500000":
                        productsWithRating = productsWithRating.Where(p => (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) >= 250000 && (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) <= 500000).ToList();
                        break;
                    case "500000-1000000":
                        productsWithRating = productsWithRating.Where(p => (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) >= 500000 && (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) <= 1000000).ToList();
                        break;
                    case "1000000-2000000":
                        productsWithRating = productsWithRating.Where(p => (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) >= 1000000 && (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) <= 2000000).ToList();
                        break;
                    case "tren2000000":
                        productsWithRating = productsWithRating.Where(p => (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) > 2000000).ToList();
                        break;
                }
            }
            else if (customMinPrice.HasValue || customMaxPrice.HasValue)
            {
                decimal min = customMinPrice ?? 0;
                decimal max = customMaxPrice ?? decimal.MaxValue;
                productsWithRating = productsWithRating.Where(p => (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) >= min && (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) <= max).ToList();
            }

            if (minPrice.HasValue)
            {
                productsWithRating = productsWithRating.Where(p => (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) >= minPrice.Value).ToList();
            }
            if (maxPrice.HasValue)
            {
                productsWithRating = productsWithRating.Where(p => (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price) <= maxPrice.Value).ToList();
            }

            if (selectedOccasionsList.Any())
            {
                productsWithRating = productsWithRating.Where(p => selectedOccasionsList.Contains(p.Occasion)).ToList();
            }

            if (selectedObjectsList.Any())
            {
                productsWithRating = productsWithRating.Where(p => selectedObjectsList.Contains(p.Object)).ToList();
            }

            if (presentations != null && presentations.Any())
            {
                productsWithRating = productsWithRating.Where(p => presentations.Contains(p.PresentationStyle)).ToList();
            }

            if (colors != null && colors.Any())
            {
                productsWithRating = productsWithRating.Where(p => p.Colors.Any(c => colors.Contains(c))).ToList();
            }

            if (flowerTypes != null && flowerTypes.Any())
            {
                productsWithRating = productsWithRating.Where(p => p.FlowerTypes.Any(ft => flowerTypes.Contains(ft))).ToList();
            }

            switch (sortOrder)
            {
                case "price_desc":
                    productsWithRating = productsWithRating.OrderByDescending(p => (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price)).ToList();
                    break;
                case "price_asc":
                    productsWithRating = productsWithRating.OrderBy(p => (p.Product.DiscountPercentage > 0 ? (p.Product.DiscountPrice ?? p.Product.Price) : p.Product.Price)).ToList();
                    break;
                case "newest":
                    productsWithRating = productsWithRating.OrderByDescending(p => p.Product.CreatedDate).ToList();
                    break;
                default:
                    break;
            }

            var totalItems = productsWithRating.Count();
            var initialCount = 32;
            var loadMoreCount = 8;

            var productsToShow = productsWithRating.Skip(initialCount + (skipCount * loadMoreCount)).Take(loadMoreCount).ToList();
            var displayedItems = initialCount + ((skipCount + 1) * loadMoreCount);
            var remainingItems = totalItems - displayedItems;

            var productsHtml = "";
            foreach (var item in productsToShow)
            {
                productsHtml += $@"
        <div class='product-card position-relative'>
            {(item.Product.IsNew ? "<span class='new-tag'>NEW</span>" : "")}
            {(item.Product.DiscountPercentage.HasValue && item.Product.DiscountPercentage > 0 ? "<span class='promo-badge'>SALE</span><span class='freeship-tag'>FREESHIP</span>" : "")}
            <a href='/Product/Display/@item.Product.Id'>
                {(string.IsNullOrEmpty(item.Product.ImageUrl)
                                ? "<div class='bg-light d-flex align-items-center justify-content-center product-img'><span class='text-muted'>Không có hình ảnh</span></div>"
                                : $"<img src='{item.Product.ImageUrl}' alt='{item.Product.Name}' class='product-img' />")}
            </a>
            <h3 class='mt-3'>{item.Product.Name}</h3>
            <div class='price'>
                {(item.Product.DiscountPercentage.HasValue && item.Product.DiscountPercentage > 0
                                ? $"<div class='original-price'>{item.Product.Price.ToString("#,##0")} đ</div><div class='discounted-price'>{item.Product.DiscountPrice.Value.ToString("#,##0")} đ</div>"
                                : $"<div class='discounted-price'>{item.Product.Price.ToString("#,##0")} đ</div>")}
            </div>
            <div class='rating mb-2'>
                {GetStarRatingHtml(item.Rating)}
                <span class='rating-score'>({item.Rating.ToString("0.0")}/5)</span>
            </div>
            <div class='text-muted mb-2'>Đã bán: {item.Product.QuantitySold}</div>
            <a href='/ShoppingCart/AddToCart?productId={item.Product.Id}' class='cart-icon' title='Thêm vào giỏ hàng'>
                <i class='bi bi-cart-plus'></i>
            </a>
        </div>";
            }

            return Json(new
            {
                productsHtml,
                remainingItems = Math.Max(0, remainingItems),
                hasMoreProducts = remainingItems > 0
            });
        }

        private string GetStarRatingHtml(decimal rating)
        {
            int fullStars = (int)rating;
            bool hasHalfStar = (double)rating - fullStars >= 0.5;
            int emptyStars = 5 - fullStars - (hasHalfStar ? 1 : 0);

            var html = "";
            for (int i = 0; i < fullStars; i++)
            {
                html += "<i class='bi bi-star-fill star-filled'></i>";
            }
            if (hasHalfStar)
            {
                html += "<i class='bi bi-star-half star-filled'></i>";
            }
            for (int i = 0; i < emptyStars; i++)
            {
                html += "<i class='bi bi-star star-empty'></i>";
            }
            return html;
        }

        // Existing ImageSearch and ImageSearchResults actions remain unchanged...
        [HttpPost]
        public async Task<IActionResult> ImageSearch(IFormFile imageFile)
        {
            if (imageFile == null || imageFile.Length == 0)
            {
                return Json(new { success = false, message = "Vui lòng tải lên một hình ảnh." });
            }

            try
            {
                var tempDir = Path.GetTempPath();
                var tempFileName = $"{Guid.NewGuid():N}.jpg";
                var tempPath = Path.Combine(tempDir, tempFileName);
                using (var stream = new FileStream(tempPath, FileMode.Create))
                {
                    await imageFile.CopyToAsync(stream);
                }

                var scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "Scripts", "analyze_image.py");
                if (!System.IO.File.Exists(scriptPath))
                {
                    return Json(new { success = false, message = "Không tìm thấy file analyze_image.py trong thư mục Scripts." });
                }

                var pythonPath = Environment.GetEnvironmentVariable("PYTHON_PATH") ?? @"C:\Users\KHOA\AppData\Local\Programs\Python\Python313\python.exe";
                var processInfo = new ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = $"\"{scriptPath}\" \"{tempPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                string result;
                string error;
                using (var process = Process.Start(processInfo))
                {
                    result = process.StandardOutput.ReadToEnd();
                    error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                }

                if (System.IO.File.Exists(tempPath))
                {
                    System.IO.File.Delete(tempPath);
                }

                if (!string.IsNullOrEmpty(error))
                {
                    return Json(new { success = false, message = "Lỗi khi phân tích ảnh: " + error });
                }

                var jsonLines = result.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Where(line => line.Trim().StartsWith("{") || line.Trim().StartsWith("["))
                                     .ToArray();
                var jsonResult = string.Join("\n", jsonLines);

                var analysisResult = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonResult);
                if (analysisResult == null || !analysisResult.ContainsKey("success"))
                {
                    return Json(new { success = false, message = "Kết quả từ Python không hợp lệ." });
                }

                var successElement = analysisResult["success"];
                if (successElement is JsonElement successJson && successJson.ValueKind == JsonValueKind.True)
                {
                    var colors = JsonSerializer.Deserialize<List<string>>(
                        analysisResult["colors"].ToString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    return Json(new
                    {
                        success = true,
                        redirectUrl = Url.Action("ImageSearchResults", new { colors = string.Join(",", colors) })
                    });
                }
                else
                {
                    var message = analysisResult.ContainsKey("message") ? analysisResult["message"].ToString() : "Phân tích thất bại.";
                    return Json(new { success = false, message = message });
                }
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Lỗi khi phân tích ảnh: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ImageSearchResults(string colors, string presentations)
        {
            var colorList = colors?.Split(',').Select(c => c.Trim().ToLower()).ToList() ?? new List<string>();

            var products = await _productService.GetAllProductsAsync();
            products = products.Where(p => p.IsActive).ToList();

            var allCategories = await _categoryService.GetAllCategoriesAsync();
            var productsWithRating = new List<ProductViewModel.ProductWithRating>();

            foreach (var product in products)
            {
                var averageRating = (await _context.Ratings
                    .Where(r => r.ProductId == product.Id)
                    .Select(r => (decimal?)r.Star)
                    .ToListAsync()).Average() ?? 0;

                var productCategory = allCategories.FirstOrDefault(c => c.Id == product.CategoryId);
                string occasion = "Không xác định";
                string objectValue = "Không xác định";

                if (productCategory != null)
                {
                    var parentCategory = productCategory.ParentCategoryId.HasValue
                        ? allCategories.FirstOrDefault(c => c.Id == productCategory.ParentCategoryId.Value)
                        : productCategory;

                    if (parentCategory != null && parentCategory.Name == "Chủ đề")
                    {
                        occasion = productCategory.Name;
                    }
                    if (parentCategory != null && parentCategory.Name == "Đối tượng")
                    {
                        objectValue = productCategory.Name;
                    }
                }

                var flowerTypesForProduct = await GetFlowerTypesForProduct(product.Id);

                var productWithRating = new ProductViewModel.ProductWithRating
                {
                    Product = product,
                    Rating = averageRating,
                    Occasion = occasion,
                    Object = objectValue,
                    PresentationStyle = product.PresentationStyle?.Name?.ToLower() ?? "không xác định",
                    Colors = GetColorsForProduct(product.Id),
                    FlowerTypes = flowerTypesForProduct
                };
                productsWithRating.Add(productWithRating);
            }

            Console.WriteLine($"Total Active Products: {products.Count()}");
            Console.WriteLine($"Search Colors: {string.Join(", ", colorList)}");

            productsWithRating = productsWithRating
                .Where(p => colorList.Any() && p.Colors.Any(c => colorList.Contains(c)))
                .Take(20)
                .ToList();

            Console.WriteLine($"Final Filtered Products Count: {productsWithRating.Count}");

            var parentCategories = allCategories.Where(c => c.ParentCategoryId == null)
                                               .OrderBy(c => c.Name)
                                               .ToList();
            foreach (var category in parentCategories)
            {
                category.SubCategories = allCategories.Where(c => c.ParentCategoryId == category.Id)
                                                     .OrderBy(c => c.Name)
                                                     .ToList();
            }

            var presentationStyles = await _context.PresentationStyles
                .Select(ps => ps.Name)
                .Distinct()
                .OrderBy(name => name)
                .ToListAsync();

            ViewBag.Categories = parentCategories ?? new List<Category>();
            ViewBag.Presentations = presentationStyles.Any() ? presentationStyles : new List<string> { "Không có kiểu trình bày" };
            ViewBag.SearchColors = colorList;
            ViewBag.SearchPresentations = new List<string>();

            return View(productsWithRating);
        }
    }
}
