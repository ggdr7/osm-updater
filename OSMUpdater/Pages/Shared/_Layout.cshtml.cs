using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OsmUpdateUtility.Pages.Shared
{
    [Authorize]
    public class _LayoutModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
