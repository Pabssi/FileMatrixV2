using System.Diagnostics;
using System.Security.Claims;
using FileMatrix_Pabiran_.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace FileMatrix_Pabiran_.Controllers
{
    public class HomeController : Controller
    {
        private readonly FileMatrix_Pabiran_.Data.ApplicationDbContext _db;

        public HomeController(FileMatrix_Pabiran_.Data.ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                if (User?.Identity?.IsAuthenticated == true)
                {
                    // 1. Priority: SuperAdmin Redirection
                    if (User.IsInRole("SuperAdmin"))
                    {
                        return RedirectToAction("Index", "SuperAdmin", new { area = "SuperAdmin" });
                    }

                    // 2. Organization Member/Owner Redirection
                    var email = User.FindFirstValue(ClaimTypes.Email);
                    var currentUser = email != null
                        ? await _db.Users.FirstOrDefaultAsync(u => u.Email == email)
                        : null;

                    if (currentUser != null)
                    {
                        var hasActiveOrg = await _db.WorkplaceMembers
                            .AnyAsync(m => m.UserID == currentUser.UserID);

                        if (!hasActiveOrg)
                        {
                            hasActiveOrg = await _db.Workplaces
                                .AnyAsync(o => o.CreatedByUserID == currentUser.UserID && o.IsActive);
                        }

                        if (hasActiveOrg)
                        {
                            // Redirect to the Admin Dashboard (User Preferred)
                            return RedirectToAction("Index", "Admin", new { area = "Admin" });
                        }
                        
                        // 3. Fallback: Logged in but no organization -> Go to Onboarding
                        return RedirectToAction("Index", "Organizations");
                    }
                }
            }
            catch { }

            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
