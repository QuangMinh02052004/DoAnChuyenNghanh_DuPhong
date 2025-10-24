using Bloomie.Models.Entities;

namespace Bloomie.Models.ViewModels
{
    public class AssignSubCategoriesViewModel
    {
        public int ParentCategoryId { get; set; }
        public List<int> SubCategoryIds { get; set; } = new List<int>();
        public List<Category> ParentCategories { get; set; } = new List<Category>();
        public List<Category> SubCategories { get; set; } = new List<Category>();
    }
}