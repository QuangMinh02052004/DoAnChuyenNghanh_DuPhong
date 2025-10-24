using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace Bloomie.Areas.Admin.Models
{
    public class UserManagementViewModel
    {
        public string Id { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        public string FullName { get; set; }

        public string? PhoneNumber { get; set; }

        public bool IsActive { get; set; }

        public string? Role { get; set; }

        public IList<string> Roles { get; set; } = new List<string>();

        [ValidateNever]
        public IEnumerable<SelectListItem> RoleList { get; set; }
    }
}