using System.ComponentModel.DataAnnotations;

namespace BlazorWebApp.Models.Category
{
    public class CategoryCreateDto
    {
        [Required(ErrorMessage = "Category Title is required.")]
        [StringLength(100, ErrorMessage = "Title cannot exceed 100 characters.")]
        public string CategoryTitle { get; set; } = "";

        [StringLength(100, ErrorMessage = "Urdu Name cannot exceed 100 characters.")]
        public string CategoryNameInUrdu { get; set; } = "";

        public bool InActive { get; set; } = false;
    }
}
