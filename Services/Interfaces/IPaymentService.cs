using Bloomie.Models.Entities;
using System.Threading.Tasks;

namespace Bloomie.Services.Interfaces
{
    public interface IPaymentService
    {
        Task<Payment> CreatePaymentAsync(string orderId, string paymentMethod, decimal amount);
        Task ProcessPaymentAsync(int paymentId, string paymentStatus, string transactionId);
    }
}