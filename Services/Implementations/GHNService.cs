using Bloomie.Models.GHN;
using Bloomie.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace Bloomie.Services.Implementations
{
    public class GHNService : IGHNService
    {
        private readonly HttpClient _httpClient;
        private readonly string _shopId;

        public GHNService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClient = httpClientFactory.CreateClient("GHN");
            _shopId = configuration["GHN:ShopID"];
        }

        public async Task<List<Province>> GetProvincesAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("master-data/province");
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();

                // Log dữ liệu raw từ API
                Console.WriteLine("Dữ liệu tỉnh/thành từ API GHN (raw): " + json);

                // Chuẩn hóa JSON: thay thế các ký tự đặc biệt
                json = json.Replace("Hà N?i", "Hà Nội")
                           .Replace("H? Chí Minh", "Hồ Chí Minh")
                           .Replace("Đ?ng Nai", "Đồng Nai")
                           .Replace("Đ?ng Tháp", "Đồng Tháp")
                           .Replace("H?i Ph?ng", "Hải Phòng")
                           .Replace("H?i Dương", "Hải Dương")
                           .Replace("Th?a Thiên Hu?", "Thừa Thiên Huế")
                           .Replace("Th?a Thiên - Hu?", "Thừa Thiên Huế")
                           .Replace("Bà R?a - V?ng Tàu", "Bà Rịa - Vũng Tàu")
                           .Replace("?", "ộ")
                           .Replace("?", "ệ")
                           .Replace("?", "ă");

                // Phân tích cú pháp JSON sau khi chuẩn hóa
                var result = JsonConvert.DeserializeObject<GHNResponse<List<Province>>>(json);

                if (result?.Data == null || !result.Data.Any())
                {
                    Console.WriteLine("Dữ liệu từ API GHN rỗng hoặc không hợp lệ.");
                    return new List<Province>();
                }

                // Lọc và chuẩn hóa dữ liệu
                var filteredProvinces = result.Data
                    .Where(p => !p.ProvinceName.Contains("Test", StringComparison.OrdinalIgnoreCase) && // Loại bỏ bản ghi thử nghiệm
                                !p.ProvinceName.Contains("Hà Nội 02")) // Loại bỏ "Hà Nội 02"
                    .Select(p => new Province
                    {
                        ProvinceID = p.ProvinceID,
                        ProvinceName = NormalizeProvinceName(p.ProvinceName),
                        CountryID = p.CountryID,
                        Code = p.Code,
                        NameExtension = p.NameExtension?.Select(n => NormalizeProvinceName(n)).ToList(),
                        IsEnable = p.IsEnable,
                        RegionID = p.RegionID,
                        RegionCPN = p.RegionCPN,
                        UpdatedBy = p.UpdatedBy,
                        CreatedAt = p.CreatedAt,
                        UpdatedAt = p.UpdatedAt,
                        CanUpdateCOD = p.CanUpdateCOD,
                        Status = p.Status
                    })
                    .OrderBy(p => p.ProvinceName)
                    .ToList();

                Console.WriteLine("Danh sách tỉnh/thành trả về từ GHNService: " + JsonConvert.SerializeObject(filteredProvinces));
                return filteredProvinces;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi khi lấy danh sách tỉnh/thành từ GHN: " + ex.Message);
                return new List<Province>();
            }
        }

        private string NormalizeProvinceName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            name = name.Replace("Tỉnh ", "").Replace("Thành phố ", "").Trim();
            return name;
        }

        public async Task<List<District>> GetDistrictsAsync(int provinceId)
        {
            var response = await _httpClient.GetAsync($"master-data/district?province_id={provinceId}");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<GHNResponse<List<District>>>(json);
            return result.Data;
        }

        public async Task<List<Ward>> GetWardsAsync(int districtId)
        {
            var response = await _httpClient.GetAsync($"master-data/ward?district_id={districtId}");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<GHNResponse<List<Ward>>>(json);
            return result.Data;
        }

        public async Task<decimal> CalculateShippingFeeAsync(ShippingFeeRequest request)
        {
            Console.WriteLine($"Request to GHN: shop_id={_shopId}, from_district_id={request.FromDistrictId}, to_district_id={request.ToDistrictId}, to_ward_code={request.ToWardCode}, service_id={request.ServiceId}, weight={request.Weight}");
            var payload = new
            {
                shop_id = int.Parse(_shopId),
                from_district_id = request.FromDistrictId,
                to_district_id = request.ToDistrictId,
                to_ward_code = request.ToWardCode,
                service_id = request.ServiceId,
                weight = request.Weight,
                length = request.Length,
                width = request.Width,
                height = request.Height,
                insurance_value = request.InsuranceValue
            };

            var content = new StringContent(JsonConvert.SerializeObject(payload), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("v2/shipping-order/fee", content);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"GHN API Error: Status {response.StatusCode}, Content: {errorContent}");
                throw new Exception($"GHN API Error: {errorContent}");
            }
            var json = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"GHN API Response: {json}");
            var result = JsonConvert.DeserializeObject<GHNResponse<ShippingFeeResponse>>(json);
            return result.Data.Total;
        }
    }

    public class GHNResponse<T>
    {
        public int Code { get; set; }
        public string Message { get; set; }
        public T Data { get; set; }
    }
}