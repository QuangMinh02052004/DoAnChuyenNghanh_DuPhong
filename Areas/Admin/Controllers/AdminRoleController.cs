using Bloomie.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Bloomie.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class AdminRoleController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AdminRoleController(ApplicationDbContext context, RoleManager<IdentityRole> roleManager)
        {
            _context = context;
            _roleManager = roleManager;
        }

        public async Task<IActionResult> Index(int pageNumber = 1, string searchString = null)
        {
            int pageSize = 10;

            var rolesQuery = _context.Roles.AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                rolesQuery = rolesQuery.Where(r => r.Name.Contains(searchString));
            }

            rolesQuery = rolesQuery.OrderByDescending(p => p.Id);

            int totalItems = await rolesQuery.CountAsync();

            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            pageNumber = Math.Max(1, Math.Min(pageNumber, totalPages));

            var roles = await rolesQuery
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewData["CurrentPage"] = pageNumber;
            ViewData["TotalPages"] = totalPages;
            ViewData["TotalItems"] = totalItems;
            ViewData["SearchString"] = searchString;
            ViewData["PageSize"] = pageSize;

            return View(roles);
        }

        [HttpGet]
        public IActionResult Add()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Add(IdentityRole model)
        {
            if (!_roleManager.RoleExistsAsync(model.Name).GetAwaiter().GetResult())
            {
                _roleManager.CreateAsync(new IdentityRole(model.Name)).GetAwaiter().GetResult();
            }
            return Redirect("Index");
        }

        [HttpGet]
        public async Task<IActionResult> Update(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }
            var role = await _roleManager.FindByIdAsync(id);
            return View(role);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(string id, IdentityRole model)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                var role = await _roleManager.FindByIdAsync(id);
                if (role == null)
                {
                    return NotFound();
                }
                role.Name = model.Name;

                try
                {
                    await _roleManager.UpdateAsync(role);
                    TempData["success"] = "Role đã cập nhật thành công";
                    return RedirectToAction("Index");
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", "An error occurred while updating the role.");
                }
            }
            return View(model ?? new IdentityRole { Id = id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var role = await _roleManager.FindByIdAsync(id);
            if (role == null)
            {
                return NotFound();
            }

            try
            {
                var userRoles = await _context.UserRoles.Where(ur => ur.RoleId == id).ToListAsync();
                if (userRoles.Any())
                {
                    TempData["error"] = "Không thể xóa vai trò vì đang được gán cho người dùng.";
                    return RedirectToAction(nameof(Index));
                }

                var result = await _roleManager.DeleteAsync(role);
                if (result.Succeeded)
                {
                    TempData["success"] = "Vai trò đã được xóa thành công.";
                }
                else
                {
                    TempData["error"] = string.Join("; ", result.Errors.Select(e => e.Description));
                }
            }
            catch (Exception ex)
            {
                TempData["error"] = $"Lỗi khi xóa vai trò: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

    }
}
