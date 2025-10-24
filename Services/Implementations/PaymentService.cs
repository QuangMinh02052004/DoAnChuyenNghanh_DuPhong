using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace Bloomie.Services.Implementations
{
    public class PaymentService : IPaymentService
    {
        private readonly ApplicationDbContext _context;

        public PaymentService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Payment> CreatePaymentAsync(string orderId, string paymentMethod, decimal amount)
        {
            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null) throw new Exception("Đơn hàng không tồn tại");

            var payment = new Payment
            {
                OrderId = orderId,
                Order = order,
                Amount = amount,
                PaymentMethod = paymentMethod,
                PaymentStatus = "Đang chờ xử lý", // Trạng thái ban đầu bằng tiếng Việt
                PaymentDate = DateTime.Now
            };

            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();
            return payment;
        }

        public async Task ProcessPaymentAsync(int paymentId, string paymentStatus, string transactionId)
        {
            var payment = await _context.Payments
                .Include(p => p.Order)
                .FirstOrDefaultAsync(p => p.Id == paymentId);
            if (payment == null) throw new Exception("Thanh toán không tồn tại");

            switch (paymentStatus.ToLower())
            {
                case "paid":
                    payment.PaymentStatus = "Đã thanh toán";
                    break;
                case "failed":
                    payment.PaymentStatus = "Thất bại";
                    break;
                case "cancelled":
                    payment.PaymentStatus = "Đã hủy";
                    break;
                case "refunded":
                    payment.PaymentStatus = "Đã hoàn tiền";
                    break;
                default:
                    payment.PaymentStatus = "Đang chờ xử lý";
                    break;
            }

            // Có thể lưu thêm transactionId nếu cổng thanh toán trả về
            await _context.SaveChangesAsync();

            // Nếu thanh toán thành công, cập nhật trạng thái đơn hàng
            if (payment.PaymentStatus == "Đã thanh toán" && payment.PaymentMethod != "CashOnDelivery")
            {
                var orderService = ResolveOrderService();
                await orderService.UpdateOrderStatusAsync(
                    payment.OrderId,
                    OrderStatus.Processing,
                    "System",
                    "Thanh toán thành công"
                );
            }
        }

        // Phương thức này cần được thay thế bằng cơ chế Dependency Injection thực tế
        private IOrderService ResolveOrderService()
        {
            var context = _context; // Nên inject qua constructor
            var inventoryService = new InventoryService(context); // Nên inject qua constructor
            return new OrderService(context, inventoryService);
        }
    }
}