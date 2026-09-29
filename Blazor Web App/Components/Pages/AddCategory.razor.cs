using BlazorWebApp.Components.Common;
using BlazorWebApp.Models;
using BlazorWebApp.Models.Category;
using BlazorWebApp.Services;
using FluentValidation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.Text.Json;

namespace BlazorWebApp.Components.Pages
{
    public partial class AddCategory : IDisposable
    {
        [Inject] private ICategoryService categoryService { get; set; } = default!;
        [Inject] private ToastService toastService { get; set; } = default!;
        [Inject] private IValidator<CategoryCreateDto> categoryValidator { get; set; } = default!;
        [Inject] private ProtectedLocalStorage LocalStorage { get; set; } = default!;

        // Element refs (pure Blazor — no JS)
        private ElementReference titleRef;
        private ElementReference urduRef;
        private ElementReference searchRef;

        private CategoryCreateDto newCategory = new();
        private int editingCategoryId = 0;
        private bool isEditing = false;
        private bool isSaving = false;

        private List<CategoryResponseDto> categoryList = new();
        private CancellationTokenSource? _cts;

        private bool isLoading = false;
        private ConfirmModal? deleteConfirmModal;
        private int categoryToDeleteId = 0;

        private HashSet<int> selectedIds = new();

        private string sortColumn = "CategoryID";
        private bool sortAscending = false;

        private const string DraftKey = "CategoryDraft";
        private bool hasDraft = false;

        private string searchTerm = "";
        private int currentPage = 1;
        private int pageSize = 10;
        private int totalRecords = 0;
        private int totalPages = 0;

        private System.Timers.Timer? _searchTimer;
        private IDisposable? _navHandler;
        private string? titleError;

        private bool HasUnsavedChanges =>
            !string.IsNullOrWhiteSpace(newCategory.CategoryTitle) ||
            !string.IsNullOrWhiteSpace(newCategory.CategoryNameInUrdu) ||
            newCategory.InActive;

        private bool IsAllSelected =>
            categoryList.Any() && categoryList.All(c => selectedIds.Contains(c.CategoryID));

        private IEnumerable<CategoryResponseDto> SortedCategories
        {
            get
            {
                var q = categoryList.AsEnumerable();
                q = sortColumn switch
                {
                    "CategoryID" => sortAscending ? q.OrderBy(x => x.CategoryID) : q.OrderByDescending(x => x.CategoryID),
                    "CategoryTitle" => sortAscending ? q.OrderBy(x => x.CategoryTitle) : q.OrderByDescending(x => x.CategoryTitle),
                    "CategoryNameInUrdu" => sortAscending ? q.OrderBy(x => x.CategoryNameInUrdu) : q.OrderByDescending(x => x.CategoryNameInUrdu),
                    "InActive" => sortAscending ? q.OrderBy(x => x.InActive) : q.OrderByDescending(x => x.InActive),
                    _ => q
                };
                return q;
            }
        }

        // ---------------- LIFECYCLE ----------------
        protected override async Task OnInitializedAsync()
        {
            // Register Blazor-native navigation guard (no JS)
            _navHandler = Nav.RegisterLocationChangingHandler(OnLocationChanging);

            await LoadCategories();
            await RestoreDraftAsync();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                try { await titleRef.FocusAsync(); } catch { }
            }
        }

        // ---------------- NAVIGATION GUARD ----------------
        private ValueTask OnLocationChanging(LocationChangingContext ctx)
        {
            if (HasUnsavedChanges && !isSaving)
            {
                // Blazor-native block — no JS needed
                ctx.PreventNavigation();
                toastService.ShowToast(
                    "You have unsaved changes. Save or cancel before leaving.",
                    ToastLevel.Warning);
            }
            return ValueTask.CompletedTask;
        }

