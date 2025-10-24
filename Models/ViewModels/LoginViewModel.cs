using System.ComponentModel.DataAnnotations;

namespace Bloomie.Models.ViewModels
{
    public class LoginViewModel
    {
        public int Id { get; set; }
        [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập")]
        public string Username { get; set; }
        [DataType(DataType.Password), Required(ErrorMessage = "Vui lòng nhập mật khẩu")]
        public string Password { get; set; }
        public string? ReturnUrl { get; set; }
    }
}
