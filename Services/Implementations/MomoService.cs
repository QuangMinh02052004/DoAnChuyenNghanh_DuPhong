using Microsoft.Extensions.Options;
using Bloomie.Services.Interfaces;
using Bloomie.Models.Momo;
using System.Text;
using Newtonsoft.Json;
using RestSharp;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Bloomie.Models;

namespace Bloomie.Services.Implementations
{
    public class MomoService : IMomoService
    {
        private readonly IOptions<MomoOptionModel> _options;

        public MomoService(IOptions<MomoOptionModel> options)
        {
            _options = options;
        }

        public async Task<MomoCreatePaymentResponseModel> CreatePaymentMomo(OrderInfoModel model)
        {
            try
            {
                // Tạo orderId từ timestamp
                //model.OrderId = DateTime.UtcNow.Ticks.ToString();
                model.OrderInformation = "Khách hàng: " + model.FullName + ". Nội dung: " + model.OrderInformation;
                //var requestId = DateTime.UtcNow.Ticks.ToString();
                // Sắp xếp tham số theo thứ tự bảng chữ cái: accessKey, amount, extraData, ipnUrl, orderId, orderInfo, partnerCode, redirectUrl, requestId, requestType
                var rawData =
                    $"accessKey={_options.Value.AccessKey}" +
                    $"&amount={model.Amount}" +
                    $"&extraData=" +
                    $"&ipnUrl={_options.Value.NotifyUrl}" +
                    $"&orderId={model.OrderId}" +
                    $"&orderInfo={model.OrderInformation}" +
                    $"&partnerCode={_options.Value.PartnerCode}" +
                    $"&redirectUrl={_options.Value.ReturnUrl}" +
                    $"&requestId={model.OrderId}" +
                    $"&requestType={_options.Value.RequestType}"; // Thêm requestType

                var signature = ComputeHmacSha256(rawData, _options.Value.SecretKey);

                // Ghi log request để kiểm tra
                var requestData = new
                {
                    accessKey = _options.Value.AccessKey,
                    partnerCode = _options.Value.PartnerCode,
                    requestType = _options.Value.RequestType,
                    ipnUrl = _options.Value.NotifyUrl,
                    redirectUrl = _options.Value.ReturnUrl,
                    orderId = model.OrderId,
                    amount = model.Amount.ToString(),
                    orderInfo = model.OrderInformation,
                    requestId = model.OrderId,
                    extraData = "",
                    signature = signature,
                    lang = "vi"
                };
                Console.WriteLine($"MoMo Request: {JsonConvert.SerializeObject(requestData)}");

                // Gửi yêu cầu đến MoMo
                var client = new RestClient(_options.Value.MomoApiUrl);
                var request = new RestRequest("", Method.Post);
                request.AddHeader("Content-Type", "application/json; charset=UTF-8");
                request.AddParameter("application/json", JsonConvert.SerializeObject(requestData), ParameterType.RequestBody);

                var response = await client.ExecuteAsync(request);

                // Ghi log phản hồi từ MoMo
                Console.WriteLine($"MoMo Response Status: {(int)response.StatusCode} ({response.StatusCode})");
                Console.WriteLine($"MoMo Response Content: {response.Content}");

                // Kiểm tra trạng thái HTTP
                if (!response.IsSuccessful)
                {
                    return new MomoCreatePaymentResponseModel
                    {
                        ResultCode = 99,
                        Message = $"Lỗi HTTP: {(int)response.StatusCode} ({response.StatusCode}) - {response.Content}"
                    };
                }

                // Phân tích JSON
                var momoResponse = JsonConvert.DeserializeObject<MomoCreatePaymentResponseModel>(response.Content);
                if (momoResponse == null)
                {
                    return new MomoCreatePaymentResponseModel
                    {
                        ResultCode = 99,
                        Message = "Không thể phân tích phản hồi từ MoMo"
                    };
                }

                return momoResponse;
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"JSON Parse Error: {ex.Message}");
                return new MomoCreatePaymentResponseModel
                {
                    ResultCode = 99,
                    Message = $"Lỗi phân tích JSON: {ex.Message}"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"General Error in CreatePaymentMomo: {ex.Message}");
                return new MomoCreatePaymentResponseModel
                {
                    ResultCode = 99,
                    Message = $"Lỗi tổng quát: {ex.Message}"
                };
            }
        }

        public MomoExecuteResponseModel PaymentExecuteAsync(IQueryCollection collection)
        {
            var amount = collection.FirstOrDefault(s => s.Key == "amount").Value;
            var orderInfo = collection.FirstOrDefault(s => s.Key == "orderInfo").Value;
            var orderId = collection.FirstOrDefault(s => s.Key == "orderId").Value;
            var resultCode = collection["resultCode"];

            return new MomoExecuteResponseModel
            {
                Amount = amount,
                OrderId = orderId,
                OrderInfo = orderInfo,
                ResultCode = int.TryParse(resultCode, out var code) ? code : -1
            };
        }

        private string ComputeHmacSha256(string message, string secretKey)
        {
            var keyBytes = Encoding.UTF8.GetBytes(secretKey);
            var messageBytes = Encoding.UTF8.GetBytes(message);

            byte[] hashBytes;

            using (var hmac = new HMACSHA256(keyBytes))
            {
                hashBytes = hmac.ComputeHash(messageBytes);
            }

            var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            return hashString;
        }
    }
}