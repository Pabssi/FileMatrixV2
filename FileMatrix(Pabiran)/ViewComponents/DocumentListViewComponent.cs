using Microsoft.AspNetCore.Mvc;

namespace FileMatrix_Pabiran_.ViewComponents
{
    public class DocumentListViewComponent : ViewComponent
    {
        public IViewComponentResult Invoke() => View();
    }
}
