using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Bloomie.Data;
using Bloomie.Services.Implementations;
using Bloomie.Services.Interfaces;
using Bloomie.Models.Entities;
using Bloomie.Areas.Admin.Models;
using Bloomie.Middleware;
using Bloomie.Models.Momo;
using OfficeOpenXml;
using Python.Runtime;
using Bloomie.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR(); // Thư viện cho phép giao tiếp thời gian thực

// Connect MomoAPI
builder.Services.Configure<MomoOptionModel>(builder.Configuration.GetSection("MomoAPI"));
builder.Services.AddScoped<IMomoService, MomoService>();

// Cấu hình logging
builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole(); // Ghi log ra console
    logging.AddDebug();   // Ghi log ra debug output (Visual Studio)
});

// Cấu hình Email Service
builder.Services.AddTransient<IEmailService, EmailService>();

// Cấu hình Session và Cache
builder.Services.AddDistributedMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Cấu hình Controllers và Views
builder.Services.AddControllersWithViews();

//builder.Services.AddControllers()
//    .AddApplicationPart(typeof(Bloomie.Areas.Admin.Controllers.NotificationsController).Assembly);

// Cấu hình Database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();

builder.Services.Configure<IdentityOptions>(options =>
{
    // Password settings.
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequiredLength = 6;
    options.Password.RequiredUniqueChars = 1;

    // Lockout settings.
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(60);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    // User settings.
    options.User.AllowedUserNameCharacters =
    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
    options.User.RequireUniqueEmail = true;
});

builder.Services.ConfigureApplicationCookie(options =>
{
    // Cookie settings
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(60);

    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.SlidingExpiration = true;
});

// Cấu hình xác thực qua Google, Facebook, Twitter
builder.Services.AddAuthentication(options =>
{
    //options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    //options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    //options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
}).AddCookie().AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
{
    options.ClientId = builder.Configuration.GetSection("GoogleKeys:ClientId").Value;
    options.ClientSecret = builder.Configuration.GetSection("GoogleKeys:ClientSecret").Value;
}).AddFacebook(facebookOptions =>
{
    facebookOptions.AppId = builder.Configuration.GetSection("FacebookKeys:AppId").Value;
    facebookOptions.AppSecret = builder.Configuration.GetSection("FacebookKeys:AppSecret").Value;
    facebookOptions.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;

    // Xử lý lỗi ngay trong middleware
    facebookOptions.Events.OnRemoteFailure = context =>
    {
        context.Response.Redirect("/Account/Login?info=" + Uri.EscapeDataString("B?n ?ă h?y ??ng nh?p b?ng Facebook."));
        context.HandleResponse(); // Ngăn middleware tiếp tục xử lý
        return Task.CompletedTask;
    };
}).AddTwitter(twitterOptions =>
{
    twitterOptions.ConsumerKey = builder.Configuration.GetSection("TwitterKeys:ClientId").Value;
    twitterOptions.ConsumerSecret = builder.Configuration.GetSection("TwitterKeys:ClientSecret").Value;
}); ;

// Câus hình Data Protection
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(@"C:\DataProtectionKeys")) 
    .SetApplicationName("BloomieApp");

// Cấu hình GHN API
builder.Services.AddHttpClient("GHN", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["GHN:BaseUrl"]);
    client.DefaultRequestHeaders.Add("Token", builder.Configuration["GHN:ApiToken"]);
});
builder.Services.AddScoped<IGHNService, GHNService>();

builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IPromotionService, PromotionService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();

// Cấu hình Excel (EPPlus)
ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

// Connect VNPay API
builder.Services.AddScoped<IVnPayService, VnPayService>();

var app = builder.Build();

// Tạo roles và admin account
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    // Tạo roles
    string[] roles = new[] { "Admin", "User", "Manager", "Staff" };
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole { Name = role, NormalizedName = role.ToUpper() });
        }
    }

    // Tạo admin account
    string adminEmail = "admin@bloomie.com";
    string adminPassword = "Admin@123";
    string adminUserName = "admin";
    string adminFullName = "Administrator";

    var adminUser = await userManager.FindByEmailAsync(adminEmail);
    if (adminUser == null)
    {
        adminUser = new ApplicationUser
        {
            UserName = adminUserName,
            Email = adminEmail,
            FullName = adminFullName,
            RoleId = (await roleManager.FindByNameAsync("Admin"))?.Id, // Gán RoleId của Admin
            Token = Guid.NewGuid().ToString() // Gán giá trị Token ngẫu nhiên
        };
        var result = await userManager.CreateAsync(adminUser, adminPassword);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(adminUser, "Admin");
        }
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseSession();

// Đăng ký middleware để ghi log truy cập người dùng
app.UseUserAccessLogging();

app.UseEndpoints(endpoints =>
{
    endpoints.MapHub<NotificationHub>("/notificationHub");
    endpoints.MapControllerRoute(
        name: "areas",
        pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
    endpoints.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");
    //endpoints.MapControllers();
});

//app.MapAreaControllerRoute(
//    name: "admin",
//    areaName: "Admin",
//    pattern: "Admin/{controller=Home}/{action=Index}/{id?}");
//app.MapControllers();

//app.MapRazorPages();
//app.MapControllerRoute(
//    name: "default",
//    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
