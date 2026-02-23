using Microsoft.AspNetCore.Mvc;

namespace FileMatrix_Pabiran_.ViewComponents
{
    public class RecentDocumentsViewComponent : ViewComponent
    {
        public IViewComponentResult Invoke() => View();
    }
}
