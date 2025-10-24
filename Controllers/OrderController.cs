using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Bloomie.Data;
using Bloomie.Models;
using Bloomie.Models.Entities;
using Bloomie.Services.Interfaces;
using Bloomie.Extensions;
using Bloomie.ViewModels;
using Bloomie.Models.Vnpay;
using Bloomie.Services.Implementations;
using Bloomie.Models.GHN;
using Bloomie.Hubs;


namespace Bloomie.Controllers
{
    [Authorize]
    public class OrderController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IPaymentService _paymentService;
        private readonly IProductService _productService;
        private readonly IMomoService _momoService;
        private readonly IVnPayService _vnPayService;
        private readonly IEmailService _emailService;
        private readonly IGHNService _ghnService;
        private readonly HttpClient _httpClient;
        private readonly IHubContext<NotificationHub> _hubContext;

        public OrderController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IPaymentService paymentService,
            IProductService productService,
            IEmailService emailService,
            IMomoService momoService,
            IVnPayService vnPayService,
            IGHNService ghnService,
            IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _userManager = userManager;
            _paymentService = paymentService;
            _productService = productService;
            _emailService = emailService;
            _momoService = momoService;
            _vnPayService = vnPayService;
            _ghnService = ghnService;
            _httpClient = new HttpClient();
            _hubContext = hubContext;
        }

        [HttpGet]
        public IActionResult Checkout()
        {
            // Lấy ID người dùng hiện tại
            var userId = _userManager.GetUserId(User);
            var cartKey = $"Cart_{userId}";
            var cart = HttpContext.Session.GetObjectFromJson<ShoppingCart>(cartKey);

            if (cart == null || !cart.Items.Any())
            {
                TempData["ErrorMessage"] = "Giỏ hàng của bạn trống!";
                return RedirectToAction("Index", "ShoppingCart");
            }

            // Lấy phí vận chuyển từ cookie, mặc định 50,000 đ
            var shippingPriceCookie = Request.Cookies["ShippingPrice"];
            decimal shippingPrice = 50000;

            if (shippingPriceCookie != null)
            {
                try
                {
                    shippingPrice = JsonConvert.DeserializeObject<decimal>(shippingPriceCookie);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error deserializing shipping price cookie: {ex.Message}");
                    shippingPrice = 50000; // Nếu có lỗi dùng giá mặc định
                }
            }

            var model = new CheckoutViewModel
            {
                CartItems = cart.Items,
                TotalPrice = cart.Items.Sum(item => item.Price * item.Quantity),
                ShippingCost = shippingPrice
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Checkout(CheckoutViewModel model)
        {
            var userId = _userManager.GetUserId(User);
            var cartKey = $"Cart_{userId}";
            var cart = HttpContext.Session.GetObjectFromJson<ShoppingCart>(cartKey);

            if (cart == null || !cart.Items.Any())
            {
                TempData["ErrorMessage"] = "Giỏ hàng của bạn trống!";
                return RedirectToAction("Index", "ShoppingCart");
            }

            // Gán URL hình ảnh cho sản phẩm trong giỏ hàng
            foreach (var item in cart.Items)
            {
                var product = _context.Products.FirstOrDefault(p => p.Id == item.ProductId);
                if (product != null)
                {
                    item.ImageUrl = product.ImageUrl;
                }
                else
                {
                    item.ImageUrl = null;
                }
            }

            model.CartItems = cart.Items;

            // Kiểm tra tính hợp lệ của giỏ hàng
            foreach (var item in model.CartItems.ToList())
            {
                var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId);
                if (product == null || item.Quantity <= 0 || item.Quantity > product.Quantity || item.Price < 0)
                {
                    TempData["ErrorMessage"] = "Có lỗi với giỏ hàng: sản phẩm không tồn tại, số lượng hoặc giá không hợp lệ.";
                    model.TotalPrice = cart.Items.Sum(i => i.Price * i.Quantity);
                    model.CartItems = cart.Items;
                    return View(model);
                }

                // Kiểm tra Ngày giao hàng và Thời gian giao
                if (!item.DeliveryDate.HasValue || item.DeliveryDate.Value.Date < DateTime.Now.Date)
                {
                    TempData["ErrorMessage"] = $"Ngày giao hàng của sản phẩm {item.Name} không hợp lệ.";
                    model.TotalPrice = cart.Items.Sum(i => i.Price * i.Quantity);
                    return View(model);
                }
                if (string.IsNullOrEmpty(item.DeliveryTime))
                {
                    TempData["ErrorMessage"] = $"Vui lòng chọn khung giờ giao hàng cho sản phẩm {item.Name}.";
                    model.TotalPrice = cart.Items.Sum(i => i.Price * i.Quantity);
                    return View(model);
                }
            }

            // Ghép địa chỉ đầy đủ
            string fullAddress = model.ShippingAddress ?? "";
            model.ShippingAddress = fullAddress;

            // Tính tổng giá (bao gồm phí vận chuyển)
            decimal totalPrice = model.CartItems.Sum(item => item.Price * item.Quantity);
            decimal shippingPrice = model.ShippingCost; 
            totalPrice += shippingPrice; 

            if (totalPrice <= 0)
            {
                TempData["ErrorMessage"] = "Có lỗi với giỏ hàng: Tổng tiền không hợp lệ.";
                model.TotalPrice = totalPrice - shippingPrice; // Trừ lại phí vận chuyển để hiển thị
                model.CartItems = cart.Items;
                return View(model);
            }

            if (!ModelState.IsValid)
            {
                model.TotalPrice = totalPrice - shippingPrice; // Trừ lại phí vận chuyển để hiển thị
                model.CartItems = cart.Items;
                return View(model);
            }

            // Lấy thông tin người dùng
            var user = await _userManager.GetUserAsync(User);
            string userFullName = user?.UserName ?? "Khách";
            if (user != null)
            {
                userFullName = user.FullName ?? user.UserName;
            }

            Order order = null;
            Payment payment = null;

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var orderId = $"{DateTime.Now:yyyyMMddHHmmss}-{new Random().Next(1000, 9999)}";

                order = new Order
                {
                    Id = orderId,
                    UserId = userId,
                    OrderDate = DateTime.Now,
                    TotalPrice = totalPrice, 
                    ShippingAddress = model.ShippingAddress,
                    ShippingMethod = model.ShippingMethod,
                    Notes = model.Notes,
                    OrderStatus = OrderStatus.Pending,
                    SenderName = model.SenderName,
                    SenderEmail = model.SenderEmail,
                    SenderPhoneNumber = model.SenderPhoneNumber,
                    ReceiverName = model.IsSenderReceiverSame ? model.SenderName : model.ReceiverName,
                    ReceiverPhoneNumber = model.IsSenderReceiverSame ? model.SenderPhoneNumber : model.ReceiverPhoneNumber,
                    ReceiverEmail = model.IsSenderReceiverSame ? model.SenderEmail : model.ReceiverEmail,
                    IsSenderReceiverSame = model.IsSenderReceiverSame,
                    IsAnonymousSender = model.IsAnonymousSender,
                    PhoneNumber = model.SenderPhoneNumber,
                    OrderDetails = model.CartItems.Select(item => new OrderDetail
                    {
                        ProductId = item.ProductId,
                        Quantity = item.Quantity,
                        Price = item.Price,
                        DeliveryDate = item.DeliveryDate,
                        DeliveryTime = item.DeliveryTime
                    }).ToList()
                };

                _context.Orders.Add(order);
                await _context.SaveChangesAsync();
                Console.WriteLine($"Đã lưu đơn hàng với OrderId: {order.Id}");

                // Cập nhật số lượng sản phẩm trong kho
                foreach (var item in model.CartItems)
                {
                    var product = await _context.Products.FindAsync(item.ProductId);
                    if (product != null)
                    {
                        if (product.Quantity < item.Quantity)
                            throw new Exception($"Sản phẩm {item.Name} không đủ số lượng trong kho.");
                        product.Quantity -= item.Quantity;
                        product.QuantitySold += item.Quantity;
                        _context.Products.Update(product);
                    }
                }
                await _context.SaveChangesAsync();

                // Tạo bản ghi thanh toán
                payment = await _paymentService.CreatePaymentAsync(order.Id, model.PaymentMethod, order.TotalPrice);

                //Thông báo cho tất cả admin
                var admins = await _userManager.GetUsersInRoleAsync("Admin");
                if (admins == null || !admins.Any())
                {
                    Console.WriteLine("Không tìm thấy admin nào trong vai trò 'Admin'. Bỏ qua việc lưu thông báo.");
                }
                else
                {
                    foreach (var admin in admins)
                    {
                        if (string.IsNullOrEmpty(admin.Id))
                        {
                            Console.WriteLine($"Admin Id là null, bỏ qua admin này: {admin.UserName}");
                            continue;
                        }

                        var message = $"Một đơn hàng mới (Mã: {order.Id})";
                        var link = Url.Action("Details", "AdminOrder", new { area = "Admin", id = order.Id }, Request.Scheme);

                        if (message.Length > 255)
                        {
                            message = message.Substring(0, 252) + "...";
                        }

                        var notification = new Notification
                        {
                            UserId = admin.Id, 
                            Title = "Đơn hàng mới",
                            Message = message,
                            Link = link ?? "#",
                            CreatedAt = DateTime.Now,
                            IsRead = false
                        };

                        try
                        {
                            _context.Notifications.Add(notification);
                            await _context.SaveChangesAsync();
                            Console.WriteLine($"Đã lưu thông báo cho admin {admin.UserName} (UserId: {notification.UserId}).");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Lỗi khi lưu thông báo cho admin {admin.UserName}: {ex.Message}");
                            if (ex.InnerException != null)
                            {
                                Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                            }
                        }
                    }
                }

                //Gửi thông báo qua SignalR
                try
                {
                    var signalRMessage = $"Một đơn hàng mới từ khách hàng {order.SenderName} (Mã: {order.Id})";
                    var signalRLink = Url.Action("Details", "AdminOrder", new { id = order.Id }, Request.Scheme, "Admin");
                    await _hubContext.Clients.Group("Admins").SendAsync("ReceiveNotification", new { message = signalRMessage, link = signalRLink ?? "#" });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Lỗi khi gửi thông báo qua SignalR: {ex.Message}");
                }

                HttpContext.Session.Remove(cartKey);
                ViewData["CartCount"] = 0;

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                TempData["ErrorMessage"] = $"Có lỗi xảy ra khi xử lý đơn hàng: {ex.Message}. Vui lòng thử lại.";
                if (ex.InnerException != null)
                {
                    TempData["ErrorMessage"] += $" Chi tiết: {ex.InnerException.Message}";
                }
                model.TotalPrice = totalPrice - shippingPrice;
                model.CartItems = cart.Items;
                return View(model);
            }

            // Xử lý phương thức thanh toán
            try
            {
                if (model.PaymentMethod == "Momo")
                {
                    Console.WriteLine($"Gọi CallMomoPaymentApi: PaymentId={payment.Id}, Amount={order.TotalPrice}, OrderId={order.Id}");
                    string paymentUrl = await CallMomoPaymentApi(payment.Id, order.TotalPrice, order.Id, userFullName);
                    return Redirect(paymentUrl);
                }

                else if (model.PaymentMethod == "Vnpay")
                {
                    var vnpayModel = new PaymentInformationModel
                    {
                        Name = userFullName,
                        Amount = order.TotalPrice,
                        OrderDescription = $"Thanh toán đơn hàng #{order.Id} tại Bloomie",
                        OrderType = "billpayment",
                        TxnRef = order.Id
                    };
                    Console.WriteLine($"Chuyển hướng đến CreatePaymentVnpay - Amount: {vnpayModel.Amount}, TxnRef: {vnpayModel.TxnRef}");
                    return CreatePaymentVnpay(vnpayModel);
                }

                else if (model.PaymentMethod == "CashOnDelivery")
                {
                    order.OrderStatus = OrderStatus.Pending;
                    await _context.SaveChangesAsync();
                    var paymentService = await _paymentService.CreatePaymentAsync(order.Id, "CashOnDelivery", order.TotalPrice);
                    paymentService.PaymentStatus = "Đang chờ xử lý";
                    var userFromDb = await _userManager.GetUserAsync(User);
                    await SendOrderConfirmationEmail(userFromDb, order);
                    return RedirectToAction("OrderConfirmation", new { orderId = order.Id });
                }

                else
                {
                    TempData["ErrorMessage"] = "Phương thức thanh toán không hợp lệ.";
                    return RedirectToAction("Checkout");
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Lỗi khi xử lý thanh toán: {ex.Message}. Đơn hàng đã được lưu, nhưng thanh toán không thành công.";
                return RedirectToAction("OrderConfirmation", new { orderId = order.Id });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreatePaymentMomo(OrderInfoModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Dữ liệu gửi lên không hợp lệ.";
                return RedirectToAction("Checkout");
            }

            var response = await _momoService.CreatePaymentMomo(model);
            if (response != null && response.ResultCode == 0)
            {
                return Redirect(response.PayUrl);
            }

            TempData["ErrorMessage"] = $"Lỗi MoMo: {response?.Message ?? "Không có phản hồi từ MoMo"}";
            return RedirectToAction("Checkout");
        }

        [HttpGet]
        public async Task<IActionResult> PaymentCallBack()
        {
            var requestQuery = HttpContext.Request.Query;

            // Kiểm tra chữ ký và lấy phản hồi từ MoMo
            var response = _momoService.PaymentExecuteAsync(requestQuery);
            if (response == null || response.ResultCode == -1)
            {
                TempData["ErrorMessage"] = "Chữ ký giao dịch MoMo không hợp lệ.";
                return RedirectToAction("Index", "ShoppingCart");
            }

            // Kiểm tra orderId
            string orderIdStr = response.OrderId;
            if (string.IsNullOrEmpty(orderIdStr))
            {
                TempData["ErrorMessage"] = "Mã đơn hàng không hợp lệ.";
                return RedirectToAction("Index", "ShoppingCart");
            }

            // Tìm đơn hàng trong cơ sở dữ liệu
            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .Where(o => o.Id == orderIdStr)
                .FirstOrDefaultAsync();

            if (order == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index", "ShoppingCart");
            }

            try
            {
                if (response.ResultCode == 0) // Thanh toán thành công
                {
                    order.OrderStatus = OrderStatus.Processing;
                    _context.Update(order);

                    var payment = await _context.Payments
                        .Where(p => p.OrderId == orderIdStr)
                        .FirstOrDefaultAsync();
                    if (payment != null)
                    {
                        await _paymentService.ProcessPaymentAsync(payment.Id, "Paid", null);
                    }

                    var user = await _userManager.GetUserAsync(User);
                    if (user != null)
                    {
                        await SendOrderConfirmationEmail(user, order);
                        await SendPaymentConfirmationEmail(user, order, "MoMo");
                    }
                }
                else // Thanh toán thất bại
                {
                    order.OrderStatus = OrderStatus.Cancelled;
                    _context.Update(order);
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Có lỗi khi xử lý thanh toán: {ex.Message}";
                return RedirectToAction("Index", "ShoppingCart");
            }

            return RedirectToAction("OrderConfirmation", new { orderId = orderIdStr });
        }

        private async Task<string> CallMomoPaymentApi(int paymentId, decimal amount, string orderId, string userFullName)
        {
            if (amount <= 0)
            {
                throw new ArgumentException("Số tiền thanh toán không hợp lệ.");
            }

            if (_momoService == null)
            {
                throw new InvalidOperationException("IMomoService không được khởi tạo.");
            }

            var orderInfoModel = new OrderInfoModel
            {
                OrderId = orderId,
                Amount = (long)amount,
                OrderInformation = $"Thanh toán đơn hàng #{orderId} tại Bloomie",
                FullName = userFullName
            };

            try
            {
                var response = await _momoService.CreatePaymentMomo(orderInfoModel);

                if (response != null && response.ResultCode == 0 && !string.IsNullOrEmpty(response.PayUrl))
                {
                    return response.PayUrl;
                }
                else
                {
                    throw new Exception($"Không thể tạo thanh toán MoMo: {response?.Message ?? "Không có phản hồi hợp lệ từ MoMo"}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lỗi chi tiết trong CallMomoPaymentApi: {ex.Message}\nStackTrace: {ex.StackTrace}");
                throw;
            }
        }

        [HttpPost]
        public async Task<IActionResult> MomoNotify()
        {
            var collection = HttpContext.Request.Query;
            var response = _momoService.PaymentExecuteAsync(collection);
            var paymentId = int.Parse(response.OrderId);
            var resultCode = collection["resultCode"].ToString();
            var status = resultCode == "0" ? "Paid" : "Failed";
            await _paymentService.ProcessPaymentAsync(paymentId, status, null);
            return Ok();
        }

        public IActionResult CreatePaymentVnpay(PaymentInformationModel model)
        {
            if (model.Amount <= 0)
            {
                TempData["ErrorMessage"] = "Số tiền thanh toán không hợp lệ.";
                return RedirectToAction("Checkout");
            }
            if (string.IsNullOrEmpty(model.OrderDescription))
            {
                TempData["ErrorMessage"] = "Mô tả đơn hàng không được để trống.";
                return RedirectToAction("Checkout");
            }
            if (string.IsNullOrEmpty(model.TxnRef))
            {
                TempData["ErrorMessage"] = "Mã giao dịch không được để trống.";
                return RedirectToAction("Checkout");
            }

            try
            {
                var url = _vnPayService.CreatePaymentVnpay(model, HttpContext);
                return Redirect(url);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Có lỗi khi tạo thanh toán VnPay: {ex.Message}";
                return RedirectToAction("Checkout");
            }
        }

        [HttpGet]
        public async Task<IActionResult> PaymentCallbackVnpay()
        {
            var response = _vnPayService.PaymentExecute(Request.Query);

            if (string.IsNullOrEmpty(response.OrderId))
            {
                TempData["ErrorMessage"] = "Mã đơn hàng không hợp lệ từ VnPay.";
                return RedirectToAction("Checkout");
            }

            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .Where(o => o.Id == response.OrderId)
                .FirstOrDefaultAsync();

            if (order == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Checkout");
            }

            try
            {
                if (response.Success)
                {
                    order.OrderStatus = OrderStatus.Processing;
                    _context.Update(order);

                    var payment = await _context.Payments
                        .Where(p => p.OrderId == response.OrderId)
                        .FirstOrDefaultAsync();
                    if (payment != null)
                    {
                        await _paymentService.ProcessPaymentAsync(payment.Id, "Paid", null);
                    }

                    var user = await _userManager.GetUserAsync(User);
                    if (user != null)
                    {
                        await SendOrderConfirmationEmail(user, order);
                        await SendPaymentConfirmationEmail(user, order, "VnPay");
                    }
                    await _context.SaveChangesAsync();
                }
                else
                {
                    order.OrderStatus = OrderStatus.Cancelled;
                    _context.Update(order);
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Có lỗi khi xử lý thanh toán: {ex.Message}";
                return RedirectToAction("Checkout");
            }

            return RedirectToAction("OrderConfirmation", new { orderId = response.OrderId });
        }

        [HttpGet]
        public async Task<IActionResult> GetProvinces()
        {
            try
            {
                var provinces = await _ghnService.GetProvincesAsync();
                if (provinces == null || !provinces.Any())
                {
                    return Json(new { code = 204, message = "No Content", data = new List<object>() });
                }
                return Json(provinces);
            }
            catch (Exception ex)
            {
                return Json(new { code = 500, message = "Error", error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetDistricts(int provinceId)
        {
            var districts = await _ghnService.GetDistrictsAsync(provinceId);
            return Json(districts);
        }

        [HttpGet]
        public async Task<IActionResult> GetWards(int districtId)
        {
            var wards = await _ghnService.GetWardsAsync(districtId);
            return Json(wards);
        }

        [HttpPost]
        public async Task<IActionResult> GetShipping(Shipping shippingModel, string quan, string tinh, string phuong)
        {
            try
            {
                // Lấy danh sách tỉnh thành để tìm province_id
                var provinces = await _ghnService.GetProvincesAsync();
                var province = provinces.FirstOrDefault(p => p.ProvinceName.Contains(tinh));
                if (province == null)
                {
                    return Json(new { error = "Không tìm thấy tỉnh/thành phố." });
                }

                // Lấy danh sách quận huyện để tìm district_id
                var districts = await _ghnService.GetDistrictsAsync(province.ProvinceID);
                var district = districts.FirstOrDefault(d => d.DistrictName.Contains(quan));
                if (district == null)
                {
                    return Json(new { error = "Không tìm thấy quận/huyện." });
                }

                // Lấy danh sách xã phường để tìm ward_code
                var wards = await _ghnService.GetWardsAsync(district.DistrictID);
                var ward = wards.FirstOrDefault(w => w.WardName.Contains(phuong));
                if (ward == null)
                {
                    return Json(new { error = "Không tìm thấy xã/phường." });
                }

                // Tính phí vận chuyển
                var shippingRequest = new ShippingFeeRequest
                {
                    FromDistrictId = 1450, // Thay bằng DistrictId của shop (cần cấu hình)
                    ToDistrictId = district.DistrictID,
                    ToWardCode = ward.WardCode,
                    ServiceId = 53321, // ID dịch vụ, cần lấy từ API hoặc cấu hình (53320 là dịch vụ tiêu chuẩn)
                    Weight = 2000, // Trọng lượng mặc định (gram), có thể lấy từ giỏ hàng
                    Length = 20,   // Kích thước mặc định (cm)
                    Width = 15,
                    Height = 10,
                    InsuranceValue = 12000, // Giá trị bảo hiểm
                };

                var shippingPrice = await _ghnService.CalculateShippingFeeAsync(shippingRequest);

                // Lưu phí vận chuyển vào cookie
                var shippingPriceJson = JsonConvert.SerializeObject(shippingPrice);
                var cookieOptions = new CookieOptions
                {
                    HttpOnly = true,
                    Expires = DateTimeOffset.UtcNow.AddMinutes(30),
                    Secure = true
                };
                Response.Cookies.Append("ShippingPrice", shippingPriceJson, cookieOptions);

                // Lưu thông tin vào cơ sở dữ liệu nếu cần
                var existingShipping = await _context.Shippings
                    .FirstOrDefaultAsync(x => x.City == tinh && x.District == quan && x.Ward == phuong);
                if (existingShipping == null)
                {
                    var newShipping = new Shipping
                    {
                        City = tinh,
                        District = quan,
                        Ward = phuong,
                        Price = shippingPrice
                    };
                    _context.Shippings.Add(newShipping);
                    await _context.SaveChangesAsync();
                }

                return Json(new { shippingPrice });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calculating shipping fee: {ex.Message}");
                return Json(new { error = "Có lỗi khi tính phí vận chuyển." });
            }
        }

        public async Task<IActionResult> History()
        {
            var userId = _userManager.GetUserId(User);

            var orders = await _context.Orders
                .Where(o => o.UserId == userId)
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            return View(orders);
        }

        public async Task<IActionResult> Details(string orderId)
        {
            var userId = _userManager.GetUserId(User);

            var order = await _context.Orders
                .Where(o => o.Id == orderId && o.UserId == userId)
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .FirstOrDefaultAsync();

            if (order == null)
            {
                return NotFound();
            }

            // Lấy thông tin thanh toán liên quan đến đơn hàng
            var payment = await _context.Payments
                .Where(p => p.OrderId == orderId)
                .FirstOrDefaultAsync();

            // Sử dụng trực tiếp PaymentMethod và PaymentStatusDisplay từ model
            ViewBag.PaymentMethod = payment?.PaymentMethodDisplay ?? "Không xác định";
            ViewBag.PaymentStatus = payment?.PaymentStatusDisplay ?? "Không xác định";

            return View(order);
        }

        public async Task<IActionResult> OrderConfirmation(string orderId)
        {
            var userId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(orderId))
            {
                TempData["ErrorMessage"] = "Mã đơn hàng không hợp lệ.";
                return RedirectToAction("Index", "ShoppingCart");
            }

            var order = await _context.Orders
                .Where(o => o.Id == orderId && o.UserId == userId)
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .FirstOrDefaultAsync();

            if (order == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy đơn hàng.";
                return RedirectToAction("Index", "ShoppingCart");
            }

            return View(order);
        }

        [HttpPost]
        public async Task<IActionResult> CancelOrder(string orderId)
        {
            var userId = _userManager.GetUserId(User);
            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .ThenInclude(od => od.Product)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.UserId == userId);

            if (order == null)
            {
                return Json(new { success = false, message = "Không tìm thấy đơn hàng hoặc bạn không có quyền hủy." });
            }

            // Kiểm tra trạng thái đơn hàng
            if (order.OrderStatus != OrderStatus.Pending && order.OrderStatus != OrderStatus.Processing)
            {
                return Json(new { success = false, message = "Đơn hàng không thể hủy ở trạng thái hiện tại." });
            }

            // Cập nhật trạng thái đơn hàng thành "Đã hủy"
            order.OrderStatus = OrderStatus.Cancelled;
            _context.Orders.Update(order);

            // Hoàn lại số lượng sản phẩm vào kho
            foreach (var detail in order.OrderDetails)
            {
                var product = await _context.Products.FindAsync(detail.ProductId);
                if (product != null)
                {
                    product.Quantity += detail.Quantity;
                    product.QuantitySold -= detail.Quantity;
                    _context.Products.Update(product);
                }
            }

            await _context.SaveChangesAsync();

            // Gửi email thông báo hủy đơn hàng
            var user = await _userManager.GetUserAsync(User);
            await SendOrderCancellationEmail(user, order);

            return Json(new { success = true, message = "Hủy đơn hàng thành công!" });
        }

        private async Task SendOrderConfirmationEmail(ApplicationUser user, Order order)
        {
            var subjectForSender = $"Xác Nhận Đơn Hàng #{order.Id}";
            var messageForSender = new StringBuilder();

            messageForSender.AppendLine("<!DOCTYPE html>");
            messageForSender.AppendLine("<html lang='vi'>");
            messageForSender.AppendLine("<head>");
            messageForSender.AppendLine("<meta charset='UTF-8'>");
            messageForSender.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            messageForSender.AppendLine("<title>Xác Nhận Đơn Hàng</title>");
            messageForSender.AppendLine("<style>");
            messageForSender.AppendLine("body { font-family: 'Arial', sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 0; background-color: #f4f4f4; }");
            messageForSender.AppendLine(".container { max-width: 650px; margin: 20px auto; padding: 20px; border-radius: 15px; background-color: #fff; box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1); }");
            messageForSender.AppendLine(".header { text-align: center; background-color: #ff6f61; color: white; padding: 20px; border-radius: 15px 15px 0 0; }");
            messageForSender.AppendLine(".content { padding: 25px; }");
            messageForSender.AppendLine(".footer { text-align: center; padding: 15px; border-top: 1px solid #e0e0e0; margin-top: 20px; font-size: 14px; color: #777; }");
            messageForSender.AppendLine("h2 { color: #ff6f61; font-size: 24px; margin-bottom: 15px; }");
            messageForSender.AppendLine("p { font-size: 16px; margin-bottom: 15px; }");
            messageForSender.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 15px; }");
            messageForSender.AppendLine("th, td { border: 1px solid #e0e0e0; padding: 12px; text-align: left; }");
            messageForSender.AppendLine("th { background-color: #f9f9f9; font-weight: 600; }");
            messageForSender.AppendLine("td { background-color: #fff; }");
            messageForSender.AppendLine(".button { display: inline-block; padding: 12px 25px; background-color: #ff6f61; color: white !important; text-decoration: none; border-radius: 8px; font-size: 16px; transition: background-color 0.3s; margin: 5px; }");
            messageForSender.AppendLine(".button:hover { background-color: #e65b50; color: white !important; }");
            messageForSender.AppendLine(".contact-btn { background-color: #4682b4; color: white !important; }");
            messageForSender.AppendLine(".contact-btn:hover { background-color: #3a6d9a; color: white !important; }");
            messageForSender.AppendLine(".button-container { text-align: center; margin-top: 20px; }");
            messageForSender.AppendLine("img { max-width: 70px; max-height: 70px; border-radius: 5px; }");
            messageForSender.AppendLine("</style>");
            messageForSender.AppendLine("</head>");
            messageForSender.AppendLine("<body>");

            messageForSender.AppendLine("<div class='container'>");
            messageForSender.AppendLine("<div class='header'>");
            messageForSender.AppendLine("<h1>Bloomie Shop 🎉</h1>");
            messageForSender.AppendLine("</div>");
            messageForSender.AppendLine("<div class='content'>");
            messageForSender.AppendLine($"<h2>Xác Nhận Đơn Hàng #{order.Id} 😊</h2>");
            messageForSender.AppendLine($"<p>Xin chào <strong>{order.SenderName}</strong>,</p>");
            messageForSender.AppendLine("<p>Cảm ơn bạn đã tin tưởng và mua sắm tại Bloomie Shop! Đơn hàng của bạn đã được đặt thành công. Dưới đây là thông tin chi tiết:</p>");

            messageForSender.AppendLine("<h3>Thông Tin Người Gửi & Người Nhận</h3>");
            messageForSender.AppendLine("<table>");
            messageForSender.AppendLine($"<tr><th>Người Gửi</th><td>{order.SenderName}<br>Email: {order.SenderEmail}<br>Số điện thoại: {order.SenderPhoneNumber}</td></tr>");
            messageForSender.AppendLine($"<tr><th>Người Nhận</th><td>{order.ReceiverName}<br>Số điện thoại: {order.ReceiverPhoneNumber}</td></tr>");
            messageForSender.AppendLine("</table>");

            messageForSender.AppendLine("<h3>Thông Tin Đơn Hàng</h3>");
            messageForSender.AppendLine("<table>");
            messageForSender.AppendLine($"<tr><th>Ngày Đặt Hàng</th><td>{order.OrderDate:dd/MM/yyyy HH:mm}</td></tr>");
            messageForSender.AppendLine($"<tr><th>Địa Chỉ Giao Hàng</th><td>{order.ShippingAddress}</td></tr>");
            messageForSender.AppendLine($"<tr><th>Phương Thức Giao Hàng</th><td>{order.ShippingMethod}</td></tr>");
            var firstDetail = order.OrderDetails.FirstOrDefault();
            var deliveryDateDisplay = firstDetail != null && firstDetail.DeliveryDate.HasValue ? firstDetail.DeliveryDate.Value.ToString("dd/MM/yyyy") : "Chưa xác định";
            var deliveryTimeDisplay = firstDetail != null && !string.IsNullOrEmpty(firstDetail.DeliveryTime) ? firstDetail.DeliveryTime : "Chưa xác định";
            messageForSender.AppendLine($"<tr><th>Ngày Giao Hàng</th><td>{deliveryDateDisplay}</td></tr>");
            messageForSender.AppendLine($"<tr><th>Khung Giờ Giao Hàng</th><td>{deliveryTimeDisplay}</td></tr>");
            var notesDisplay = string.IsNullOrEmpty(order.Notes) ? "Không có" : order.Notes;
            messageForSender.AppendLine($"<tr><th>Ghi Chú</th><td>{notesDisplay}</td></tr>");
            messageForSender.AppendLine($"<tr><th>Tổng Tiền</th><td>{order.TotalPrice:#,##0} đ</td></tr>");
            messageForSender.AppendLine("</table>");

            messageForSender.AppendLine("<h3>Chi Tiết Đơn Hàng</h3>");
            messageForSender.AppendLine("<table>");
            messageForSender.AppendLine("<tr><th>Hình Ảnh</th><th>Sản Phẩm</th><th>Số Lượng</th><th>Giá</th><th>Tổng</th></tr>");
            foreach (var detail in order.OrderDetails)
            {
                messageForSender.AppendLine($"<tr><td><img src='{detail.Product.ImageUrl}' alt='{detail.Product.Name}'></td><td>{detail.Product.Name}</td><td>{detail.Quantity}</td><td>{detail.Price:#,##0} đ</td><td>{detail.Price * detail.Quantity:#,##0} đ</td></tr>");
            }
            messageForSender.AppendLine("</table>");

            messageForSender.AppendLine("<div class='button-container'>");
            messageForSender.AppendLine($"<a href='http://localhost:5187/Order/Details?orderId={order.Id}' class='button' style='color: white !important; background-color: #ff6f61; padding: 12px 25px; text-decoration: none; border-radius: 8px; margin: 5px; display: inline-block;'>Xem Chi Tiết Đơn Hàng</a>");
            messageForSender.AppendLine($"<a href='mailto:bloomieshop25@gmail.com' class='button contact-btn' style='color: white !important; background-color: #4682b4; padding: 12px 25px; text-decoration: none; border-radius: 8px; margin: 5px; display: inline-block;'>Liên Hệ Ngay 🚚</a>");
            messageForSender.AppendLine("</div>");
            messageForSender.AppendLine("<p>Chúng tôi sẽ xử lý đơn hàng của bạn trong thời gian sớm nhất. Nếu bạn có thắc mắc, đừng ngần ngại liên hệ qua email <a href='mailto:bloomieshop25@gmail.com'>bloomieshop@gmail.com</a> hoặc số 0123 456 789 nhé! 😄</p>");
            messageForSender.AppendLine("</div>");
            messageForSender.AppendLine("<div class='footer'>");
            messageForSender.AppendLine("© 2025 Bloomie Shop. Tất cả quyền được bảo lưu.");
            messageForSender.AppendLine("</div>");
            messageForSender.AppendLine("</div>");
            messageForSender.AppendLine("</body>");
            messageForSender.AppendLine("</html>");

            await _emailService.SendEmailAsync(order.SenderEmail, subjectForSender, messageForSender.ToString());
        }

        private async Task SendPaymentConfirmationEmail(ApplicationUser user, Order order, string paymentMethod)
        {
            var subject = $"Xác Nhận Thanh Toán Đơn Hàng #{order.Id}";
            var message = new StringBuilder();

            message.AppendLine("<!DOCTYPE html>");
            message.AppendLine("<html lang='vi'>");
            message.AppendLine("<head>");
            message.AppendLine("<meta charset='UTF-8'>");
            message.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            message.AppendLine("<title>Xác Nhận Thanh Toán</title>");
            message.AppendLine("<style>");
            message.AppendLine("body { font-family: 'Arial', sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 0; background-color: #f4f4f4; }");
            message.AppendLine(".container { max-width: 650px; margin: 20px auto; padding: 20px; border-radius: 15px; background-color: #fff; box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1); }");
            message.AppendLine(".header { text-align: center; background-color: #ff6f61; color: white; padding: 20px; border-radius: 15px 15px 0 0; }");
            message.AppendLine(".content { padding: 25px; }");
            message.AppendLine(".footer { text-align: center; padding: 15px; border-top: 1px solid #e0e0e0; margin-top: 20px; font-size: 14px; color: #777; }");
            message.AppendLine("h2 { color: #ff6f61; font-size: 24px; margin-bottom: 15px; }");
            message.AppendLine("p { font-size: 16px; margin-bottom: 15px; }");
            message.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 15px; }");
            message.AppendLine("th, td { border: 1px solid #e0e0e0; padding: 12px; text-align: left; }");
            message.AppendLine("th { background-color: #f9f9f9; font-weight: 600; }");
            message.AppendLine("td { background-color: #fff; }");
            message.AppendLine(".button { display: inline-block; padding: 12px 25px; background-color: #ff6f61; color: white !important; text-decoration: none; border-radius: 8px; font-size: 16px; transition: background-color 0.3s; margin: 5px; }");
            message.AppendLine(".button:hover { background-color: #e65b50; color: white !important; }");
            message.AppendLine(".contact-btn { background-color: #4682b4; color: white !important; }");
            message.AppendLine(".contact-btn:hover { background-color: #3a6d9a; color: white !important; }");
            message.AppendLine(".button-container { text-align: center; margin-top: 20px; }");
            message.AppendLine("img { max-width: 70px; max-height: 70px; border-radius: 5px; }");
            message.AppendLine("</style>");
            message.AppendLine("</head>");
            message.AppendLine("<body>");

            message.AppendLine("<div class='container'>");
            message.AppendLine("<div class='header'>");
            message.AppendLine("<h1>Bloomie Shop 🎉</h1>");
            message.AppendLine("</div>");
            message.AppendLine("<div class='content'>");
            message.AppendLine($"<h2>Xác Nhận Thanh Toán Đơn Hàng #{order.Id} 😊</h2>");
            message.AppendLine($"<p>Xin chào <strong>{order.SenderName}</strong>,</p>");
            message.AppendLine("<p>Chúc mừng bạn! Thanh toán cho đơn hàng của bạn đã hoàn tất thành công. Dưới đây là thông tin chi tiết:</p>");

            message.AppendLine("<h3>Thông Tin Thanh Toán</h3>");
            message.AppendLine("<table>");
            message.AppendLine($"<tr><th>Mã Đơn Hàng</th><td>#{order.Id}</td></tr>");
            message.AppendLine($"<tr><th>Phương Thức Thanh Toán</th><td>{paymentMethod}</td></tr>");
            message.AppendLine($"<tr><th>Tổng Tiền</th><td>{order.TotalPrice:#,##0} đ</td></tr>");
            message.AppendLine($"<tr><th>Ngày Thanh Toán</th><td>{DateTime.Now:dd/MM/yyyy HH:mm}</td></tr>");
            message.AppendLine("</table>");

            message.AppendLine("<h3>Thông Tin Đơn Hàng</h3>");
            message.AppendLine("<table>");
            message.AppendLine($"<tr><th>Ngày Đặt Hàng</th><td>{order.OrderDate:dd/MM/yyyy HH:mm}</td></tr>");
            message.AppendLine($"<tr><th>Địa Chỉ Giao Hàng</th><td>{order.ShippingAddress}</td></tr>");
            message.AppendLine($"<tr><th>Người Nhận</th><td>{order.ReceiverName}<br>Số điện thoại: {order.ReceiverPhoneNumber}</td></tr>");
            var firstDetailPayment = order.OrderDetails.FirstOrDefault();
            var deliveryDateDisplayPayment = firstDetailPayment != null && firstDetailPayment.DeliveryDate.HasValue ? firstDetailPayment.DeliveryDate.Value.ToString("dd/MM/yyyy") : "Chưa xác định";
            var deliveryTimeDisplayPayment = firstDetailPayment != null && !string.IsNullOrEmpty(firstDetailPayment.DeliveryTime) ? firstDetailPayment.DeliveryTime : "Chưa xác định";
            message.AppendLine($"<tr><th>Ngày Giao Hàng</th><td>{deliveryDateDisplayPayment}</td></tr>");
            message.AppendLine($"<tr><th>Khung Giờ Giao Hàng</th><td>{deliveryTimeDisplayPayment}</td></tr>");
            message.AppendLine("</table>");

            message.AppendLine("<h3>Chi Tiết Đơn Hàng</h3>");
            message.AppendLine("<table>");
            message.AppendLine("<tr><th>Hình Ảnh</th><th>Sản Phẩm</th><th>Số Lượng</th><th>Giá</th><th>Tổng</th></tr>");
            foreach (var detail in order.OrderDetails)
            {
                message.AppendLine($"<tr><td><img src='{detail.Product.ImageUrl}' alt='{detail.Product.Name}'></td><td>{detail.Product.Name}</td><td>{detail.Quantity}</td><td>{detail.Price:#,##0} đ</td><td>{detail.Price * detail.Quantity:#,##0} đ</td></tr>");
            }
            message.AppendLine("</table>");

            message.AppendLine("<div class='button-container'>");
            message.AppendLine($"<a href='http://localhost:5187/Order/Details?orderId={order.Id}' class='button' style='color: white !important; background-color: #ff6f61; padding: 12px 25px; text-decoration: none; border-radius: 8px; margin: 5px; display: inline-block;'>Xem Chi Tiết Đơn Hàng</a>");
            message.AppendLine($"<a href='mailto:bloomieshop25@gmail.com' class='button contact-btn' style='color: white !important; background-color: #4682b4; padding: 12px 25px; text-decoration: none; border-radius: 8px; margin: 5px; display: inline-block;'>Liên Hệ Ngay 🚚</a>");
            message.AppendLine("</div>");
            message.AppendLine("<p>Đơn hàng của bạn đang được chuẩn bị giao. Chúng tôi sẽ thông báo khi hàng đến tay bạn! Nếu cần hỗ trợ, hãy liên hệ qua <a href='mailto:bloomieshop25@gmail.com'>bloomieshop@gmail.com</a> hoặc 0123 456 789 nhé! 😄</p>");
            message.AppendLine("</div>");
            message.AppendLine("<div class='footer'>");
            message.AppendLine("© 2025 Bloomie Shop. Tất cả quyền được bảo lưu.");
            message.AppendLine("</div>");
            message.AppendLine("</div>");
            message.AppendLine("</body>");
            message.AppendLine("</html>");

            await _emailService.SendEmailAsync(order.SenderEmail, subject, message.ToString());
        }

        private async Task SendOrderCancellationEmail(ApplicationUser user, Order order)
        {
            var subject = $"Thông Báo Hủy Đơn Hàng #{order.Id}";
            var message = new StringBuilder();

            message.AppendLine("<!DOCTYPE html>");
            message.AppendLine("<html lang='vi'>");
            message.AppendLine("<head>");
            message.AppendLine("<meta charset='UTF-8'>");
            message.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            message.AppendLine("<title>Thông Báo Hủy Đơn Hàng</title>");
            message.AppendLine("<style>");
            message.AppendLine("body { font-family: 'Arial', sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 0; background-color: #f4f4f4; }");
            message.AppendLine(".container { max-width: 650px; margin: 20px auto; padding: 20px; border-radius: 15px; background-color: #fff; box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1); }");
            message.AppendLine(".header { text-align: center; background-color: #ff6f61; color: white; padding: 20px; border-radius: 15px 15px 0 0; }");
            message.AppendLine(".content { padding: 25px; }");
            message.AppendLine(".footer { text-align: center; padding: 15px; border-top: 1px solid #e0e0e0; margin-top: 20px; font-size: 14px; color: #777; }");
            message.AppendLine("h2 { color: #ff6f61; font-size: 24px; margin-bottom: 15px; }");
            message.AppendLine("p { font-size: 16px; margin-bottom: 15px; }");
            message.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 15px; }");
            message.AppendLine("th, td { border: 1px solid #e0e0e0; padding: 12px; text-align: left; }");
            message.AppendLine("th { background-color: #f9f9f9; font-weight: 600; }");
            message.AppendLine("td { background-color: #fff; }");
            message.AppendLine(".button { display: inline-block; padding: 12px 25px; background-color: #ff6f61; color: white !important; text-decoration: none; border-radius: 8px; font-size: 16px; transition: background-color 0.3s; margin: 5px; }");
            message.AppendLine(".button:hover { background-color: #e65b50; color: white !important; }");
            message.AppendLine(".contact-btn { background-color: #4682b4; color: white !important; }");
            message.AppendLine(".contact-btn:hover { background-color: #3a6d9a; color: white !important; }");
            message.AppendLine(".button-container { text-align: center; margin-top: 20px; }");
            message.AppendLine("img { max-width: 70px; max-height: 70px; border-radius: 5px; }");
            message.AppendLine("</style>");
            message.AppendLine("</head>");
            message.AppendLine("<body>");

            message.AppendLine("<div class='container'>");
            message.AppendLine("<div class='header'>");
            message.AppendLine("<h1>Bloomie Shop 🎉</h1>");
            message.AppendLine("</div>");
            message.AppendLine("<div class='content'>");
            message.AppendLine($"<h2>Thông Báo Hủy Đơn Hàng #{order.Id} 😔</h2>");
            message.AppendLine($"<p>Xin chào <strong>{order.SenderName}</strong>,</p>");
            message.AppendLine("<p>Đơn hàng của bạn đã được hủy theo yêu cầu. Chúng tôi rất tiếc vì điều này! Dưới đây là thông tin chi tiết:</p>");

            message.AppendLine("<h3>Thông Tin Đơn Hàng</h3>");
            message.AppendLine("<table>");
            message.AppendLine($"<tr><th>Mã Đơn Hàng</th><td>#{order.Id}</td></tr>");
            message.AppendLine($"<tr><th>Ngày Đặt Hàng</th><td>{order.OrderDate:dd/MM/yyyy HH:mm}</td></tr>");
            var firstDetailCancel = order.OrderDetails.FirstOrDefault();
            var deliveryDateDisplayCancel = firstDetailCancel != null && firstDetailCancel.DeliveryDate.HasValue ? firstDetailCancel.DeliveryDate.Value.ToString("dd/MM/yyyy") : "Chưa xác định";
            var deliveryTimeDisplayCancel = firstDetailCancel != null && !string.IsNullOrEmpty(firstDetailCancel.DeliveryTime) ? firstDetailCancel.DeliveryTime : "Chưa xác định";
            message.AppendLine($"<tr><th>Ngày Giao Hàng</th><td>{deliveryDateDisplayCancel}</td></tr>");
            message.AppendLine($"<tr><th>Khung Giờ Giao Hàng</th><td>{deliveryTimeDisplayCancel}</td></tr>");
            message.AppendLine($"<tr><th>Tổng Tiền</th><td>{order.TotalPrice:#,##0} đ</td></tr>");
            message.AppendLine($"<tr><th>Trạng Thái</th><td>{OrderStatusHelper.GetStatusDescription(order.OrderStatus)}</td></tr>");
            message.AppendLine("</table>");

            message.AppendLine("<h3>Chi Tiết Đơn Hàng</h3>");
            message.AppendLine("<table>");
            message.AppendLine("<tr><th>Hình Ảnh</th><th>Sản Phẩm</th><th>Số Lượng</th><th>Giá</th><th>Tổng</th></tr>");
            foreach (var detail in order.OrderDetails)
            {
                message.AppendLine($"<tr><td><img src='{detail.Product.ImageUrl}' alt='{detail.Product.Name}'></td><td>{detail.Product.Name}</td><td>{detail.Quantity}</td><td>{detail.Price:#,##0} đ</td><td>{detail.Price * detail.Quantity:#,##0} đ</td></tr>");
            }
            message.AppendLine("</table>");

            message.AppendLine("<div class='button-container'>");
            message.AppendLine($"<a href='http://localhost:5187/Order/Details?orderId={order.Id}' class='button' style='color: white !important; background-color: #ff6f61; padding: 12px 25px; text-decoration: none; border-radius: 8px; margin: 5px; display: inline-block;'>Xem Chi Tiết Đơn Hàng</a>");
            message.AppendLine($"<a href='mailto:bloomieshop25@gmail.com' class='button contact-btn' style='color: white !important; background-color: #4682b4; padding: 12px 25px; text-decoration: none; border-radius: 8px; margin: 5px; display: inline-block;'>Liên Hệ Ngay 🚚</a>");
            message.AppendLine("</div>");
            message.AppendLine("<p>Nếu bạn cần hỗ trợ hoặc muốn đặt lại đơn hàng, hãy liên hệ qua <a href='mailto:bloomieshop25@gmail.com'>bloomieshop@gmail.com</a> hoặc 0123 456 789 nhé! 😊</p>");
            message.AppendLine("</div>");
            message.AppendLine("<div class='footer'>");
            message.AppendLine("© 2025 Bloomie Shop. Tất cả quyền được bảo lưu.");
            message.AppendLine("</div>");
            message.AppendLine("</div>");
            message.AppendLine("</body>");
            message.AppendLine("</html>");

            await _emailService.SendEmailAsync(order.SenderEmail, subject, message.ToString());
        }
    }
}