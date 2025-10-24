using Bloomie.Models.Entities;

namespace Bloomie.Models.ViewModels
{
    public class ProductViewModel
    {
        public List<ProductWithRating> Products { get; set; }
        public List<ProductWithRating> NewProducts { get; set; }
        public List<ProductWithRating> BestSellingProducts { get; set; }
        public List<ProductWithRating> BestSellingWithPromotions { get; set; }
        public List<ProductWithRating> NewWithPromotions { get; set; }
        public List<Category> PopularCategories { get; set; }
        public List<ProductWithRating> SpecialOffers { get; set; }

        public class ProductWithRating
        {
            public Product Product { get; set; }
            public decimal Rating { get; set; }
            public string Occasion { get; set; }
            public string Object { get; set; }
            public string PresentationStyle { get; set; }
            public List<string> Colors { get; set; }
            public List<string> FlowerTypes { get; set; }
        }
    }
}