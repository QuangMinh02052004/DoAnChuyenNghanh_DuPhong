using Bloomie.Models.Entities;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Bloomie.Areas.Admin.Models
{
    public class ImportInventoryViewModel
    {
        //[Required(ErrorMessage = "Vui lòng chọn loại hoa.")]
        public int FlowerTypeId { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập số lượng.")]
        [Range(1, int.MaxValue, ErrorMessage = "Số lượng phải lớn hơn 0.")]
        public int Quantity { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập giá nhập.")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Giá nhập phải lớn hơn 0.")]
        public decimal UnitPrice { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn nhà cung cấp.")]
        public int? SupplierId { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập ngày nhập.")]
        [DataType(DataType.Date)]
        public DateTime ImportDate { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập ngày hết hạn.")]
        [DataType(DataType.Date)]
        public DateTime ExpiryDate { get; set; }

        public string Reason { get; set; }

        public int? BatchId { get; set; } // Nullable để hỗ trợ chọn lô hoặc tạo mới
        public string ProductName { get; set; } // Hiển thị tên loại hoa
        public int? ProductId { get; set; } // Dùng cho điều chỉnh kho
        public AdjustmentType AdjustmentType { get; set; } // Dùng cho điều chỉnh kho
    }
}