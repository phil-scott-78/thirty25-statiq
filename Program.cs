using System.Threading.Tasks;
using Statiq.App;
using Statiq.Web;

namespace Thirty25
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            await Bootstrapper.Factory
                .CreateWeb(args)
                .RunAsync();
        }
    }
}
