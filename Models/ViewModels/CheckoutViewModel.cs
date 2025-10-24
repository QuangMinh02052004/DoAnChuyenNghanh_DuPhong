using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Bloomie.Models.Entities;

namespace Bloomie.ViewModels
{
    public class CheckoutViewModel
    {
        public decimal TotalPrice { get; set; }
        public List<CartItem> CartItems { get; set; } = new List<CartItem>();

        [Required(ErrorMessage = "Vui lòng nhập địa chỉ giao hàng")]
        [MaxLength(500, ErrorMessage = "Địa chỉ không được vượt quá 500 ký tự")]
        public string ShippingAddress { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn phương thức giao hàng")]
        public string ShippingMethod { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn phương thức thanh toán")]
        public string PaymentMethod { get; set; }

        public decimal ShippingCost { get; set; }
        public string Notes { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập tên người gửi")]
        [MaxLength(100, ErrorMessage = "Tên không được vượt quá 100 ký tự")]
        public string SenderName { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập email người gửi")]
        [EmailAddress(ErrorMessage = "Vui lòng nhập email hợp lệ")]
        [MaxLength(255, ErrorMessage = "Email không được vượt quá 255 ký tự")]
        public string SenderEmail { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập số điện thoại người gửi")]
        [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
        [MaxLength(11, ErrorMessage = "Số điện thoại không được vượt quá 11 số")]
        public string SenderPhoneNumber { get; set; }

        [RequiredUnlessSenderReceiverSame(ErrorMessage = "Vui lòng nhập tên người nhận")]
        [MaxLength(100, ErrorMessage = "Tên không được vượt quá 100 ký tự")]
        public string ReceiverName { get; set; }

        [RequiredUnlessSenderReceiverSame(ErrorMessage = "Vui lòng nhập số điện thoại người nhận")]
        [Phone(ErrorMessage = "Số điện thoại không hợp lệ")]
        [MaxLength(11, ErrorMessage = "Số điện thoại không được vượt quá 11 số")]
        public string ReceiverPhoneNumber { get; set; }

        [EmailAddress(ErrorMessage = "Vui lòng nhập email hợp lệ")]
        [MaxLength(255, ErrorMessage = "Email không được vượt quá 255 ký tự")]
        public string ReceiverEmail { get; set; }

        public bool IsSenderReceiverSame { get; set; }
        public bool IsAnonymousSender { get; set; }
    }

    // Custom validation attribute để kiểm tra khi IsSenderReceiverSame = false
    public class RequiredUnlessSenderReceiverSameAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            var model = (CheckoutViewModel)validationContext.ObjectInstance;
            if (!model.IsSenderReceiverSame && string.IsNullOrWhiteSpace(value?.ToString()))
            {
                return new ValidationResult(ErrorMessage);
            }
            return ValidationResult.Success;
        }
    }
}