using Bloomie.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Bloomie.Models;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")] // Sử dụng SD.Role_Admin nếu SD là class chứa hằng số
    public class AdminUserController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;

        public AdminUserController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        public async Task<IActionResult> Index(int pageNumber = 1, string searchString = null)
        {
            // Định nghĩa số lượng người dùng trên mỗi trang
            int pageSize = 10; // Số lượng người dùng trên mỗi trang (có thể điều chỉnh)

            // Lấy tất cả người dùng cùng vai trò của họ
            var usersQuery = from u in _context.Users
                             join ur in _context.UserRoles on u.Id equals ur.UserId into userRoles
                             from ur in userRoles.DefaultIfEmpty()
                             join r in _context.Roles on ur.RoleId equals r.Id into roles
                             from r in roles.DefaultIfEmpty()
                             group new { r.Name } by u into g
                             select new
                             {
                                 User = g.Key,
                                 Roles = g.Select(x => x.Name).Where(x => x != null).ToList()
                             };

            // Áp dụng bộ lọc tìm kiếm nếu có searchString
            if (!string.IsNullOrEmpty(searchString))
            {
                usersQuery = usersQuery.Where(u => u.User.UserName.Contains(searchString) || u.User.Email.Contains(searchString));
            }

            // Tính tổng số mục
            int totalItems = await usersQuery.CountAsync();

            // Tính toán phân trang
            int totalPages = (int)Math.Ceiling((double)totalItems / pageSize);
            pageNumber = Math.Max(1, Math.Min(pageNumber, totalPages)); // Đảm bảo pageNumber nằm trong phạm vi hợp lệ

            // Lấy người dùng cho trang hiện tại
            var usersWithRoles = await usersQuery
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Truyền dữ liệu phân trang cho view
            ViewData["CurrentPage"] = pageNumber;
            ViewData["TotalPages"] = totalPages;
            ViewData["TotalItems"] = totalItems;
            ViewData["PageSize"] = pageSize;
            ViewData["SearchString"] = searchString;

            // Truyền ID của người dùng hiện tại
            var currentUser = await _userManager.GetUserAsync(User);
            ViewBag.loggedInUserId = currentUser?.Id;

            return View(usersWithRoles);
        }

        public async Task<IActionResult> Add()
        {
            var roles = await _roleManager.Roles.ToListAsync();
            ViewBag.Roles = new SelectList(roles, "Id", "Name");
            return View(new ApplicationUser());
        }

        [HttpPost]
        public async Task<IActionResult> Add(ApplicationUser user, string RoleId, string password)
        {
            if (string.IsNullOrEmpty(user.Token))
            {
                user.Token = Guid.NewGuid().ToString(); // Tạo giá trị mặc định (GUID)
            }

            ModelState.Remove("Token");

            if (ModelState.IsValid)
            {
                var result = await _userManager.CreateAsync(user, password);
                if (result.Succeeded)
                {
                    if (!string.IsNullOrEmpty(RoleId))
                    {
                        var role = await _roleManager.FindByIdAsync(RoleId);
                        if (role != null)
                        {
                            await _userManager.AddToRoleAsync(user, role.Name);
                        }
                    }
                    TempData["success"] = "Tạo user thành công";
                    return RedirectToAction("Index", "AdminUser");
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }
            }

            var roles = await _roleManager.Roles.ToListAsync();
            ViewBag.Roles = new SelectList(roles, "Id", "Name");
            return View(user);
        }

        // GET: Hiển thị form chỉnh sửa
        [HttpGet]
        public async Task<IActionResult> Update(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            // Lấy danh sách vai trò hiện tại của người dùng
            var roles = await _userManager.GetRolesAsync(user);
            ViewBag.CurrentRoles = roles; // Lưu vai trò hiện tại để hiển thị trong form
            ViewBag.AllRoles = new SelectList(await _roleManager.Roles.ToListAsync(), "Name", "Name"); // Sử dụng "Name" cho cả value và text

            return View(user);
        }

        // POST: Xử lý chỉnh sửa người dùng
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(string id, ApplicationUser user, string[] selectedRoles, string password)
        {
            // Xóa validation không cần thiết
            ModelState.Remove("RoleId");
            ModelState.Remove("password");
            ModelState.Remove("Token"); // Bỏ qua validation cho Token

            // Gán giá trị mặc định cho Token nếu không có
            if (string.IsNullOrEmpty(user.Token))
            {
                user.Token = Guid.NewGuid().ToString();
            }

            if (string.IsNullOrEmpty(id) || id != user.Id)
            {
                return NotFound();
            }

            var existingUser = await _userManager.FindByIdAsync(id);
            if (existingUser == null)
            {
                return NotFound();
            }

            // Đảm bảo chọn ít nhất một vai trò
            if (selectedRoles == null || !selectedRoles.Any())
            {
                ModelState.AddModelError("", "Vui lòng chọn ít nhất một vai trò.");
                ViewBag.AllRoles = new SelectList(await _roleManager.Roles.ToListAsync(), "Name", "Name");
                ViewBag.CurrentRoles = await _userManager.GetRolesAsync(existingUser);
                return View(user);
            }

            if (ModelState.IsValid)
            {
                // Cập nhật thông tin người dùng
                existingUser.FullName = user.FullName;
                existingUser.UserName = user.UserName;
                existingUser.Email = user.Email;
                existingUser.PhoneNumber = user.PhoneNumber;
                existingUser.Token = user.Token; // Cập nhật Token

                // Nếu có mật khẩu mới, cập nhật mật khẩu
                if (!string.IsNullOrEmpty(password))
                {
                    var token = await _userManager.GeneratePasswordResetTokenAsync(existingUser);
                    var result = await _userManager.ResetPasswordAsync(existingUser, token, password);
                    if (!result.Succeeded)
                    {
                        foreach (var error in result.Errors)
                        {
                            ModelState.AddModelError("", error.Description);
                        }
                        ViewBag.AllRoles = new SelectList(await _roleManager.Roles.ToListAsync(), "Name", "Name");
                        ViewBag.CurrentRoles = await _userManager.GetRolesAsync(existingUser);
                        return View(user);
                    }
                }

                // Cập nhật thông tin người dùng
                var updateResult = await _userManager.UpdateAsync(existingUser);
                if (updateResult.Succeeded)
                {
                    // Lấy danh sách vai trò hiện tại
                    var currentRoles = await _userManager.GetRolesAsync(existingUser);

                    // Xóa vai trò cũ nếu không còn được chọn
                    var rolesToRemove = currentRoles.Except(selectedRoles ?? new string[] { });
                    if (rolesToRemove.Any())
                    {
                        await _userManager.RemoveFromRolesAsync(existingUser, rolesToRemove);
                    }

                    // Thêm vai trò mới nếu được chọn
                    var rolesToAdd = selectedRoles?.Except(currentRoles) ?? new string[] { };
                    foreach (var roleName in rolesToAdd)
                    {
                        if (await _roleManager.RoleExistsAsync(roleName))
                        {
                            await _userManager.AddToRoleAsync(existingUser, roleName);
                        }
                        else
                        {
                            ModelState.AddModelError("", $"Vai trò {roleName} không tồn tại.");
                        }
                    }

                    TempData["success"] = "Cập nhật user thành công";
                    return RedirectToAction("Index");
                }

                foreach (var error in updateResult.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }
            }

            // Nếu có lỗi, nạp lại dữ liệu vai trò
            ViewBag.AllRoles = new SelectList(await _roleManager.Roles.ToListAsync(), "Name", "Name");
            ViewBag.CurrentRoles = await _userManager.GetRolesAsync(existingUser);
            return View(user);
        }

        // GET: Hiển thị form xác nhận xóa
        [HttpGet]
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            // Kiểm tra nếu người dùng đang cố xóa chính họ
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser.Id == user.Id)
            {
                TempData["error"] = "Bạn không thể xóa chính tài khoản của mình.";
                return RedirectToAction("Index");
            }

            // Kiểm tra nếu tài khoản là Admin
            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains("Admin"))
            {
                TempData["error"] = "Không thể xóa tài khoản Admin.";
                return RedirectToAction("Index");
            }

            return View(user);
        }

        // POST: Xử lý xóa người dùng
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var deleteResult = await _userManager.DeleteAsync(user);
            if (!deleteResult.Succeeded)
            {
                TempData["error"] = "Có lỗi xảy ra khi xóa user.";
                return View("Error");
            }

            TempData["success"] = "User đã được xóa thành công";
            return RedirectToAction("Index");
        }
    }
}