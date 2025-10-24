using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.ViewModels
{
    public class UserViewModel
    {
        public int Id { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập Username")]
        public string Username { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập Email"), EmailAddress]
        public string Email { get; set; }
        [Required(ErrorMessage = "Mật khẩu là bắt buộc")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu phải dài ít nhất 6 ký tự")]
        [DataType(DataType.Password)]
        public string Password { get; set; }
        [Required(ErrorMessage = "Xác nhận mật khẩu là bắt buộc.")]
        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "Mật khẩu xác nhận không khớp với mật khẩu.")]
        public string ConfirmPassword { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập Họ và Tên")]
        public string FullName { get; set; }
    }
}
