using System.Threading.Tasks;
using Statiq.App;
using Statiq.Highlight;
using Statiq.Web;

namespace Thirty25
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            var highlight = new HighlightCode();

            await Bootstrapper.Factory
                .CreateWeb(args)
                .RunAsync();
        }
    }
}
