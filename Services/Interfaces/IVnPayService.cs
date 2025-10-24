using Bloomie.Models.Vnpay;

namespace Bloomie.Services.Interfaces
{
    public interface IVnPayService
    {
        string CreatePaymentVnpay(PaymentInformationModel model, HttpContext context);
        PaymentResponseModel PaymentExecute(IQueryCollection collections);
    }
}
