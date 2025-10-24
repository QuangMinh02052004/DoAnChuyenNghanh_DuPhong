using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Services.Interfaces;
using Bloomie.Extensions;
using Bloomie.Models.ViewModels;

namespace Bloomie.Controllers
{
    public class HomeController : Controller
    {
        private readonly IProductService _productService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;

        public HomeController(IProductService productService, UserManager<ApplicationUser> userManager, ApplicationDbContext context)
        {
            _productService = productService;
            _userManager = userManager;
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // Lấy tất cả sản phẩm và thông tin  đánh giá từng sản phẩm
            var allProducts = await _context.Products
                .Include(p => p.Ratings)
                .ToListAsync();
            allProducts ??= new List<Product>(); // Đảm bảo không null

            // Lấy các chương trình khuyến mãi đang hoạt động
            var activePromotions = await _context.Promotions
                .Where(p => p.IsActive && p.StartDate <= DateTime.Now && p.EndDate >= DateTime.Now)
                .Include(p => p.PromotionProducts)
                .ThenInclude(pp => pp.Product)
                .ToListAsync();

            // Lấy danh sách ID sản phẩm có khuyến mãi
            var productIdsWithPromotions = activePromotions
                .SelectMany(p => p.PromotionProducts)
                .Select(pp => pp.ProductId)
                .Distinct()
                .ToList();
            
            // Nếu sản phẩm có nhiều khuyến mãi, chọn khuyến mãi có giảm giá cao nhất
            var promotionDiscounts = new Dictionary<int, decimal>();
            foreach (var promotion in activePromotions)
            {
                foreach (var pp in promotion.PromotionProducts)
                {
                    if (!promotionDiscounts.ContainsKey(pp.ProductId))
                    {
                        promotionDiscounts[pp.ProductId] = promotion.DiscountPercentage;
                    }
                    else
                    {
                        promotionDiscounts[pp.ProductId] = Math.Max(promotionDiscounts[pp.ProductId], promotion.DiscountPercentage);
                    }
                }
            }

            // Lấy danh sách sản phẩm mới
            var newProducts = allProducts
                .Where(p => p.CreatedDate >= DateTime.Now.AddDays(-7) || p.IsNew)
                .Take(10)
                .ToList();

            // Lấy danh sách sản phẩm bán chạy
            var bestSellingProducts = allProducts
                .OrderByDescending(p => p.QuantitySold)
                .Take(20)
                .ToList();

            // Tính trung bình Rating từ cơ sở dữ liệu
            var productRatings = await _context.Ratings
                .GroupBy(r => r.ProductId)
                .Select(g => new { ProductId = g.Key, AverageRating = g.Average(r => (decimal?)r.Star) ?? 0 })
                .ToDictionaryAsync(g => g.ProductId, g => g.AverageRating);

            // Tạo danh sách ProductWithRating cho NewProducts
            var newProductsWithRating = new List<ProductViewModel.ProductWithRating>();
            foreach (var product in newProducts)
            {
                product.DiscountPercentage = 0;
                if (promotionDiscounts.ContainsKey(product.Id))
                {
                    product.DiscountPercentage = promotionDiscounts[product.Id];
                }
                var averageRating = productRatings.ContainsKey(product.Id) ? productRatings[product.Id] : 0;

                newProductsWithRating.Add(new ProductViewModel.ProductWithRating
                {
                    Product = product,
                    Rating = averageRating
                });
            }

            // Tạo danh sách ProductWithRating cho BestSellingProducts
            var bestSellingProductsWithRating = new List<ProductViewModel.ProductWithRating>();
            foreach (var product in bestSellingProducts)
            {
                product.DiscountPercentage = 0;
                if (promotionDiscounts.ContainsKey(product.Id))
                {
                    product.DiscountPercentage = promotionDiscounts[product.Id];
                }
                var averageRating = productRatings.ContainsKey(product.Id) ? productRatings[product.Id] : 0;

                bestSellingProductsWithRating.Add(new ProductViewModel.ProductWithRating
                {
                    Product = product,
                    Rating = averageRating
                });
            }

            // Lấy danh sách danh mục cha
            var categories = await _context.Categories
                .Where(c => c.ParentCategoryId == null)
                .Include(c => c.SubCategories)
                .OrderBy(c => c.Name)
                .ToListAsync();
            // Sắp xếp danh mục con theo tên
            foreach (var category in categories)
            {
                category.SubCategories = category.SubCategories.OrderBy(sc => sc.Name).ToList();
            }

            ViewBag.Categories = categories ?? new List<Category>();

            var viewModel = new ProductViewModel
            {
                NewProducts = newProductsWithRating,
                BestSellingProducts = bestSellingProductsWithRating
            };

            var userId = _userManager.GetUserId(User);
            var cartKey = $"Cart_{userId}";
            // Lấy giỏ hàng từ session, nếu không có thì tạo mới
            var cart = HttpContext.Session.GetObjectFromJson<ShoppingCart>(cartKey) ?? new ShoppingCart();
            ViewData["CartCount"] = cart.TotalItems;

            return View(viewModel);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        //[HttpGet]
        //public IActionResult Contact()
        //{
        //    return View(new ContactFormModel());
        //}

        //[HttpPost]
        //public IActionResult Contact(ContactFormModel model)
        //{
        //    if (ModelState.IsValid)
        //    {
        //        TempData["SuccessMessage"] = "Cảm ơn bạn đã liên hệ! Chúng tôi sẽ phản hồi sớm nhất có thể.";
        //        return RedirectToAction("Contact");
        //    }
        //    return View(model);
        //}
    }
}