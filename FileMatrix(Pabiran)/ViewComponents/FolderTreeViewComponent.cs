using Microsoft.AspNetCore.Mvc;

namespace FileMatrix_Pabiran_.ViewComponents
{
    public class FolderTreeViewComponent : ViewComponent
    {
        public IViewComponentResult Invoke() => View();
    }
}
