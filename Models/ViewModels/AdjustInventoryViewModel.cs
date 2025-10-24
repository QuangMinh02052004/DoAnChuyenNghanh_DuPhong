using Bloomie.Models.Entities;
using System.ComponentModel.DataAnnotations;

namespace Bloomie.Areas.Admin.Models
{
    public class AdjustInventoryViewModel
    {
        public int FlowerTypeId { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tên sản phẩm.")]
        public string ProductName { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập số lượng giảm.")]
        [Range(1, int.MaxValue, ErrorMessage = "Số lượng giảm phải lớn hơn 0.")]
        public int Quantity { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn loại điều chỉnh.")]
        public AdjustmentType AdjustmentType { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập lý do.")]
        public string Reason { get; set; }
        [Required(ErrorMessage = "Vui lòng chọn lô hàng.")]
        public int? BatchId { get; set; } // Thêm BatchId
    }
}