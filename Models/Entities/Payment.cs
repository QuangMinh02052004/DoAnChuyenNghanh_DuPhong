namespace Bloomie.Models.Entities
{
    //public enum PaymentStatus
    //{
    //    Pending, 
    //    Paid, 
    //    Failed, 
    //    Cancelled, 
    //    Refunded 
    //}

    public class Payment
    {
        public int Id { get; set; }
        public string OrderId { get; set; }
        public Order Order { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } 
        public string PaymentStatus { get; set; } 
        public DateTime PaymentDate { get; set; }

        public string PaymentMethodDisplay
        {
            get
            {
                return PaymentMethod switch
                {
                    "Momo" => "Thanh toán qua MoMo",
                    "Vnpay" => "Thanh toán qua Vnpay",
                    "CashOnDelivery" => "Thanh toán khi nhận hàng",
                    _ => PaymentMethod ?? "Không xác định"
                };
            }
        }

        public string PaymentStatusDisplay
        {
            get
            {
                return PaymentStatus switch
                {
                    "Pending" => "Đang chờ xử lý",
                    "Paid" => "Đã thanh toán",
                    "Failed" => "Thất bại",
                    "Cancelled" => "Đã hủy",
                    "Refunded" => "Đã hoàn tiền",
                    _ => PaymentStatus ?? "Không xác định"
                };
            }
        }
    }
}
