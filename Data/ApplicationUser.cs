using Bloomie.Models.Entities;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace Bloomie.Data
{
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; }
        public string RoleId { get; set; } // ID của vai trò (Role) người dùng
        public string Token { get; set; } // Token để xác thực người dùng
        public string? ProfileImageUrl { get; set; }
    }
}
