using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Bloomie.Services.Implementations
{
    public class InventoryService : IInventoryService
    {
        private readonly ApplicationDbContext _context;

        public InventoryService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<FlowerType>> GetAllFlowerTypesAsync()
        {
            return await _context.FlowerTypes
                .Where(ft => ft.IsActive)
                .ToListAsync();
        }

        public async Task ImportFlowerTypeAsync(int flowerTypeId, int quantity, string reason, string createdBy, int? supplierId, int? batchId)
        {
            var flowerType = await _context.FlowerTypes.FindAsync(flowerTypeId);
            if (flowerType != null)
            {
                // Tính tổng CurrentQuantity từ BatchFlowerTypes
                var totalQuantity = await _context.BatchFlowerTypes
                    .Where(bft => bft.FlowerTypeId == flowerTypeId)
                    .SumAsync(bft => bft.CurrentQuantity);

                flowerType.Quantity = totalQuantity + quantity; // Cộng thêm quantity mới nhập
                _context.FlowerTypes.Update(flowerType);

                var transaction = new InventoryTransaction
                {
                    FlowerTypeId = flowerTypeId,
                    Quantity = quantity,
                    TransactionType = TransactionType.Import,
                    Reason = reason,
                    CreatedBy = createdBy,
                    TransactionDate = DateTime.Now,
                    SupplierId = supplierId,
                    BatchId = batchId
                };
                _context.InventoryTransactions.Add(transaction);
                await _context.SaveChangesAsync();
            }
        }

        public async Task AdjustFlowerTypeAsync(int flowerTypeId, int quantity, AdjustmentType adjustmentType, string reason, string userName, int batchId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Lấy loại hoa
                var flowerType = await _context.FlowerTypes
                    .FirstOrDefaultAsync(ft => ft.Id == flowerTypeId && ft.IsActive);
                if (flowerType == null)
                {
                    throw new Exception("Loại hoa không tồn tại hoặc không hoạt động.");
                }

                // Kiểm tra số lượng tồn kho
                if (flowerType.Quantity < quantity)
                {
                    throw new Exception($"Số lượng tồn kho không đủ. Hiện tại chỉ có {flowerType.Quantity} {flowerType.Name}.");
                }

                // Lấy lô hàng và thông tin BatchFlowerTypes
                var batch = await _context.Batches
                    .Include(b => b.Supplier)
                    .Include(b => b.BatchFlowerTypes)
                    .FirstOrDefaultAsync(b => b.Id == batchId);
                if (batch == null)
                {
                    throw new Exception("Lô hàng không tồn tại.");
                }

                // Tìm BatchFlowerType tương ứng với loại hoa
                var batchFlowerType = batch.BatchFlowerTypes
                    .FirstOrDefault(bft => bft.FlowerTypeId == flowerTypeId);
                if (batchFlowerType == null)
                {
                    throw new Exception($"Loại hoa {flowerType.Name} không tồn tại trong lô {batchId}.");
                }

                // Kiểm tra số lượng trong lô
                if (batchFlowerType.CurrentQuantity < quantity)
                {
                    throw new Exception($"Số lượng trong lô {batchId} không đủ. Hiện tại chỉ có {batchFlowerType.CurrentQuantity} {flowerType.Name}.");
                }

                // Giảm số lượng trong BatchFlowerTypes
                batchFlowerType.CurrentQuantity -= quantity;
                _context.BatchFlowerTypes.Update(batchFlowerType);

                // Giảm số lượng tồn kho trong FlowerTypes
                flowerType.Quantity -= quantity;
                _context.FlowerTypes.Update(flowerType);

                // Lưu giao dịch điều chỉnh kho
                var inventoryTransaction = new InventoryTransaction
                {
                    FlowerTypeId = flowerTypeId,
                    Quantity = -quantity,
                    TransactionType = TransactionType.Adjustment,
                    AdjustmentType = adjustmentType,
                    Reason = reason,
                    TransactionDate = DateTime.Now,
                    CreatedBy = userName,
                    Status = TransactionStatus.Approved,
                    SupplierId = batch.SupplierId,
                    BatchId = batchId
                };
                _context.InventoryTransactions.Add(inventoryTransaction);

                // Lưu thay đổi
                var changes = await _context.SaveChangesAsync();
                if (changes < 3) // Cần lưu 3 thay đổi: FlowerTypes, BatchFlowerTypes, InventoryTransaction
                {
                    throw new Exception($"Không lưu đủ thay đổi. Số thay đổi: {changes}, Dự kiến: 3.");
                }

                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                throw new Exception($"Lỗi khi điều chỉnh kho: {ex.Message}", ex);
            }
        }

        public async Task ExportInventoryAsync(int flowerTypeId, int quantity, string reason, string createdBy, string? orderId)
        {
            var flowerType = await _context.FlowerTypes.FindAsync(flowerTypeId);
            if (flowerType == null)
            {
                throw new Exception("Loại hoa không tồn tại.");
            }

            flowerType.Quantity -= quantity;
            if (flowerType.Quantity < 0)
            {
                throw new Exception("Tồn kho không đủ để xuất.");
            }

            var transaction = new InventoryTransaction
            {
                FlowerTypeId = flowerTypeId,
                TransactionType = TransactionType.Export,
                Quantity = -quantity,
                Reason = reason ?? $"Xuất kho cho đơn hàng #{orderId}",
                TransactionDate = DateTime.Now,
                CreatedBy = createdBy,
                Status = TransactionStatus.Approved,
                OrderId = orderId
            };

            _context.FlowerTypes.Update(flowerType);
            _context.InventoryTransactions.Add(transaction);
            await _context.SaveChangesAsync();
        }

        public async Task<IEnumerable<InventoryTransaction>> GetInventoryHistoryAsync(int flowerTypeId)
        {
            return await _context.InventoryTransactions
                .Where(t => t.FlowerTypeId == flowerTypeId)
                .Include(t => t.FlowerType)
                .Include(t => t.Order)
                .Include(t => t.Supplier)
                .Include(t => t.Batch)
                .OrderByDescending(t => t.TransactionDate)
                .ToListAsync();
        }
    }
}