using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Services.Interfaces;

namespace Bloomie.Services.Implementations
{
    public class CategoryService : ICategoryService
    {
        private readonly ApplicationDbContext _context;

        public CategoryService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Category>> GetAllCategoriesAsync()
        {
            return await _context.Categories
                .Include(c => c.SubCategories)
                .Include(c => c.ParentCategory)
                .Include(c => c.Products)
                .ToListAsync();
        }

        public async Task<IEnumerable<Category>> GetTopLevelCategoriesAsync()
        {
            return await _context.Categories
                .Where(c => c.ParentCategoryId == null)
                .Include(c => c.SubCategories)
                .ToListAsync();
        }

        public async Task<Category> GetCategoryByIdAsync(int id)
        {
            return await _context.Categories
                .Include(c => c.SubCategories)
                .Include(c => c.ParentCategory)
                .Include(c => c.Products)
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task AddCategoryAsync(Category category)
        {
            await _context.Categories.AddAsync(category);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateCategoryAsync(Category category)
        {
            _context.Categories.Update(category);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteCategoryAsync(int id)
        {
            var category = await _context.Categories
                .Include(c => c.SubCategories)
                .Include(c => c.Products)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category == null)
            {
                throw new KeyNotFoundException($"Danh mục với Id {id} không tồn tại.");
            }

            if (category.SubCategories.Any())
            {
                throw new InvalidOperationException("Không thể xóa danh mục này vì nó có danh mục con. Vui lòng xóa các danh mục con trước.");
            }

            if (category.Products != null && category.Products.Any())
            {
                throw new InvalidOperationException("Không thể xóa danh mục này vì nó có sản phẩm liên kết. Vui lòng xóa hoặc chuyển các sản phẩm sang danh mục khác trước.");
            }

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();
        }

        public async Task AssignSubCategoriesAsync(int parentCategoryId, List<int> subCategoryIds)
        {
            if (parentCategoryId == 0 || subCategoryIds == null || !subCategoryIds.Any())
            {
                throw new ArgumentException("Danh mục cha và danh mục con không hợp lệ.");
            }

            var parentCategory = await _context.Categories
                .FirstOrDefaultAsync(c => c.Id == parentCategoryId);
            if (parentCategory == null)
            {
                throw new KeyNotFoundException($"Danh mục cha với Id {parentCategoryId} không tồn tại.");
            }

            var subCategories = await _context.Categories
                .Where(c => subCategoryIds.Contains(c.Id))
                .ToListAsync();

            foreach (var subCategory in subCategories)
            {
                // Kiểm tra để tránh vòng lặp danh mục
                if (subCategory.Id == parentCategoryId)
                {
                    throw new InvalidOperationException("Không thể gán danh mục làm con của chính nó.");
                }

                subCategory.ParentCategoryId = parentCategoryId;
            }

            await _context.SaveChangesAsync();
        }
    }
}
