using Bloomie.Models.Entities;
using System.Threading.Tasks;

namespace Bloomie.Services.Interfaces
{
    public interface IOrderService
    {
        Task UpdateOrderStatusAsync(string orderId, OrderStatus newStatus, string changedBy, string note);
        // Các phương thức khác...
    }
}