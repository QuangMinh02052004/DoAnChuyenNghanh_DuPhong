using System.Reflection.Emit;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Bloomie.Models;
using Bloomie.Models.Entities;

namespace Bloomie.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<FlowerType> FlowerTypes { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<PresentationStyle> PresentationStyles { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<ProductImage> ProductImages { get; set; }
        public DbSet<Promotion> Promotions { get; set; }
        public DbSet<PromotionProduct> PromotionProducts { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderDetail> OrderDetails { get; set; }
        public DbSet<OrderStatusHistory> OrderStatusHistories { get; set; }
        public DbSet<InventoryTransaction> InventoryTransactions { get; set; }
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<Batch> Batches { get; set; }
        public DbSet<BatchFlowerType> BatchFlowerTypes { get; set; }
        public DbSet<FlowerTypeProduct> FlowerTypeProducts { get; set; }
        public DbSet<UserAccessLog> UserAccessLogs { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<Shipping> Shippings { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<Rating> Ratings { get; set; }
        public DbSet<Reply> Replies { get; set; }
        public DbSet<Report> Reports { get; set; }
        public DbSet<UserLike> UserLikes { get; set; }
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Quan hệ giữa Product và Category (1-n)
            builder.Entity<Product>()
                .HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId);


            // Quan hệ giữa Category và ParentCategory (1-n)
            builder.Entity<Category>()
                .HasOne(c => c.ParentCategory)
                .WithMany(c => c.SubCategories)
                .HasForeignKey(c => c.ParentCategoryId);


            // Quan hệ giữa Product và ProductImage (1-n)
            builder.Entity<ProductImage>()
                .HasOne(pi => pi.Product)
                .WithMany(p => p.Images)
                .HasForeignKey(pi => pi.ProductId);


            // Quan hệ nhiều-nhiều giữa Product và Promotion thông qua PromotionProduct
            builder.Entity<PromotionProduct>()
                .HasKey(pp => new { pp.PromotionId, pp.ProductId }); // Khóa chính composite


            builder.Entity<PromotionProduct>()
                .HasOne(pp => pp.Product)
                .WithMany(p => p.PromotionProducts)
                .HasForeignKey(pp => pp.ProductId);

            builder.Entity<PromotionProduct>()
                .HasOne(pp => pp.Promotion)
                .WithMany(p => p.PromotionProducts)
                .HasForeignKey(pp => pp.PromotionId);

            // Quan hệ giữa FlowerType và Supplier (n-n)
            builder.Entity<FlowerType>()
                .HasMany(ft => ft.Suppliers)
                .WithMany(s => s.FlowerTypes)
                .UsingEntity(j => j.ToTable("FlowerTypeSuppliers"));

            // Quan hệ nhiều-nhiều giữa FlowerType và Product thông qua FlowerTypeProduct
            builder.Entity<FlowerTypeProduct>()
                .HasKey(ftp => new { ftp.FlowerTypeId, ftp.ProductId });

            builder.Entity<FlowerTypeProduct>()
                .HasOne(ftp => ftp.FlowerType)
                .WithMany(ft => ft.FlowerTypeProducts)
                .HasForeignKey(ftp => ftp.FlowerTypeId);

            builder.Entity<FlowerTypeProduct>()
                .HasOne(ftp => ftp.Product)
                .WithMany(p => p.FlowerTypeProducts)
                .HasForeignKey(ftp => ftp.ProductId);

            // Quan hệ nhiều-nhiều giữa Batch và FlowerType thông qua BatchFlowerType
            builder.Entity<BatchFlowerType>()
                .HasKey(bft => new { bft.BatchId, bft.FlowerTypeId });

            builder.Entity<BatchFlowerType>()
                .HasOne(bft => bft.Batch)
                .WithMany(b => b.BatchFlowerTypes)
                .HasForeignKey(bft => bft.BatchId);

            builder.Entity<BatchFlowerType>()
                .HasOne(bft => bft.FlowerType)
                .WithMany(ft => ft.BatchFlowerTypes)
                .HasForeignKey(bft => bft.FlowerTypeId);

            // Quan hệ giữa FlowerType và InventoryTransaction (1-n)
            builder.Entity<InventoryTransaction>()
                .HasOne(it => it.FlowerType)
                .WithMany(ft => ft.Transactions)
                .HasForeignKey(it => it.FlowerTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            // Quan hệ giữa Supplier và Batch (1-n)
            builder.Entity<Batch>()
                .HasOne(b => b.Supplier)
                .WithMany(s => s.Batches)
                .HasForeignKey(b => b.SupplierId);

            // Quan hệ giữa Supplier và InventoryTransaction (1-n)
            builder.Entity<InventoryTransaction>()
                .HasOne(it => it.Supplier)
                .WithMany(s => s.Transactions)
                .HasForeignKey(it => it.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            // Quan hệ giữa Batch và InventoryTransaction (1-n)
            builder.Entity<InventoryTransaction>()
                .HasOne(it => it.Batch)
                .WithMany(b => b.Transactions)
                .HasForeignKey(it => it.BatchId)
                .OnDelete(DeleteBehavior.Restrict);

            // Quan hệ giữa Order và OrderDetails (1 - n)
            builder.Entity<Order>()
            .HasMany(o => o.OrderDetails)
            .WithOne(od => od.Order)
            .HasForeignKey(od => od.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

            // Quan hệ giữa Order và Payment (1 - 1)
            builder.Entity<Order>()
            .HasOne(o => o.Payment)
            .WithOne(p => p.Order)
            .HasForeignKey<Payment>(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

            // Cấu hình cho Notification và User (1-n)
            builder.Entity<Notification>()
                .HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Cấu hình cho Message và User (1-n)
            builder.Entity<Message>()
                .HasOne(m => m.Sender)
                .WithMany()
                .HasForeignKey(m => m.SenderId)
                .OnDelete(DeleteBehavior.Restrict);

            // Cấu hình cho Message và User (1-n)
            builder.Entity<Message>()
                .HasOne(m => m.Receiver)
                .WithMany()
                .HasForeignKey(m => m.ReceiverId)
                .OnDelete(DeleteBehavior.Restrict);

            // Cấu hình cho Reply và Rating (1-n)
            builder.Entity<Reply>()
                .HasOne(r => r.Rating)
                .WithMany(rating => rating.Replies)
                .HasForeignKey(r => r.RatingId)
                .OnDelete(DeleteBehavior.Restrict);

            // Cấu hình cho Report và Rating (1-n)
            builder.Entity<Report>()
                .HasOne(r => r.Rating)
                .WithMany(rating => rating.Reports)
                .HasForeignKey(r => r.RatingId)
                .OnDelete(DeleteBehavior.Restrict);

            // Cấu hình cho UserLike và Rating (1-n)
            builder.Entity<UserLike>()
                .HasOne(ul => ul.Rating)
                .WithMany(r => r.UserLikes)
                .HasForeignKey(ul => ul.RatingId)
                .OnDelete(DeleteBehavior.Restrict);

            // Cấu hình cho UserLike và Reply (1-n)
            builder.Entity<UserLike>()
                .HasOne(ul => ul.Reply)
                .WithMany(r => r.UserLikes)
                .HasForeignKey(ul => ul.ReplyId)
                .OnDelete(DeleteBehavior.Cascade);

            // Cấu hình cho UserLike và User (1-n)
            builder.Entity<UserLike>()
                .HasOne(ul => ul.User)
                .WithMany()
                .HasForeignKey(ul => ul.UserId)
                .OnDelete(DeleteBehavior.Restrict);

        }
    }
}
