namespace Bloomie.Models.Entities
{
    public class Category
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int? ParentCategoryId { get; set; } // Khóa ngoại trỏ đến danh mục cha
        public Category? ParentCategory { get; set; } // Có thể thuộc về một category cha
        public List<Category>? SubCategories { get; set; } = new List<Category>(); // 1 category có thể có nhiều con
        public List<Product>? Products { get; set; }
        public string? Description { get; set; }
    }
}
