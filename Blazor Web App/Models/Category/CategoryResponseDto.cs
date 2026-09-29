namespace BlazorWebApp.Models.Category
{
    public class CategoryResponseDto
    {
        public int CategoryID { get; set; }
        public string CategoryTitle { get; set; } = string.Empty;
        public string CategoryNameInUrdu { get; set; } = string.Empty;
        public bool InActive { get; set; }
        public DateTime CreatedAt { get; set; } // <--- Ensure this exists
    }
}
