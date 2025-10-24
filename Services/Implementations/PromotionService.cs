using Microsoft.EntityFrameworkCore;
using Bloomie.Data;
using Bloomie.Models.Entities;
using Bloomie.Services.Interfaces;

namespace Bloomie.Services.Implementations
{
    public class PromotionService : IPromotionService
    {
        private readonly ApplicationDbContext _context;

        public PromotionService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Promotion>> GetAllPromotionsAsync()
        {
            return await _context.Promotions
                .Include(p => p.PromotionProducts)
                .ThenInclude(pp => pp.Product)
                .ToListAsync();
        }
    }
}
