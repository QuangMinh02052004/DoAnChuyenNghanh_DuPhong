using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Bloomie.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Bloomie.Data;
using System.Linq;
using System.Threading.Tasks;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminOrderController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminOrderController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 1. Hiển thị danh sách đơn hàng với phân trang và tìm kiếm
        [HttpGet]
        public async Task<IActionResult> Index(int pageNumber = 1, string searchString = null)
        {
            // Thiết lập kích thước trang
            int pageSize = 10; // Số đơn hàng mỗi trang, có thể tùy chỉnh

            // Lấy danh sách đơn hàng từ database
            var query = _context.Orders
                .Include(o => o.User) // Lấy thông tin khách hàng
                .Include(o => o.OrderDetails) // Lấy chi tiết đơn hàng
                .ThenInclude(d => d.Product) // Bao gồm Product để tránh null
                .AsQueryable();

            // Áp dụng tìm kiếm nếu có
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(o => o.Id.Contains(searchString) ||
                                        (o.User != null && o.User.FullName.Contains(searchString)) ||
                                        (o.ShippingAddress != null && o.ShippingAddress.Contains(searchString)));
            }

            // Sắp xếp theo ngày đặt hàng (mới nhất trước)
            query = query.OrderByDescending(o => o.OrderDate);

            // Tính toán phân trang
            int totalItems = await query.CountAsync();
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            // Lấy đơn hàng cho trang hiện tại
            var orders = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Truyền các giá trị cần thiết vào ViewData
            ViewData["CurrentPage"] = pageNumber;
            ViewData["TotalPages"] = totalPages;
            ViewData["TotalItems"] = totalItems;
            ViewData["PageSize"] = pageSize;
            ViewData["SearchString"] = searchString;

            return View(orders);
        }

        // 2. Xem chi tiết đơn hàng
        [HttpGet]
        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var order = await _context.Orders
                .Include(o => o.User)
                .Include(o => o.OrderDetails)
                .ThenInclude(d => d.Product) // Bao gồm Product để tránh null
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
            {
                return NotFound();
            }

            return View(order);
        }

        // 3. Xóa đơn hàng (thêm để hỗ trợ chức năng xóa trong view)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id, int currentPage = 1)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var order = await _context.Orders.FindAsync(id);
            if (order == null)
            {
                return NotFound();
            }

            _context.Orders.Remove(order);
            await _context.SaveChangesAsync();

            // Chuyển hướng về trang Index, giữ lại trang hiện tại
            return RedirectToAction(nameof(Index), new { pageNumber = currentPage });
        }
    }
}