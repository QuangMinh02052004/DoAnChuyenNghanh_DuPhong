namespace Bloomie.Models.GHN
{
    public class Province
    {
        public int ProvinceID { get; set; }
        public string ProvinceName { get; set; }
        public int CountryID { get; set; }
        public string Code { get; set; }
        public List<string> NameExtension { get; set; } // Đảm bảo là List<string>
        public int? IsEnable { get; set; }
        public int? RegionID { get; set; }
        public int? RegionCPN { get; set; }
        public int? UpdatedBy { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
        public bool? CanUpdateCOD { get; set; }
        public int? Status { get; set; }
    }

    public class District
    {
        public int DistrictID { get; set; }
        public string DistrictName { get; set; }
    }

    public class Ward
    {
        public string WardCode { get; set; }
        public string WardName { get; set; }
    }

    public class ShippingFeeRequest
    {
        public int FromDistrictId { get; set; } // ID quận của shop
        public int ToDistrictId { get; set; }   // ID quận của khách
        public string ToWardCode { get; set; }  // Mã phường của khách
        public int ServiceId { get; set; }      // ID dịch vụ vận chuyển (cần lấy từ API dịch vụ)
        public int Weight { get; set; }         // Trọng lượng (gram)
        public int Length { get; set; }         // Chiều dài (cm)
        public int Width { get; set; }          // Chiều rộng (cm)
        public int Height { get; set; }         // Chiều cao (cm),
        public int InsuranceValue { get; set; } // Giá trị bảo hiểm (VND)
    }

    public class ShippingFeeResponse
    {
        public int Total { get; set; } // Phí vận chuyển (VND)
    }
}
