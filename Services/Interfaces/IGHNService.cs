using Bloomie.Models.GHN;

namespace Bloomie.Services.Interfaces
{
    public interface IGHNService
    {
        Task<List<Province>> GetProvincesAsync();
        Task<List<District>> GetDistrictsAsync(int provinceId);
        Task<List<Ward>> GetWardsAsync(int districtId);
        Task<decimal> CalculateShippingFeeAsync(ShippingFeeRequest request);
    }
}
