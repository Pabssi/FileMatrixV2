using Microsoft.AspNetCore.Mvc;

namespace FileMatrix_Pabiran_.ViewComponents
{
    public class NotificationBadgeViewComponent : ViewComponent
    {
        public IViewComponentResult Invoke() => View();
    }
}
