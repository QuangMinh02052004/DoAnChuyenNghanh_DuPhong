using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Services.Interfaces;

namespace Bloomie.Services.Implementations
{
    public class ProductService : IProductService
    {
        private readonly ApplicationDbContext _context;
        public ProductService(ApplicationDbContext context)
        {
            _context = context;
        }
        public async Task<IEnumerable<Product>> GetAllProductsAsync()
        {
            return await _context.Products
            .Include(p => p.Category)
            .Include(p => p.PresentationStyle)
            .Include(p => p.FlowerTypeProducts)
            .ThenInclude(ftp => ftp.FlowerType).ToListAsync(); 
        }
        public async Task<Product> GetProductByIdAsync(int id)
        {
            return await _context.Products
            .Include(p => p.Images)
            .Include(p => p.FlowerTypeProducts)
            .FirstOrDefaultAsync(p => p.Id == id);
        }
        public async Task<List<Product>> GetProductsByCategoryIdAsync(int categoryId)
        {
            return await _context.Products
                .Where(p => p.CategoryId == categoryId)
                .ToListAsync();
        }
        public async Task AddProductAsync(Product product)
        {
            _context.Products.Add(product);
            await _context.SaveChangesAsync();
        }
        public async Task UpdateProductAsync(Product product)
        {
            _context.Products.Update(product);
            await _context.SaveChangesAsync();
        }
        public async Task DeleteProductAsync(int id)
        {
            var product = await _context.Products.FindAsync(id);
            _context.Products.Remove(product);
            await _context.SaveChangesAsync();
        }
    }
}

