using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Bloomie.Services.Implementations
{
    public class OrderService : IOrderService
    {
        private readonly ApplicationDbContext _context;
        private readonly IInventoryService _inventoryService;

        public OrderService(ApplicationDbContext context, IInventoryService inventoryService)
        {
            _context = context;
            _inventoryService = inventoryService;
        }

        public async Task UpdateOrderStatusAsync(string orderId, OrderStatus newStatus, string changedBy, string note)
        {
            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .FirstOrDefaultAsync(o => o.Id == orderId);
            if (order == null) throw new Exception("Đơn hàng không tồn tại");

            order.OrderStatus = newStatus;

            var statusHistory = new OrderStatusHistory
            {
                OrderId = orderId,
                Status = newStatus,
                ChangeDate = DateTime.Now,
                ChangedBy = changedBy
            };

            _context.OrderStatusHistories.Add(statusHistory);

            // Nếu trạng thái là Processing hoặc Shipped, xuất kho
            if (newStatus == OrderStatus.Processing || newStatus == OrderStatus.Shipped)
            {
                foreach (var detail in order.OrderDetails)
                {
                    await _inventoryService.ExportInventoryAsync(
                        detail.ProductId,
                        detail.Quantity,
                        $"Xuất kho cho đơn hàng #{orderId}",
                        changedBy,
                        orderId
                    );
                }
            }

            await _context.SaveChangesAsync();
        }

        // Các phương thức khác của OrderService...
    }
}