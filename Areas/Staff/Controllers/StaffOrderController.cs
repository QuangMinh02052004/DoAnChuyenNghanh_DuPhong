using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Bloomie.Data;
using System.Linq;
using System.Threading.Tasks;
using Bloomie.Models.Entities;
using Bloomie.Services.Interfaces;
using Bloomie.Services.Implementations;
using System.Text;

namespace Bloomie.Areas.Staff.Controllers
{
    [Area("Staff")]
    [Authorize(Roles = "Staff")]
    public class StaffOrderController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;

        public StaffOrderController(ApplicationDbContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        // Hiển thị danh sách đơn hàng chờ duyệt
        [HttpGet]
        public async Task<IActionResult> Index(int pageNumber = 1, string searchString = null, OrderStatus? statusFilter = null)
        {
            int pageSize = 10;
            var query = _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product)
                .AsQueryable();

            if (statusFilter.HasValue)
            {
                query = query.Where(o => o.OrderStatus == statusFilter.Value);
            }

            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(o => o.Id.Contains(searchString) ||
                                         (o.User != null && o.User.FullName.Contains(searchString)) ||
                                         (o.ShippingAddress != null && o.ShippingAddress.Contains(searchString)));
            }

            query = query.OrderByDescending(o => o.OrderDate);

            int totalItems = await query.CountAsync();
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            var orders = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewData["CurrentPage"] = pageNumber;
            ViewData["TotalPages"] = totalPages;
            ViewData["TotalItems"] = totalItems;
            ViewData["PageSize"] = pageSize;
            ViewData["SearchString"] = searchString;
            ViewData["StatusFilter"] = statusFilter;

