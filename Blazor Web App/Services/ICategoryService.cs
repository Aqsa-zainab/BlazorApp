using BlazorWebApp.Models;
using BlazorWebApp.Models.Category;

namespace BlazorWebApp.Services;

public interface ICategoryService
{
    Task<PagedResult<CategoryResponseDto>> GetCategoriesPagedAsync(string searchTerm, int page, int pageSize,CancellationToken token = default);

    Task<IEnumerable<CategoryResponseDto>> GetAllCategoriesAsync(string searchTerm,CancellationToken token = default);

    Task<bool> IsCategoryTitleExistsAsync(string title, int excludeId = 0);

    Task CreateCategoryAsync(CategoryCreateDto category);
    Task UpdateCategoryAsync(CategoryUpdateDto category);
    Task DeleteCategoryAsync(int id);

    /// <summary>Deletes multiple categories in one round-trip. Returns rows affected.</summary>
    Task<int> DeleteCategoriesAsync(IEnumerable<int> ids);
}