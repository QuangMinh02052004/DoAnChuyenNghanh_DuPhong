using System.Security.Claims;
using System.Threading.Tasks;
using Bloomie.Data;
using Bloomie.Services.Interfaces;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Pages.Manage;
using Bloomie.Models.ViewModels;

namespace Bloomie.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;

        public AccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, ApplicationDbContext context, IEmailService emailService)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
            _emailService = emailService;
        }

        public IActionResult Login(string returnUrl = null)
        {
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel loginVM)
        {
            if (ModelState.IsValid)
            {
                var result = await _signInManager.PasswordSignInAsync(loginVM.Username, loginVM.Password, false, true);
                if (result.Succeeded)
                {
                    // Tìm người dùng theo tên đăng nhập
                    var user = await _userManager.FindByNameAsync(loginVM.Username);
                    if (await _userManager.IsInRoleAsync(user, "Admin"))
                    {
                        return Redirect(loginVM.ReturnUrl ?? "/Admin/AdminDashboard");
                    }
                    else if (await _userManager.IsInRoleAsync(user, "Staff"))
                    {
                        return Redirect(loginVM.ReturnUrl ?? "/Staff/StaffDashboard");
                    }
                    return Redirect(loginVM.ReturnUrl ?? "/Home/Index");
                }
                if (result.IsLockedOut)
                {
                    ModelState.AddModelError("", "Tài khoản đã bị khóa tạm thời. Vui lòng thử lại sau 5 phút.");
                }
                else
                {
                    ModelState.AddModelError("", "Tên đăng nhập hoặc mật khẩu không đúng.");
                }
            }
            else
            {
                Console.WriteLine("ModelState không hợp lệ: " + string.Join(", ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
            }
            return View(loginVM);
        }

        // Khởi tạo đăng nhập bằng Google
        public async Task LoginByGoogle()
        {
            await HttpContext.ChallengeAsync(GoogleDefaults.AuthenticationScheme,
                new AuthenticationProperties
                {
                    RedirectUri = Url.Action("GoogleResponse")
                });
        }

        public async Task<IActionResult> GoogleResponse()
        {
            // Xác thực kết quả từ Google
            var result = await HttpContext.AuthenticateAsync(GoogleDefaults.AuthenticationScheme);
            if (!result.Succeeded)
            {
                return RedirectToAction("Login");
            }

            // Lấy claims
            var claims = result.Principal?.Identities.FirstOrDefault()?.Claims.Select(claim => new
            {
                claim.Issuer, // Ai phát hành claim
                claim.OriginalIssuer, // Nơi claim đc tạo
                claim.Type, 
                claim.Value
            });

            // Lấy email từ claims
            var email = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
            string emailName = email.Split('@')[0];

            // Kiểm tra người dùng đã tồn tại
            var existingUser = await _userManager.FindByEmailAsync(email);

            if (existingUser == null)
            {
                var passwordHasher = new PasswordHasher<ApplicationUser>();
                var hashedPassword = passwordHasher.HashPassword(null, "123456789");

                // Đảm bảo vai trò "User" tồn tại
                if (!await _roleManager.RoleExistsAsync("User"))
                {
                    await _roleManager.CreateAsync(new IdentityRole { Name = "User", NormalizedName = "USER" });
                }

                // Lấy vai trò "User"
                var userRole = await _roleManager.FindByNameAsync("User");

                string token = Guid.NewGuid().ToString();

                var newUser = new ApplicationUser
                {
                    UserName = emailName,
                    Email = email,
                    FullName = emailName,
                    RoleId = userRole?.Id, 
                    Token = token 
                };
                newUser.PasswordHash = hashedPassword;

                // Tạo người dùng trong cơ sở dữ liệu
                var createUserResult = await _userManager.CreateAsync(newUser);
                if (!createUserResult.Succeeded)
                {
                    TempData["error"] = "Đăng ký tài khoản thất bại. Vui lòng thử lại sau: " + string.Join(", ", createUserResult.Errors.Select(e => e.Description));
                    return RedirectToAction("Login", "Account");
                }

                // Gán vai trò User
                var roleResult = await _userManager.AddToRoleAsync(newUser, "User");
                if (!roleResult.Succeeded)
                {
                    TempData["error"] = "Gán vai trò thất bại. Vui lòng thử lại sau: " + string.Join(", ", roleResult.Errors.Select(e => e.Description));
                    return RedirectToAction("Login", "Account");
                }

                // Đăng nhập tự động cho người dùng mới
                await _signInManager.SignInAsync(newUser, isPersistent: false);
                TempData["success"] = "Đăng ký tài khoản thành công.";
                return RedirectToAction("Index", "Home");
            }
            else
            {
                await _signInManager.SignInAsync(existingUser, isPersistent: false);
                TempData["success"] = "Đăng nhập thành công.";
                return RedirectToAction("Index", "Home");
            }
        }

        public async Task LoginByFacebook()
        {
            await HttpContext.ChallengeAsync("Facebook",
                new AuthenticationProperties
                {
                    RedirectUri = Url.Action("FacebookResponse")
                });
        }

        public async Task<IActionResult> FacebookResponse()
        {
            var info = HttpContext.Request.Query["info"].FirstOrDefault();
            if (!string.IsNullOrEmpty(info))
            {
                TempData["info"] = info;
                return RedirectToAction("Login", "Account");
            }

            try
            {
                var result = await HttpContext.AuthenticateAsync("Facebook");
                if (!result.Succeeded)
                {
                    TempData["error"] = "Đăng nhập bằng Facebook thất bại. Chi tiết: " + (result.Failure?.Message ?? "Không có chi tiết lỗi.");
                    return RedirectToAction("Login", "Account");
                }

                var claims = result.Principal?.Identities.FirstOrDefault()?.Claims;
                var email = claims?.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
                var fullName = claims?.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;

                if (string.IsNullOrEmpty(email))
                {
                    TempData["error"] = "Không thể lấy thông tin email từ Facebook.";
                    return RedirectToAction("Login", "Account");
                }

                string emailName = email.Split('@')[0];
                var existingUser = await _userManager.FindByEmailAsync(email);

                if (existingUser == null)
                {
                    if (!await _roleManager.RoleExistsAsync("User"))
                    {
                        await _roleManager.CreateAsync(new IdentityRole { Name = "User", NormalizedName = "USER" });
                    }

                    var userRole = await _roleManager.FindByNameAsync("User");
                    string token = Guid.NewGuid().ToString();

                    var newUser = new ApplicationUser
                    {
                        UserName = emailName,
                        Email = email,
                        FullName = fullName ?? emailName,
                        RoleId = userRole?.Id,
                        Token = token
                    };

                    var createUserResult = await _userManager.CreateAsync(newUser);
                    if (!createUserResult.Succeeded)
                    {
                        TempData["error"] = "Đăng ký tài khoản thất bại: " + string.Join(", ", createUserResult.Errors.Select(e => e.Description));
                        return RedirectToAction("Login", "Account");
                    }

                    var roleResult = await _userManager.AddToRoleAsync(newUser, "User");
                    if (!roleResult.Succeeded)
                    {
                        TempData["error"] = "Gán vai trò thất bại: " + string.Join(", ", roleResult.Errors.Select(e => e.Description));
                        return RedirectToAction("Login", "Account");
                    }

                    await _emailService.SendEmailAsync(newUser.Email, "Chào mừng đến với Bloomie",
                        $"Chào {newUser.FullName},<br/>Tài khoản của bạn đã được tạo. Vui lòng <a href='{Request.Scheme}://{Request.Host}/Account/SetNewPassword?email={newUser.Email}&token={token}'>nhấp vào đây</a> để đặt mật khẩu mới.");

                    TempData["success"] = "Đăng ký bằng Facebook thành công. Vui lòng đặt mật khẩu mới.";
                    return RedirectToAction("SetNewPassword", new { email = newUser.Email, token = token });
                }
                else
                {
                    await _signInManager.SignInAsync(existingUser, isPersistent: false);
                    TempData["success"] = "Đăng nhập bằng Facebook thành công.";
                    return RedirectToAction("Index", "Home");
                }
            }
            catch (Exception ex)
            {
                TempData["error"] = "Đã xảy ra lỗi khi xử lý đăng nhập Facebook: " + ex.Message;
                return RedirectToAction("Login", "Account");
            }
        }

        public async Task LoginByTwitter()
        {
            await HttpContext.ChallengeAsync("Twitter",
                new AuthenticationProperties
                {
                    RedirectUri = Url.Action("TwitterResponse")
                });
        }

        public async Task<IActionResult> TwitterResponse()
        {
            var result = await HttpContext.AuthenticateAsync("Twitter");
            if (!result.Succeeded)
            {
                TempData["error"] = "Đăng nhập bằng Twitter thất bại. Chi tiết: " + (result.Failure?.Message ?? "Không có chi tiết lỗi.");
                return RedirectToAction("Login", "Account");
            }

            var claims = result.Principal?.Identities.FirstOrDefault()?.Claims;
            var email = claims?.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
            var fullName = claims?.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;

            if (string.IsNullOrEmpty(email))
            {
                email = claims?.FirstOrDefault(c => c.Type == "urn:twitter:username")?.Value + "@twitter.com";
                if (string.IsNullOrEmpty(email))
                {
                    TempData["error"] = "Không thể lấy thông tin email từ Twitter.";
                    return RedirectToAction("Login", "Account");
                }
            }

            string emailName = email.Split('@')[0];
            var existingUser = await _userManager.FindByEmailAsync(email);

            if (existingUser == null)
            {
                if (!await _roleManager.RoleExistsAsync("User"))
                {
                    await _roleManager.CreateAsync(new IdentityRole { Name = "User", NormalizedName = "USER" });
                }

                var userRole = await _roleManager.FindByNameAsync("User");
                string token = Guid.NewGuid().ToString();

                var newUser = new ApplicationUser
                {
                    UserName = emailName,
                    Email = email,
                    FullName = fullName ?? emailName,
                    RoleId = userRole?.Id,
                    Token = token
                };

                var createUserResult = await _userManager.CreateAsync(newUser);
                if (!createUserResult.Succeeded)
                {
                    TempData["error"] = "Đăng ký tài khoản thất bại: " + string.Join(", ", createUserResult.Errors.Select(e => e.Description));
                    return RedirectToAction("Login", "Account");
                }

                var roleResult = await _userManager.AddToRoleAsync(newUser, "User");
                if (!roleResult.Succeeded)
                {
                    TempData["error"] = "Gán vai trò thất bại: " + string.Join(", ", roleResult.Errors.Select(e => e.Description));
                    return RedirectToAction("Login", "Account");
                }

                await _emailService.SendEmailAsync(newUser.Email, "Chào mừng đến với Bloomie",
                    $"Chào {newUser.FullName},<br/>Tài khoản của bạn đã được tạo. Vui lòng <a href='{Request.Scheme}://{Request.Host}/Account/SetNewPassword?email={newUser.Email}&token={token}'>nhấp vào đây</a> để đặt mật khẩu mới.");

                TempData["success"] = "Đăng ký bằng Twitter thành công. Vui lòng đặt mật khẩu mới.";
                return RedirectToAction("SetNewPassword", new { email = newUser.Email, token = token });
            }
            else
            {
                await _signInManager.SignInAsync(existingUser, isPersistent: false);
                TempData["success"] = "Đăng nhập bằng Twitter thành công.";
                return RedirectToAction("Index", "Home");
            }
        }

        public async Task<IActionResult> SetNewPassword(string email, string token)
        {
            // Kiểm tra email và token
            var user = await _userManager.Users
                .FirstOrDefaultAsync(u => u.Email == email && u.Token == token);

            if (user == null)
            {
                TempData["error"] = "Liên kết không hợp lệ.";
                return RedirectToAction("Login");
            }

            ViewBag.Email = email;
            ViewBag.Token = token;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SetNewPassword(ApplicationUser model, string token)
        {
            var user = await _userManager.Users
                .FirstOrDefaultAsync(u => u.Email == model.Email && u.Token == token);

            if (user == null)
            {
                TempData["error"] = "Liên kết không hợp lệ.";
                return RedirectToAction("Login");
            }

            if (ModelState.IsValid)
            {
                var passwordHasher = new PasswordHasher<ApplicationUser>();
                var passwordHash = passwordHasher.HashPassword(user, model.PasswordHash);

                user.PasswordHash = passwordHash;
                user.Token = Guid.NewGuid().ToString();
                await _userManager.UpdateAsync(user);

                await _signInManager.SignInAsync(user, isPersistent: false);
                TempData["success"] = "Mật khẩu đã được đặt thành công. Bạn có thể đăng nhập ngay bây giờ.";
                return RedirectToAction("Index", "Home");
            }

            ViewBag.Email = user.Email;
            ViewBag.Token = token;
            return View(model);
        }

        public IActionResult Register()
        {
            return View(new UserViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> Register(UserViewModel user)
        {
            if (ModelState.IsValid)
            {
                // Kiểm tra xem tên người dùng đã tồn tại chưa
                var existingUser = await _userManager.FindByNameAsync(user.Username);
                if (existingUser != null)
                {
                    ModelState.AddModelError("Username", "Tên đăng nhập đã tồn tại. Vui lòng chọn tên đăng nhập khác.");
                    return View(user);
                }

                // Đảm bảo vai trò "User" tồn tại
                if (!await _roleManager.RoleExistsAsync("User"))
                {
                    await _roleManager.CreateAsync(new IdentityRole { Name = "User", NormalizedName = "USER" });
                }

                var userRole = await _roleManager.FindByNameAsync("User");

                var newUser = new ApplicationUser
                {
                    FullName = user.FullName,
                    UserName = user.Username,
                    Email = user.Email,
                    RoleId = userRole?.Id,
                    Token = string.Empty
                };

                var result = await _userManager.CreateAsync(newUser, user.Password);
                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(newUser, "User");
                    TempData["success"] = "Tạo user thành công";
                    return RedirectToAction("Login");
                }
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }
            }
            return View(user);
        }

        public async Task<IActionResult> Logout(string returnUrl = "/")
        {
            // Xóa tất cả cookie xác thực
            await HttpContext.SignOutAsync();
            await _signInManager.SignOutAsync();
            return Redirect(returnUrl);
        }

        public async Task<IActionResult> NewPassword(string email, string token)
        {
            // Kiểm tra email và token
            var checkuser = await _userManager.Users
                .Where(u => u.Email == email)
                .Where(u => u.Token == token).FirstOrDefaultAsync();

            if (checkuser != null)
            {
                ViewBag.Email = checkuser.Email;
                ViewBag.Token = token;
            }
            else
            {
                TempData["error"] = "Không tìm thấy email hoặc token không đúng";
                return RedirectToAction("ForgotPassword", "Account");
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> UpdateNewPassword(ApplicationUser user, string token)
        {
            var checkuser = await _userManager.Users
                .Where(u => u.Email == user.Email)
                .Where(u => u.Token == user.Token).FirstOrDefaultAsync();

            if (checkuser != null)
            {
                string newtoken = Guid.NewGuid().ToString();
                var passwordHasher = new PasswordHasher<ApplicationUser>();
                var passwordHash = passwordHasher.HashPassword(checkuser, user.PasswordHash);

                checkuser.PasswordHash = passwordHash;
                checkuser.Token = newtoken;

                await _userManager.UpdateAsync(checkuser);
                TempData["success"] = "Mật khẩu cập nhật thành công.";
                return RedirectToAction("Login", "Account");
            }
            else
            {
                TempData["error"] = "Không tìm thấy email hoặc token không đúng";
                return RedirectToAction("ForgotPassword", "Account");
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SendMailForgotPass(ApplicationUser user)
        {
            // Kiểm tra email tồn tại
            var checkMail = await _userManager.Users.FirstOrDefaultAsync(u => u.Email == user.Email);

            if (checkMail == null)
            {
                TempData["error"] = "Email không tồn tại";
                return RedirectToAction("ForgotPassword", "Account");
            }
            else
            {
                string token = Guid.NewGuid().ToString();
                checkMail.Token = token;
                _context.Update(checkMail);
                await _context.SaveChangesAsync();

                var receiver = checkMail.Email;
                var subject = "Đặt lại mật khẩu cho tài khoản Bloomie Flower Shop";

                // Tạo URL để đổi mật khẩu
                var resetLink = $"{Request.Scheme}://{Request.Host}/Account/NewPassword?email={checkMail.Email}&token={token}";

                // Template HTML cho email
                var message = $@"
                <!DOCTYPE html>
                <html lang='vi'>
                <head>
                    <meta charset='UTF-8'>
                    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
                <style>
                    body {{
                        font-family: Arial, sans-serif;
                        background-color: #f4f4f4;
                        margin: 0;
                        padding: 0;
                    }}
                    .container {{
                        max-width: 600px;
                        margin: 20px auto;
                        background-color: #ffffff;
                        border-radius: 10px;
                        box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
                        overflow: hidden;
                    }}
                    .header {{
                        background-color: #FF7043;
                        padding: 20px;
                        text-align: center;
                    }}
                    .header h1 {{
                        color: #ffffff; 
                        margin: 0;
                    }}
                    .header img {{
                        max-width: 150px;
                        height: auto;
                    }}
                    .content {{
                        padding: 30px;
                        text-align: center;
                        color: #333;
                    }}
                    .content h2 {{
                        font-size: 24px;
                        margin-bottom: 20px;
                        color: #2d3436;
                    }}
                    .content p {{
                        font-size: 16px;
                        line-height: 1.6;
                        margin-bottom: 20px;
                    }}
                    .btn {{
                        display: inline-block;
                        padding: 12px 24px;
                        background-color: #FF7043;
                        color: #ffffff !important; 
                        text-decoration: none;
                        font-size: 16px;
                        font-weight: bold;
                        border-radius: 5px;
                        transition: background-color 0.3s ease;
                    }}
                    .btn:hover {{
                        background-color: #E64A19;
                    }}
                    .footer {{
                        background-color: #f8f8f8;
                        padding: 15px;
                        text-align: center;
                        font-size: 14px;
                        color: #777;
                    }}
                    .footer a {{
                        color: #FF7043;
                        text-decoration: none;
                    }}
                    .footer a:hover {{
                        text-decoration: underline;
                    }}
                </style>
                </head>
                <body>
                    <div class='container'>
                    <!-- Header -->
                    <div class='header'>
                        <h1>Bloomie Shop</h1>
                    </div>
                    <!-- Content -->
                    <div class='content'>
                        <h2>Đặt lại mật khẩu của bạn</h2>
                        <p>Xin chào {checkMail.FullName ?? "Khách hàng thân mến"},</p>
                        <p>Chúng tôi đã nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn tại Bloomie Flower Shop. Vui lòng nhấn vào nút bên dưới để tiến hành đổi mật khẩu:</p>
                        <a href='{resetLink}' class='btn'>Đổi mật khẩu</a>
                        <p>Nếu bạn không yêu cầu đặt lại mật khẩu, vui lòng bỏ qua email này hoặc liên hệ với chúng tôi qua hotline <strong>0987 654 321</strong>.</p>
                    </div>
                    <!-- Footer -->
                    <div class='footer'>
                        <p>© 2025 Bloomie Flower Shop. Tất cả quyền được bảo lưu.</p>
                        <p>Theo dõi chúng tôi: 
                        <a href='#'>Facebook</a> | 
                        <a href='#'>Instagram</a>
                    </p>
                    <p>Hotline: 0987 654 321 | Email: bloomieshop25@gmail.vn</p>
                    </div>
                    </div>
                </body>
                </html>";

                await _emailService.SendEmailAsync(receiver, subject, message);
            }

            TempData["success"] = "Một email chứa hướng dẫn đặt lại mật khẩu đã được gửi đến địa chỉ email của bạn.";
            return RedirectToAction("ForgotPassword", "Account");
        }

        public async Task<IActionResult> ForgotPassword()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> UpdateAccount()
        {
            // Kiểm tra người dùng đã đăng nhập chưa
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return RedirectToAction("Login");
            }

            // Lấy thông tin người dùng từ Claims
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return NotFound();
            }

            // Đảm bảo Token không null
            if (string.IsNullOrEmpty(user.Token))
            {
                user.Token = Guid.NewGuid().ToString();
                await _userManager.UpdateAsync(user);
            }

            return View(user);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateAccount(ApplicationUser model, string NewPassword, string ConfirmNewPassword)
        {
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return RedirectToAction("Login");
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                TempData["error"] = "Không tìm thấy người dùng.";
                return View(model);
            }

            try
            {
                // Xóa lỗi validation cho NewPassword và ConfirmNewPassword trước khi kiểm tra
                ModelState.Remove("NewPassword");
                ModelState.Remove("ConfirmNewPassword");

                // Kiểm tra ModelState (chỉ áp dụng cho các trường trong model)
                if (!ModelState.IsValid)
                {
                    TempData["error"] = "Dữ liệu không hợp lệ. Vui lòng kiểm tra lại.";
                    return View(model);
                }

                // Cập nhật tên người dùng nếu có thay đổi
                if (!string.IsNullOrEmpty(model.UserName) && model.UserName != user.UserName)
                {
                    var setUserNameResult = await _userManager.SetUserNameAsync(user, model.UserName);
                    if (!setUserNameResult.Succeeded)
                    {
                        foreach (var error in setUserNameResult.Errors)
                        {
                            ModelState.AddModelError("", error.Description);
                        }
                        TempData["error"] = "Cập nhật tên người dùng thất bại.";
                        return View(model);
                    }
                }

                // Cập nhật số điện thoại và họ tên
                if (!string.IsNullOrEmpty(model.PhoneNumber))
                {
                    user.PhoneNumber = model.PhoneNumber;
                }
                if (!string.IsNullOrEmpty(model.FullName))
                {
                    user.FullName = model.FullName;
                }

                // Kiểm tra và cập nhật mật khẩu nếu NewPassword không rỗng
                if (!string.IsNullOrEmpty(NewPassword) || !string.IsNullOrEmpty(ConfirmNewPassword))
                {
                    // Nếu một trong hai trường có giá trị, cả hai phải có giá trị và khớp nhau
                    if (string.IsNullOrEmpty(NewPassword) || string.IsNullOrEmpty(ConfirmNewPassword))
                    {
                        ModelState.AddModelError("", "Vui lòng nhập cả mật khẩu mới và xác nhận mật khẩu.");
                        TempData["error"] = "Vui lòng nhập cả mật khẩu mới và xác nhận mật khẩu.";
                        return View(model);
                    }

                    if (NewPassword != ConfirmNewPassword)
                    {
                        ModelState.AddModelError("", "Mật khẩu mới và xác nhận mật khẩu không khớp.");
                        TempData["error"] = "Mật khẩu mới và xác nhận mật khẩu không khớp.";
                        return View(model);
                    }

                    // Reset mật khẩu
                    var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                    var result = await _userManager.ResetPasswordAsync(user, token, NewPassword);
                    if (!result.Succeeded)
                    {
                        foreach (var error in result.Errors)
                        {
                            ModelState.AddModelError("", error.Description);
                        }
                        TempData["error"] = "Cập nhật mật khẩu thất bại: " + string.Join(", ", result.Errors.Select(e => e.Description));
                        return View(model);
                    }
                }

                // Cập nhật thông tin người dùng
                var updateResult = await _userManager.UpdateAsync(user);
                if (updateResult.Succeeded)
                {
                    TempData["success"] = "Cập nhật thông tin tài khoản thành công.";
                    return RedirectToAction("UpdateAccount");
                }
                else
                {
                    foreach (var error in updateResult.Errors)
                    {
                        ModelState.AddModelError("", error.Description);
                    }
                    TempData["error"] = "Cập nhật thông tin tài khoản thất bại: " + string.Join(", ", updateResult.Errors.Select(e => e.Description));
                    return View(model);
                }
            }
            catch (Exception ex)
            {
                TempData["error"] = $"Lỗi hệ thống: {ex.Message}";
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return RedirectToAction("Login");
            }

            // Lấy thông tin người dùng từ Claims
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                TempData["error"] = "Không tìm thấy người dùng.";
                return RedirectToAction("Login");
            }

            var roles = await _userManager.GetRolesAsync(user);
            ViewBag.Roles = roles.FirstOrDefault();
            return View(user);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateProfileImage(IFormFile ProfileImage)
        {
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return RedirectToAction("Login");
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                return NotFound();
            }

            // Kiểm tra xem người dùng có chọn ảnh không
            if (ProfileImage != null && ProfileImage.Length > 0)
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/profile");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(ProfileImage.FileName);
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await ProfileImage.CopyToAsync(stream);
                }

                user.ProfileImageUrl = $"/profile/{fileName}";
                await _userManager.UpdateAsync(user);
                TempData["success"] = "Cập nhật hình ảnh đại diện thành công.";
            }
            else
            {
                TempData["info"] = "Bạn đã chọn không upload ảnh. Ảnh mặc định sẽ được sử dụng.";
            }

            return RedirectToAction("Profile");
        }
    }
}