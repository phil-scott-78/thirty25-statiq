using System.Threading.Tasks;
using Statiq.App;
using Statiq.Common;
using Statiq.Web;

namespace Thirty25
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            await Bootstrapper.Factory
                .CreateWeb(args)
                .AddProcess(ProcessTiming.Initialization,
                    _ => new ProcessLauncher("npm", "install") { LogErrors = false })
                .AddProcess(ProcessTiming.AfterExecution,
                    _ => new ProcessLauncher("npm", "run", "build:tailwind") { LogErrors = false, })
                .RunAsync();
        }
    }
}
