using BlazorWebApp.Models;
using BlazorWebApp.Models.Category;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using System.Data;

namespace BlazorWebApp.Services;

public class CategoryService : ICategoryService
{
    private readonly ISqlConnectionFactory _dbFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CategoryService> _logger;

    private const string CacheKeyPrefix = "CategoryCache_";
    private static int _cacheVersion = 0;   // bumped on every mutation → invalidates all cached keys

    public CategoryService(ISqlConnectionFactory dbFactory,
                           IMemoryCache cache,
                           ILogger<CategoryService> logger)
    {
        _dbFactory = dbFactory;
        _cache = cache;
        _logger = logger;
    }

    // ----------------------------------------------------------------
    // READ — Paged
    // ----------------------------------------------------------------
    public async Task<PagedResult<CategoryResponseDto>> GetCategoriesPagedAsync(
        string searchTerm, int page, int pageSize, CancellationToken token = default)
    {
        string key = $"{CacheKeyPrefix}v{_cacheVersion}_Paged_{searchTerm}_{page}_{pageSize}";

        if (_cache.TryGetValue(key, out PagedResult<CategoryResponseDto>? cached) && cached != null)
            return cached;

        using var connection = _dbFactory.CreateConnection();
        int offset = (page - 1) * pageSize;

        var parameters = new
        {
            SearchTerm = searchTerm ?? "",
            Offset = offset,
            PageSize = pageSize
        };

        var command = new CommandDefinition(
            "sp_Inv_Category_GetPaged",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: token);

        using var multi = await connection.QueryMultipleAsync(command);

        int total = await multi.ReadSingleAsync<int>();
        var rows = (await multi.ReadAsync<CategoryResponseDto>()).ToList();

        var result = new PagedResult<CategoryResponseDto>
        {
            Data = rows,
            TotalRecords = total
        };

        _cache.Set(key, result, TimeSpan.FromMinutes(2));
        return result;
    }

    // ----------------------------------------------------------------
    // READ — All (for export)
    // ----------------------------------------------------------------
    public async Task<IEnumerable<CategoryResponseDto>> GetAllCategoriesAsync(
        string searchTerm, CancellationToken token = default)
    {
        string key = $"{CacheKeyPrefix}v{_cacheVersion}_All_{searchTerm}";

        if (_cache.TryGetValue(key, out IEnumerable<CategoryResponseDto>? cached) && cached != null)
            return cached;

        using var connection = _dbFactory.CreateConnection();
        var parameters = new { SearchTerm = searchTerm ?? "" };

        var command = new CommandDefinition(
            "sp_Inv_Category_GetAll",
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: token);

        var rows = (await connection.QueryAsync<CategoryResponseDto>(command)).ToList();
        _cache.Set(key, rows, TimeSpan.FromMinutes(2));
        return rows;
    }

    // ----------------------------------------------------------------
    // VALIDATION — duplicate check
    // ----------------------------------------------------------------
    public async Task<bool> IsCategoryTitleExistsAsync(string title, int excludeId = 0)
    {
        try
        {
            using var connection = _dbFactory.CreateConnection();
            const string sql = @"SELECT COUNT(1)
                                 FROM dbo.Inv_CategoryInfo
                                 WHERE LOWER(LTRIM(RTRIM(CategoryTitle)))
                                     = LOWER(LTRIM(RTRIM(@Title)))
                                   AND CategoryID <> @ExcludeId";

            int count = await connection.ExecuteScalarAsync<int>(
                sql,
                new { Title = (title ?? "").Trim(), ExcludeId = excludeId });

            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check duplicate category title: {Title}", title);
            return false;   // fail open
        }
    }

    // ----------------------------------------------------------------
    // CREATE
    // ----------------------------------------------------------------
    public async Task CreateCategoryAsync(CategoryCreateDto category)
    {
        try
        {
            using var connection = _dbFactory.CreateConnection();
            await connection.ExecuteAsync(
                "sp_Inv_Category_Insert",
                category,
                commandType: CommandType.StoredProcedure);

            InvalidateCache();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create category: {Title}", category.CategoryTitle);
            throw;
        }
    }

    // ----------------------------------------------------------------
    // UPDATE
    // ----------------------------------------------------------------
    public async Task UpdateCategoryAsync(CategoryUpdateDto category)
    {
        try
        {
            using var connection = _dbFactory.CreateConnection();
            await connection.ExecuteAsync(
                "sp_Inv_Category_Update",
                category,
                commandType: CommandType.StoredProcedure);

            InvalidateCache();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update category ID: {Id}", category.CategoryID);
            throw;
        }
    }

    // ----------------------------------------------------------------
    // DELETE — single
    // ----------------------------------------------------------------
    public async Task DeleteCategoryAsync(int id)
    {
        try
        {
            using var connection = _dbFactory.CreateConnection();
            await connection.ExecuteAsync(
                "sp_Inv_Category_Delete",
                new { CategoryID = id },
                commandType: CommandType.StoredProcedure);

            InvalidateCache();
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            _logger.LogWarning(ex, "Attempted to delete category ID {Id} which is bound to products.", id);
            throw new InvalidOperationException(
                "Cannot delete this category because active products are assigned to it.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete category ID: {Id}", id);
            throw;
        }
    }

    // ----------------------------------------------------------------
    // DELETE — bulk
    // ----------------------------------------------------------------
    public async Task<int> DeleteCategoriesAsync(IEnumerable<int> ids)
    {
        var list = ids?.Where(i => i > 0).Distinct().ToList() ?? new List<int>();
        if (list.Count == 0) return 0;

        // Comma-separated IDs — safe because we filtered to positive ints
        string csv = string.Join(",", list);

        try
        {
            using var connection = _dbFactory.CreateConnection();

            int deleted = await connection.ExecuteScalarAsync<int>(
                "sp_Inv_Category_DeleteBatch",
                new { IDs = csv },
                commandType: CommandType.StoredProcedure);

            InvalidateCache();
            return deleted;
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            _logger.LogWarning(ex,
                "Bulk delete blocked by FK constraint for IDs: {Ids}", csv);
            throw new InvalidOperationException(
                "One or more selected categories are assigned to products and cannot be deleted.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk delete failed for IDs: {Ids}", csv);
            throw;
        }
    }

    // ----------------------------------------------------------------
    // Cache invalidation — bump version, old keys become unreachable
    // ----------------------------------------------------------------
    private void InvalidateCache()
    {
        System.Threading.Interlocked.Increment(ref _cacheVersion);
    }
}