using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace Bloomie.Models.ViewModels
{
    public class CreateProductViewModel
    {
        public int Id { get; set; }
        [Required(ErrorMessage = "Tên sản phẩm là bắt buộc")]
        [StringLength(100, ErrorMessage = "Tên sản phẩm không được vượt quá 100 ký tự")]
        public string Name { get; set; }
        public string Description { get; set; }

        [Required(ErrorMessage = "Giá sản phẩm là bắt buộc")]
        [Range(0.01, 10000000, ErrorMessage = "Giá phải nằm trong khoảng từ 0.01 đến 10,000,000")]
        public decimal Price { get; set; }

        [Required(ErrorMessage = "Số lượng tồn kho là bắt buộc")]
        [Range(0, int.MaxValue, ErrorMessage = "Số lượng tồn kho phải là số không âm")]
        public int Quantity { get; set; }

        //[Range(1, 1000, ErrorMessage = "Ngưỡng tồn kho thấp phải nằm trong khoảng từ 1 đến 1000")]
        //public int LowStockThreshold { get; set; } = 5;
        public int PresentationStyleId { get; set; }
        public bool IsActive { get; set; } = true;

        public bool IsNew { get; set; }

        //[Range(0, 100, ErrorMessage = "Phần trăm giảm giá phải nằm trong khoảng từ 0 đến 100")]
        //public decimal? DiscountPercentage { get; set; }

        [StringLength(500, ErrorMessage = "URL hình ảnh không được vượt quá 500 ký tự")]
        public string? ImageUrl { get; set; }

        public List<string> AdditionalImageUrls { get; set; } = new List<string>();

        [Required(ErrorMessage = "Danh mục là bắt buộc")]
        [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn một danh mục hợp lệ")]
        public int CategoryId { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn ít nhất một loại hoa")]
        [MinLength(1, ErrorMessage = "Vui lòng chọn ít nhất một loại hoa")]
        public List<FlowerTypeSelection> FlowerTypes { get; set; } = new List<FlowerTypeSelection>();
    }

    public class FlowerTypeSelection
    {
        [Required(ErrorMessage = "Loại hoa là bắt buộc")]
        [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn một loại hoa hợp lệ")]
        public int FlowerTypeId { get; set; }

        [Required(ErrorMessage = "Số lượng hoa là bắt buộc")]
        [Range(1, int.MaxValue, ErrorMessage = "Số lượng hoa phải lớn hơn 0")]
        public int Quantity { get; set; }
    }
}