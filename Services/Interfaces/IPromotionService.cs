using Bloomie.Models.Entities;

namespace Bloomie.Services.Interfaces
{
    public interface IPromotionService
    {
        Task<IEnumerable<Promotion>> GetAllPromotionsAsync();
        //Task<Promotion> GetPromotionByIdAsync(int id);
        //Task AddPromotionAsync(Promotion promotion);
        //Task UpdatePromotionAsync(Promotion promotion);
        //Task DeletePromotionAsync(int id);
    }
}
