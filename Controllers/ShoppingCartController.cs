using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Bloomie.Extensions;
using System.Globalization;

namespace Bloomie.Controllers
{
    public class ShoppingCartController : Controller
    {
        private readonly IProductService _productService;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ShoppingCartController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IProductService productService)
        {
            _productService = productService;
            _context = context;
            _userManager = userManager;
        }

        // Tạo khóa giỏ hàng duy nhất
        private string GetCartKey()
        {
            var isAuthenticated = User?.Identity?.IsAuthenticated ?? false;
            string cartKey;

            if (isAuthenticated)
            {
                cartKey = $"Cart_{_userManager.GetUserId(User)}";
            }
            else
            {
                if (string.IsNullOrEmpty(HttpContext.Session.Id))
                {
                    HttpContext.Session.SetString("TempSessionId", Guid.NewGuid().ToString());
                }
                cartKey = $"Cart_Anonymous_{HttpContext.Session.GetString("TempSessionId") ?? HttpContext.Session.Id}";
            }

            return cartKey;
        }

        // Lấy giỏ hàng từ session, nếu không có thì tạo mới
        private ShoppingCart GetCart()
        {
            var cartKey = GetCartKey();
            var cart = HttpContext.Session.GetObjectFromJson<ShoppingCart>(cartKey) ?? new ShoppingCart();
            UpdateCartCount(cart);
            return cart;
        }

        // Cập nhật số lượng sản phẩm trong giỏ hàng vào ViewData
        private void UpdateCartCount(ShoppingCart cart)
        {
            ViewData["CartCount"] = cart.TotalItems;
        }

        [HttpGet]
        public async Task<IActionResult> AddToCart(int productId, int quantity = 1, string deliveryDate = null, string deliveryTime = null, decimal? discountedPrice = null)
        {
            // Kiểm tra ngày giao
            DateTime? parsedDeliveryDate = null;
            if (!string.IsNullOrEmpty(deliveryDate))
            {
                if (!DateTime.TryParseExact(deliveryDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                    {
                        return Json(new { success = false, message = "Định dạng ngày giao hàng không hợp lệ!" });
                    }
                    TempData["ErrorMessage"] = "Định dạng ngày giao hàng không hợp lệ!";
                    return RedirectToAction("Index");
                }
                parsedDeliveryDate = date;
            }
            else
            {
                parsedDeliveryDate = DateTime.Now.AddDays(1).Date; 
            }

            //Gán khung giờ mặc định nếu không có giá trị
            deliveryTime = string.IsNullOrEmpty(deliveryTime) ? "08:00 - 10:00" : deliveryTime;

            // Kiểm tra sản phẩm
            var product = await _productService.GetProductByIdAsync(productId);
            if (product == null)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "Sản phẩm không tồn tại!" });
                }
                return NotFound();
            }

            // Kiểm tra số lượng
            if (quantity <= 0 || quantity > product.Quantity)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "Số lượng không hợp lệ hoặc không đủ hàng!" });
                }
                TempData["ErrorMessage"] = "Số lượng không hợp lệ hoặc không đủ hàng!";
                return RedirectToAction("Index");
            }

            // Kiểm tra ngày giao từ hôm nay trở đi
            if (!parsedDeliveryDate.HasValue || parsedDeliveryDate.Value.Date < DateTime.Now.Date)
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "Ngày giao hàng phải từ hôm nay trở đi!" });
                }
                TempData["ErrorMessage"] = "Ngày giao hàng phải từ hôm nay trở đi!";
                return RedirectToAction("Index");
            }

            // Kiểm tra khung giờ giao
            if (string.IsNullOrEmpty(deliveryTime))
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "Vui lòng chọn khung giờ giao hàng!" });
                }
                TempData["ErrorMessage"] = "Vui lòng chọn khung giờ giao hàng!";
                return RedirectToAction("Index");
            }

            // Kiểm tra quyền của người dùng
            var user = await _userManager.GetUserAsync(User);
            var isAuthenticated = User?.Identity?.IsAuthenticated ?? false;
            if (isAuthenticated && await _userManager.IsInRoleAsync(user, "Admin"))
            {
                if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    return Json(new { success = false, message = "Admin không thể thêm sản phẩm vào giỏ hàng!" });
                }
                TempData["ErrorMessage"] = "🚫 Admin không thể thêm sản phẩm vào giỏ hàng.";
                return RedirectToAction("Index");
            }

            // Tính giá cuối cùng
            decimal finalPrice = discountedPrice ?? (product.DiscountPrice.HasValue ? product.DiscountPrice.Value : product.Price);

            // Tạo đối tượng CartItem
            var cartItem = new CartItem
            {
                ProductId = productId,
                Name = product.Name,
                Price = product.Price,
                Quantity = quantity,
                ImageUrl = product.ImageUrl,
                DeliveryDate = parsedDeliveryDate,
                DeliveryTime = deliveryTime
            };

            // Thêm vào giỏ hàng và lưu vào session
            var cart = GetCart();
            cart.AddItem(cartItem);
            HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success = true, cartCount = cart.TotalItems, message = "Đã thêm sản phẩm vào giỏ hàng!" });
            }

            return RedirectToAction("Index");
        }

        public IActionResult Index()
        {
            var cart = GetCart();
            return View(cart);
        }

        public IActionResult RemoveFromCart(int productId, DateTime? deliveryDate, string deliveryTime)
        {
            var cart = GetCart();
            cart.Items.RemoveAll(i => i.ProductId == productId && i.DeliveryDate == deliveryDate && i.DeliveryTime == deliveryTime);
            HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
            UpdateCartCount(cart);
            return RedirectToAction("Index");
        }

        public IActionResult IncreaseQuantity(int productId)
        {
            var cart = GetCart();
            var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);

            if (item != null)
            {
                item.Quantity++;
                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
            }

            return RedirectToAction("Index");
        }

        public IActionResult DecreaseQuantity(int productId)
        {
            var cart = GetCart();
            var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);

            if (item != null)
            {
                if (item.Quantity > 1)
                {
                    item.Quantity--;
                }
                else
                {
                    cart.Items.Remove(item);
                }
                HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult UpdateQuantity(int productId, int quantity)
        {
            var cart = GetCart();
            var item = cart.Items.FirstOrDefault(i => i.ProductId == productId);

            if (item == null)
            {
                return Json(new { success = false, message = "Sản phẩm không tồn tại trong giỏ hàng!" });
            }

            if (quantity < 1)
            {
                quantity = 1;
            }

            item.Quantity = quantity;
            HttpContext.Session.SetObjectAsJson(GetCartKey(), cart);

            var newTotal = cart.Items.Sum(i => i.Price * i.Quantity);
            return Json(new { success = true, newTotal = newTotal });
        }
    }
}