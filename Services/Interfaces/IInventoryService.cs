using Bloomie.Models.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Bloomie.Services.Interfaces
{
    public interface IInventoryService
    {
        Task ImportFlowerTypeAsync(int flowerTypeId, int quantity, string reason, string createdBy, int? supplierId, int? batchId);
        Task AdjustFlowerTypeAsync(int flowerTypeId, int quantity, AdjustmentType adjustmentType, string reason, string userName, int batchId);
        Task ExportInventoryAsync(int flowerTypeId, int quantity, string reason, string createdBy, string? orderId);
        Task<IEnumerable<InventoryTransaction>> GetInventoryHistoryAsync(int flowerTypeId);
        Task<List<FlowerType>> GetAllFlowerTypesAsync();
    }
}