        // ---------------- DRAFT ----------------
        private async Task RestoreDraftAsync()
        {
            try
            {
                var result = await LocalStorage.GetAsync<string>(DraftKey);
                if (result.Success && !string.IsNullOrWhiteSpace(result.Value))
                {
                    var draft = JsonSerializer.Deserialize<CategoryCreateDto>(result.Value);
                    if (draft != null && !string.IsNullOrWhiteSpace(draft.CategoryTitle))
                    {
                        newCategory = draft;
                        hasDraft = true;
                    }
                }
            }
            catch { }
        }

        private async Task SaveDraftAsync()
        {
            try
            {
                if (!HasUnsavedChanges && !isEditing)
                {
                    await LocalStorage.DeleteAsync(DraftKey);
                    return;
                }
                string json = JsonSerializer.Serialize(newCategory);
                await LocalStorage.SetAsync(DraftKey, json);
            }
            catch { }
        }

        private async Task DiscardDraft()
        {
            try { await LocalStorage.DeleteAsync(DraftKey); } catch { }
            newCategory = new CategoryCreateDto();
            hasDraft = false;
            StateHasChanged();
            try { await titleRef.FocusAsync(); } catch { }
        }

        // ---------------- KEYBOARD ----------------
        private async Task OnWrapperKeyDown(KeyboardEventArgs e)
        {
            // Alt+F → focus search
            if (e.AltKey && (e.Key == "f" || e.Key == "F"))
            {
                try { await searchRef.FocusAsync(); } catch { }
                return;
            }

            // Alt+S → save
            if (e.AltKey && (e.Key == "s" || e.Key == "S") && !isSaving)
            {
                await SaveCategory();
                return;
            }

            // F2 → new
            if (e.Key == "F2")
            {
                await NewCategory();
                return;
            }

            // Esc → cancel
            if (e.Key == "Escape")
            {
                if (HasUnsavedChanges || isEditing)
                    await CancelEdit();
            }
        }

