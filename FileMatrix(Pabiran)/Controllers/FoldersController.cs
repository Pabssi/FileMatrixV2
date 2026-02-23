using Microsoft.AspNetCore.Mvc;

namespace FileMatrix_Pabiran_.Controllers
{
    public class FoldersController : Controller
    {
        public IActionResult Index() => View();
        public IActionResult Create() => View();
        public IActionResult Details(int id) => View();
    }
}