            return View(orders);
        }

        // Xem chi tiết đơn hàng
        [HttpGet]
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id))
                return NotFound();

            var order = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
                return NotFound();

            return View(order);
        }

        // Duyệt đơn hàng
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(string id, int currentPage = 1)
        {
            if (string.IsNullOrEmpty(id))
                return NotFound();

            var order = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null)
                return NotFound();

            // Chỉ cho phép duyệt đơn khi đang ở trạng thái Pending
            if (order.OrderStatus != OrderStatus.Pending)
            {
                TempData["error"] = "Chỉ có thể duyệt đơn hàng ở trạng thái 'Chờ xác nhận'.";
                return RedirectToAction(nameof(Index), new { pageNumber = currentPage });
            }

            order.OrderStatus = OrderStatus.Processing;

            // Lưu lịch sử thay đổi trạng thái
            order.StatusHistory.Add(new OrderStatusHistory
            {
                OrderId = order.Id,
                Status = OrderStatus.Processing,
                ChangeDate = DateTime.Now,
                ChangedBy = User.Identity?.Name ?? "Staff"
            });

            _context.Orders.Update(order);
            await _context.SaveChangesAsync();

            // Gửi email thông báo xác nhận đơn hàng
            if (!string.IsNullOrEmpty(order.User?.Email))
            {
                var subject = $"Thông Báo: Đơn Hàng #{order.Id} Đã Được Xác Nhận";
                var message = new StringBuilder();

                message.AppendLine("<!DOCTYPE html>");
                message.AppendLine("<html lang='vi'>");
                message.AppendLine("<head>");
                message.AppendLine("<meta charset='UTF-8'>");
                message.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
                message.AppendLine("<title>Thông Báo Xác Nhận Đơn Hàng</title>");
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
                message.AppendLine($"<h2>Đơn Hàng #{order.Id} Đã Được Xác Nhận 😊</h2>");
                message.AppendLine($"<p>Xin chào <strong>{order.User?.FullName ?? "Khách hàng"}</strong>,</p>");
                message.AppendLine("<p>Đơn hàng của bạn đã được đội ngũ nhân viên Bloomie xác nhận và đang được xử lý. Dưới đây là thông tin chi tiết:</p>");

                message.AppendLine("<h3>Thông Tin Đơn Hàng</h3>");
                message.AppendLine("<table>");
                message.AppendLine($"<tr><th>Mã Đơn Hàng</th><td>#{order.Id}</td></tr>");
                message.AppendLine($"<tr><th>Ngày Đặt Hàng</th><td>{order.OrderDate:dd/MM/yyyy HH:mm}</td></tr>");
                message.AppendLine($"<tr><th>Địa Chỉ Giao Hàng</th><td>{order.ShippingAddress}</td></tr>");
                var firstDetail = order.OrderDetails.FirstOrDefault();
                var deliveryDateDisplay = firstDetail != null && firstDetail.DeliveryDate.HasValue ? firstDetail.DeliveryDate.Value.ToString("dd/MM/yyyy") : "Chưa xác định";
                var deliveryTimeDisplay = firstDetail != null && !string.IsNullOrEmpty(firstDetail.DeliveryTime) ? firstDetail.DeliveryTime : "Chưa xác định";
                message.AppendLine($"<tr><th>Ngày Giao Hàng</th><td>{deliveryDateDisplay}</td></tr>");
                message.AppendLine($"<tr><th>Khung Giờ Giao Hàng</th><td>{deliveryTimeDisplay}</td></tr>");
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
                message.AppendLine("<p>Chúng tôi đang chuẩn bị đơn hàng của bạn và sẽ thông báo khi hàng được giao. Nếu có thắc mắc, hãy liên hệ qua <a href='mailto:bloomieshop25@gmail.com'>bloomieshop@gmail.com</a> hoặc 0123 456 789 nhé! 😄</p>");
                message.AppendLine("</div>");
                message.AppendLine("<div class='footer'>");
                message.AppendLine("© 2025 Bloomie Shop. Tất cả quyền được bảo lưu.");
                message.AppendLine("</div>");
                message.AppendLine("</div>");
                message.AppendLine("</body>");
                message.AppendLine("</html>");

                try
                {
                    await _emailService.SendEmailAsync(order.User.Email, subject, message.ToString());
                }
                catch (Exception ex)
                {
                    TempData["error"] = "Đã duyệt đơn hàng, nhưng không thể gửi email thông báo.";
                }
            }

            TempData["success"] = "Đơn hàng đã được duyệt và chuyển sang trạng thái 'Đang xử lý'.";
            return RedirectToAction(nameof(Index), new { pageNumber = currentPage });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(string id, OrderStatus newStatus, int currentPage = 1)
        {
            if (string.IsNullOrEmpty(id))
                return NotFound();

            var order = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null)
                return NotFound();

            if (newStatus <= order.OrderStatus)
            {
                TempData["error"] = "Không thể cập nhật trạng thái này. Vui lòng kiểm tra trạng thái hiện tại.";
                return RedirectToAction(nameof(Index), new { pageNumber = currentPage });
            }

            order.OrderStatus = newStatus;

            order.StatusHistory.Add(new OrderStatusHistory
            {
                OrderId = order.Id,
                Status = newStatus,
                ChangeDate = DateTime.Now,
                ChangedBy = User.Identity?.Name ?? "Staff"
            });

            _context.Orders.Update(order);
            await _context.SaveChangesAsync();

            // Chỉ gửi email khi trạng thái là Delivered
            if (newStatus == OrderStatus.Delivered && !string.IsNullOrEmpty(order.User?.Email))
            {
                var subject = $"Thông Báo: Đơn Hàng #{order.Id} Đã Được Giao Thành Công";
                var message = new StringBuilder();

                message.AppendLine("<!DOCTYPE html>");
                message.AppendLine("<html lang='vi'>");
                message.AppendLine("<head>");
                message.AppendLine("<meta charset='UTF-8'>");
                message.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
                message.AppendLine("<title>Thông Báo Giao Hàng Thành Công</title>");
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
                message.AppendLine($"<h2>Đơn Hàng #{order.Id} Đã Được Giao Thành Công 😊</h2>");
                message.AppendLine($"<p>Xin chào <strong>{order.User?.FullName ?? "Khách hàng"}</strong>,</p>");
                message.AppendLine("<p>Chúc mừng bạn! Đơn hàng của bạn đã được giao thành công. Dưới đây là thông tin chi tiết:</p>");

                message.AppendLine("<h3>Thông Tin Đơn Hàng</h3>");
                message.AppendLine("<table>");
                message.AppendLine($"<tr><th>Mã Đơn Hàng</th><td>#{order.Id}</td></tr>");
                message.AppendLine($"<tr><th>Ngày Đặt Hàng</th><td>{order.OrderDate:dd/MM/yyyy HH:mm}</td></tr>");
                message.AppendLine($"<tr><th>Địa Chỉ Giao Hàng</th><td>{order.ShippingAddress}</td></tr>");
                var firstDetail = order.OrderDetails.FirstOrDefault();
                var deliveryDateDisplay = firstDetail != null && firstDetail.DeliveryDate.HasValue ? firstDetail.DeliveryDate.Value.ToString("dd/MM/yyyy") : "Chưa xác định";
                var deliveryTimeDisplay = firstDetail != null && !string.IsNullOrEmpty(firstDetail.DeliveryTime) ? firstDetail.DeliveryTime : "Chưa xác định";
                message.AppendLine($"<tr><th>Ngày Giao Hàng</th><td>{deliveryDateDisplay}</td></tr>");
                message.AppendLine($"<tr><th>Khung Giờ Giao Hàng</th><td>{deliveryTimeDisplay}</td></tr>");
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
                message.AppendLine("<p>Cảm ơn bạn đã mua sắm tại Bloomie Shop! Nếu có bất kỳ thắc mắc nào, vui lòng liên hệ qua <a href='mailto:bloomieshop25@gmail.com'>bloomieshop@gmail.com</a> hoặc 0123 456 789 nhé! 😄</p>");
                message.AppendLine("</div>");
                message.AppendLine("<div class='footer'>");
                message.AppendLine("© 2025 Bloomie Shop. Tất cả quyền được bảo lưu.");
                message.AppendLine("</div>");
                message.AppendLine("</div>");
                message.AppendLine("</body>");
                message.AppendLine("</html>");

                try
                {
                    await _emailService.SendEmailAsync(order.User.Email, subject, message.ToString());
                }
                catch (Exception ex)
                {
                    TempData["error"] = "Đã cập nhật trạng thái, nhưng không thể gửi email thông báo.";
                }
            }

            TempData["success"] = $"Đơn hàng đã được cập nhật trạng thái thành {OrderStatusHelper.GetStatusDescription(newStatus)}.";
            return RedirectToAction(nameof(Index), new { pageNumber = currentPage });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(string id, int currentPage = 1)
        {
            if (string.IsNullOrEmpty(id))
                return NotFound();

            var order = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null)
                return NotFound();

            // Chỉ cho phép hủy đơn khi đang ở trạng thái Pending hoặc Processing
            if (order.OrderStatus != OrderStatus.Pending && order.OrderStatus != OrderStatus.Processing)
            {
                TempData["error"] = "Chỉ có thể hủy đơn hàng ở trạng thái 'Chờ xác nhận' hoặc 'Đang xử lý'.";
                return RedirectToAction(nameof(Index), new { pageNumber = currentPage });
            }

            order.OrderStatus = OrderStatus.Cancelled;

            order.StatusHistory.Add(new OrderStatusHistory
            {
                OrderId = order.Id,
                Status = OrderStatus.Cancelled,
                ChangeDate = DateTime.Now,
                ChangedBy = User.Identity?.Name ?? "Staff"
            });

            _context.Orders.Update(order);
            await _context.SaveChangesAsync();

            // Gửi email thông báo hủy đơn
            if (!string.IsNullOrEmpty(order.User?.Email))
            {
                var subject = $"Thông Báo: Đơn Hàng #{order.Id} Đã Bị Hủy Bởi Nhân Viên";
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
                message.AppendLine($"<p>Xin chào <strong>{order.User?.FullName ?? "Khách hàng"}</strong>,</p>");
                message.AppendLine("<p>Chúng tôi rất tiếc phải thông báo rằng đơn hàng của bạn đã bị hủy bởi đội ngũ nhân viên Bloomie. Dưới đây là thông tin chi tiết:</p>");

                message.AppendLine("<h3>Thông Tin Đơn Hàng</h3>");
                message.AppendLine("<table>");
                message.AppendLine($"<tr><th>Mã Đơn Hàng</th><td>#{order.Id}</td></tr>");
                message.AppendLine($"<tr><th>Ngày Đặt Hàng</th><td>{order.OrderDate:dd/MM/yyyy HH:mm}</td></tr>");
                var firstDetail = order.OrderDetails.FirstOrDefault();
                var deliveryDateDisplay = firstDetail != null && firstDetail.DeliveryDate.HasValue ? firstDetail.DeliveryDate.Value.ToString("dd/MM/yyyy") : "Chưa xác định";
                var deliveryTimeDisplay = firstDetail != null && !string.IsNullOrEmpty(firstDetail.DeliveryTime) ? firstDetail.DeliveryTime : "Chưa xác định";
                message.AppendLine($"<tr><th>Ngày Giao Hàng</th><td>{deliveryDateDisplay}</td></tr>");
                message.AppendLine($"<tr><th>Khung Giờ Giao Hàng</th><td>{deliveryTimeDisplay}</td></tr>");
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

                try
                {
                    await _emailService.SendEmailAsync(order.User.Email, subject, message.ToString());
                }
                catch (Exception ex)
                {
                    TempData["error"] = "Đã hủy đơn hàng, nhưng không thể gửi email thông báo.";
                }
            }

            TempData["success"] = "Đơn hàng đã bị hủy thành công.";
            return RedirectToAction(nameof(Index), new { pageNumber = currentPage });
        }
    }
}