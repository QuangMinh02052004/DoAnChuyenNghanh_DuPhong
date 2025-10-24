namespace Bloomie.Models.Entities
{
    public enum TransactionType
    {
        Import, // Nhập kho
        Export, // Xuất kho
        Adjustment // Điều chỉnh kho (hoa hỏng, lỗi, v.v.)
    }

    public enum AdjustmentType
    {
        Damaged, // Hư hỏng
        Lost, // Mất mát
        InventoryError, // Sai sót kiểm kê
        Other // Khác
    }

    public enum TransactionStatus
    {
        Pending, // Chờ duyệt
        Approved, // Đã duyệt
        Rejected // Bị từ chối
    }

    // Giao dịch kho
    public class InventoryTransaction
    {
        public int Id { get; set; }
        public int? ProductId { get; set; }
        public Product Product { get; set; }
        public int? FlowerTypeId { get; set; } // Nullable, dùng khi giao dịch liên quan đến FlowerType
        public FlowerType FlowerType { get; set; }
        public TransactionType TransactionType { get; set; }
        public int Quantity { get; set; } // Số lượng thay đổi
        public string Reason { get; set; } // Lý do (ví dụ: "Nhập kho từ nhà cung cấp", "Xuất kho cho đơn hàng #123")
        public DateTime TransactionDate { get; set; }
        public string CreatedBy { get; set; } // Người thực hiện (Admin ID hoặc tên)
        public decimal UnitPrice { get; set; } // Giá đơn vị tại thời điểm giao dịch
        public decimal TotalValue => Quantity * UnitPrice; // Tổng giá trị (tính toán)

        // Liên kết với đơn hàng 
        public string? OrderId { get; set; } // Nullable vì không phải giao dịch nào cũng liên quan đến đơn hàng
        public Order Order { get; set; }

        // Liên kết với nhà cung cấp 
        public int? SupplierId { get; set; }
        public Supplier Supplier { get; set; }

        // Liên kết với lô hàng 
        public int? BatchId { get; set; } 
        public Batch Batch { get; set; }

        // Phân loại điều chỉnh kho
        public AdjustmentType? AdjustmentType { get; set; } // Nullable, chỉ áp dụng cho TransactionType = Adjustment

        // Trạng thái giao dịch
        public TransactionStatus Status { get; set; } = TransactionStatus.Approved; // Mặc định là Approved
    }
}