using Microsoft.AspNetCore.Mvc;

namespace BlazorWebApp.Controllers
{
    public class ExportController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