        private async Task OnTitleKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Enter")
            {
                await SaveDraftAsync();
                try { await urduRef.FocusAsync(); } catch { }
            }
        }

        private async Task OnUrduKeyDown(KeyboardEventArgs e)
        {
            if (e.Key == "Enter")
                await SaveCategory();
        }

        // ---------------- BLUR VALIDATION ----------------
        private async Task ValidateTitleBlur()
        {
            if (string.IsNullOrWhiteSpace(newCategory.CategoryTitle))
            {
                titleError = "Category Title is required.";
                return;
            }
            if (newCategory.CategoryTitle.Length > 100)
            {
                titleError = "Category Title cannot exceed 100 characters.";
                return;
            }

            // Duplicate check only if creating or editing different
            int checkId = isEditing ? editingCategoryId : 0;
            bool exists = await categoryService.IsCategoryTitleExistsAsync(
                newCategory.CategoryTitle, checkId);

            titleError = exists
                ? $"A category named '{newCategory.CategoryTitle}' already exists."
                : null;

            await SaveDraftAsync();
            StateHasChanged();
        }

        // ---------------- SEARCH ----------------
        private async Task HandleSearch(ChangeEventArgs e)
        {
            searchTerm = e.Value?.ToString() ?? "";
            currentPage = 1;

            _searchTimer?.Stop();
            _searchTimer?.Dispose();
            _searchTimer = new System.Timers.Timer(300) { AutoReset = false };
            _searchTimer.Elapsed += async (_, __) =>
            {
                await InvokeAsync(async () =>
                {
                    _searchTimer?.Stop();
                    await LoadCategories();
                });
            };
            _searchTimer.Start();
            await Task.CompletedTask;
        }

        // ---------------- SORT ----------------
        private void SortBy(string column)
        {
            if (sortColumn == column) sortAscending = !sortAscending;
            else { sortColumn = column; sortAscending = true; }
            StateHasChanged();
        }

        private string SortIconClass(string column)
        {
            if (sortColumn != column) return "bi bi-arrow-down-up text-muted small";
            return sortAscending ? "bi bi-sort-up-alt text-primary" : "bi bi-sort-down text-primary";
        }

        // ---------------- LOAD ----------------
        private async Task LoadCategories()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            isLoading = true;
            StateHasChanged();

            try
            {
                var result = await categoryService.GetCategoriesPagedAsync(
                    searchTerm, currentPage, pageSize, token);

                categoryList = result.Data.ToList();
                totalRecords = result.TotalRecords;
                totalPages = (int)Math.Ceiling((double)totalRecords / pageSize);
                if (currentPage > totalPages && totalPages > 0) currentPage = totalPages;
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                toastService.ShowToast("Error loading: " + ex.Message, ToastLevel.Error);
            }
            finally
            {
                isLoading = false;
                StateHasChanged();
            }
        }

        private async Task GoToPage(int page)
        {
            if (page < 1 || page > totalPages || page == currentPage) return;
            currentPage = page;
            await LoadCategories();
        }

        private async Task OnPageSizeChanged(ChangeEventArgs e)
        {
            if (int.TryParse(e.Value?.ToString(), out int n) && n > 0)
            {
                pageSize = n;
                currentPage = 1;
                await LoadCategories();
            }
        }

        // ---------------- SELECT ----------------
        private void ToggleSelect(int id)
        {
            if (selectedIds.Contains(id)) selectedIds.Remove(id);
            else selectedIds.Add(id);
            StateHasChanged();
        }

        private void ToggleSelectAll()
        {
            if (IsAllSelected) selectedIds.Clear();
            else foreach (var c in categoryList) selectedIds.Add(c.CategoryID);
            StateHasChanged();
        }

        // ---------------- PRINT / EXPORT ----------------
        private void PrintHint()
        {
            toastService.ShowToast("Press Ctrl+P to print this page.", ToastLevel.Info);
        }

        private void ExportToExcel()
        {
            // Navigate to minimal API endpoint — browser triggers file download
            Nav.NavigateTo($"/api/categories/export?search={Uri.EscapeDataString(searchTerm)}",
                forceLoad: true);
        }

        // ---------------- CRUD ----------------
        private async Task NewCategory()
        {
            newCategory = new CategoryCreateDto();
            isEditing = false;
            editingCategoryId = 0;
            hasDraft = false;
            titleError = null;
            try { await LocalStorage.DeleteAsync(DraftKey); } catch { }
            StateHasChanged();
            await Task.Delay(50);
            try { await titleRef.FocusAsync(); } catch { }
        }

        private async Task ResetForm() => await NewCategory();

        private async Task EditCategory(CategoryResponseDto cat)
        {
            isEditing = true;
            editingCategoryId = cat.CategoryID;
            newCategory = new CategoryCreateDto
            {
                CategoryTitle = cat.CategoryTitle,
                CategoryNameInUrdu = cat.CategoryNameInUrdu,
                InActive = cat.InActive
            };
            hasDraft = false;
            titleError = null;
            try { await LocalStorage.DeleteAsync(DraftKey); } catch { }
            StateHasChanged();
            await Task.Delay(50);
            try { await titleRef.FocusAsync(); } catch { }
        }

        private async Task DuplicateCategory(CategoryResponseDto cat)
        {
            isEditing = false;
            editingCategoryId = 0;
            newCategory = new CategoryCreateDto
            {
                CategoryTitle = cat.CategoryTitle + " (Copy)",
                CategoryNameInUrdu = cat.CategoryNameInUrdu,
                InActive = cat.InActive
            };
            titleError = null;
            StateHasChanged();
            await Task.Delay(50);
            try { await titleRef.FocusAsync(); } catch { }
            toastService.ShowToast("Duplicated into form — edit and save.", ToastLevel.Info);
        }

        private async Task CancelEdit() => await NewCategory();

        private async Task SaveCategory()
        {
            if (isSaving) return;
            isSaving = true;
            StateHasChanged();

            try
            {
                var v = await categoryValidator.ValidateAsync(newCategory);
                if (!v.IsValid)
                {
                    toastService.ShowToast(v.Errors.First().ErrorMessage, ToastLevel.Warning);
                    return;
                }

                int checkId = isEditing ? editingCategoryId : 0;
                bool exists = await categoryService.IsCategoryTitleExistsAsync(
                    newCategory.CategoryTitle, checkId);
                if (exists)
                {
                    toastService.ShowToast(
                        $"'{newCategory.CategoryTitle}' already exists.", ToastLevel.Warning);
                    return;
                }

                if (isEditing)
                {
                    await categoryService.UpdateCategoryAsync(new CategoryUpdateDto
                    {
                        CategoryID = editingCategoryId,
                        CategoryTitle = newCategory.CategoryTitle,
                        CategoryNameInUrdu = newCategory.CategoryNameInUrdu,
                        InActive = newCategory.InActive
                    });
                    toastService.ShowToast("Category updated!", ToastLevel.Success);
                }
                else
                {
                    await categoryService.CreateCategoryAsync(newCategory);
                    toastService.ShowToast("Category saved!", ToastLevel.Success);
                }

                await NewCategory();
                await LoadCategories();
            }
            catch (Exception ex)
            {
                toastService.ShowToast("Error: " + ex.Message, ToastLevel.Error);
            }
            finally
            {
                isSaving = false;
                StateHasChanged();
                try { await titleRef.FocusAsync(); } catch { }
            }
        }

        // ---------------- DELETE + UNDO ----------------
        private async Task DeleteCategory(int id)
        {
            categoryToDeleteId = id;
            if (deleteConfirmModal != null) deleteConfirmModal.Show();
            await Task.CompletedTask;
        }

        private async Task OnConfirmDeleteConfirmed(bool confirmed)
        {
            int idToDelete = categoryToDeleteId;
            categoryToDeleteId = 0;
            if (!confirmed) return;

            var snapshot = categoryList.FirstOrDefault(c => c.CategoryID == idToDelete);

            try
            {
                await categoryService.DeleteCategoryAsync(idToDelete);
                await LoadCategories();

                if (snapshot != null)
                {
                    toastService.ShowToastWithAction(
                        $"Deleted '{snapshot.CategoryTitle}'.",
                        ToastLevel.Warning,
                        "Undo",
                        async () =>
                        {
                            await categoryService.CreateCategoryAsync(new CategoryCreateDto
                            {
                                CategoryTitle = snapshot.CategoryTitle,
                                CategoryNameInUrdu = snapshot.CategoryNameInUrdu,
                                InActive = snapshot.InActive
                            });
                            await LoadCategories();
                            toastService.ShowToast("Restored.", ToastLevel.Success);
                            StateHasChanged();
                        });
                }
            }
            catch (Exception ex)
            {
                toastService.ShowToast(ex.Message, ToastLevel.Error);
            }
        }

        private async Task DeleteSelected()
        {
            if (selectedIds.Count == 0) return;

            // Two-phase: first click asks again? Just confirm in UI already
            var ids = selectedIds.ToList();
            int count = ids.Count;

            try
            {
                int deleted = await categoryService.DeleteCategoriesAsync(ids);
                selectedIds.Clear();
                await LoadCategories();
                toastService.ShowToast(
                    $"{deleted} categor{(deleted == 1 ? "y" : "ies")} deleted.",
                    ToastLevel.Success);
            }
            catch (Exception ex)
            {
                toastService.ShowToast("Bulk delete failed: " + ex.Message, ToastLevel.Error);
            }
        }

        // ---------------- HELPERS ----------------
        private string CounterClass(string? value, int max)
        {
            int len = value?.Length ?? 0;
            if (len == 0) return "text-muted";
            if (len >= max) return "text-danger fw-bold";
            if (len >= max * 0.8) return "text-warning fw-bold";
            return "text-muted";
        }

        public void Dispose()
        {
            _navHandler?.Dispose();
            _searchTimer?.Stop();
            _searchTimer?.Dispose();
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}