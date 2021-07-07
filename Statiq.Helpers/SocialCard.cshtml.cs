using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace Thirty25.Statiq.Helpers
{
    public class SocialCardModel : PageModel
    {
        [BindProperty(Name = "title", SupportsGet = true)]
        public string Title { get; set; }

        [BindProperty(Name = "desc", SupportsGet = true)]
        public string Description { get; set; }
        
        [BindProperty(Name = "tags", SupportsGet = true)]
        public string Tags { get; set; }

        private readonly ILogger<SocialCardModel> _logger;

        public SocialCardModel(ILogger<SocialCardModel> logger)
        {
            _logger = logger;
        }

        public void OnGet()
        {
        }
    }
}
