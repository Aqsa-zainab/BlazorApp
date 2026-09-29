using BlazorWebApp.Components;
using BlazorWebApp.Services;
using BlazorWebApp.Validators;
using BlazorWebApp.Models.Category;   // ← added
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

// ---------- Blazor ----------
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ---------- Infrastructure ----------
builder.Services.AddMemoryCache();

// ---------- Database ----------
builder.Services.AddScoped<ISqlConnectionFactory, SqlConnectionFactory>();

// ---------- Business services ----------
builder.Services.AddScoped<ICategoryService, CategoryService>();

// ---------- Validators ----------
builder.Services.AddScoped<IValidator<CategoryCreateDto>, CategoryValidator>();

// ---------- UI services ----------
builder.Services.AddScoped<ToastService>();

var app = builder.Build();

// ---------- Pipeline ----------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();