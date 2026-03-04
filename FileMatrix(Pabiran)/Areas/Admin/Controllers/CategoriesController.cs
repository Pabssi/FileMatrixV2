using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FileMatrix_Pabiran_.Models;
using FileMatrix_Pabiran_.Data;

namespace FileMatrix_Pabiran_.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Route("Admin/Categories")]
    /// <summary>
    /// CategoriesController: Workplace Taxonomy Management.
    /// 
    /// RESPONSIBILITY: Manages the organizational labels (Categories) used to group 
    /// documents within a specific workplace.
    /// </summary>
    public class CategoriesController : BaseAdminController
    {
        public CategoriesController(ApplicationDbContext context) : base(context)
        {
        }

        /// <summary>
        /// Retrieves all categories for the current workplace and calculates 
        /// document counts per category for the UI.
        /// </summary>
        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            if (CurrentWorkplace == null) return RedirectToAction("Index", "Organizations", new { area = "" });

            // Simple Query: Get all the categories that belong to our current workplace.
            var categories = await _context.Categories
                .Where(c => c.WorkplaceID == CurrentWorkplace.WorkplaceID)
                .OrderBy(c => c.Name)
                .ToListAsync();

            // Optional: Count documents per category
            // Simple Query: Count how many documents are in each category so we can show counts in the UI.
            ViewBag.CategoryCounts = await _context.Documents
                .Where(d => d.WorkplaceID == CurrentWorkplace.WorkplaceID && d.CategoryID != null)
                .GroupBy(d => d.CategoryID)
                .Select(g => new { CategoryID = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CategoryID!.Value, x => x.Count);

            return View(categories);
        }

        /// <summary>
        /// Creates a new category with custom visual metadata (Icon/Color). 
        /// Restricted to Workplace Admins.
        /// </summary>
        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string name, string? icon, string? color)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();
            if (CurrentMembership.RoleID > 1) return Forbid(); // Admin only

            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["ErrorMessage"] = "Category name is required.";
                return RedirectToAction(nameof(Index));
            }

            var category = new Category
            {
                WorkplaceID = CurrentWorkplace.WorkplaceID,
                Name = name.Trim(),
                Icon = icon ?? "📁",
                Color = color ?? "#6366f1"
            };

            _context.Categories.Add(category);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Category '{name}' created successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("Edit/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, string name, string? icon, string? color)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();
            if (CurrentMembership.RoleID > 1) return Forbid();

            var category = await _context.Categories
                .FirstOrDefaultAsync(c => c.CategoryID == id && c.WorkplaceID == CurrentWorkplace.WorkplaceID);

            if (category == null) return NotFound();

            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["ErrorMessage"] = "Category name is required.";
                return RedirectToAction(nameof(Index));
            }

            category.Name = name.Trim();
            category.Icon = icon ?? "📁";
            category.Color = color ?? "#6366f1";

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Category updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost("Delete/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            if (CurrentWorkplace == null || CurrentMembership == null) return Unauthorized();
            if (CurrentMembership.RoleID > 1) return Forbid();

            var category = await _context.Categories
                .FirstOrDefaultAsync(c => c.CategoryID == id && c.WorkplaceID == CurrentWorkplace.WorkplaceID);

            if (category == null) return NotFound();

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Category deleted successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}

