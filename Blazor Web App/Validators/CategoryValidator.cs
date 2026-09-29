using FluentValidation;
using BlazorWebApp.Models.Category;   // ← must point to Models.Category

namespace BlazorWebApp.Validators
{
    public class CategoryValidator : AbstractValidator<CategoryCreateDto>
    {
        public CategoryValidator()
        {
            RuleFor(x => x.CategoryTitle)
                .NotEmpty().WithMessage("Category title is required.")
                .MaximumLength(100).WithMessage("Category title cannot exceed 100 characters.");

            RuleFor(x => x.CategoryNameInUrdu)
                .MaximumLength(100).WithMessage("Urdu title cannot exceed 100 characters.")
                .When(x => !string.IsNullOrEmpty(x.CategoryNameInUrdu));
        }
    }
}