using Statiq.App;
using Statiq.Common;
using Statiq.Web;

await Bootstrapper.Factory
    .CreateWeb(args)
    .AddShortcode<FullUrlShortCode>("FullUrl")
    .AddProcess(ProcessTiming.Initialization,
        _ => new ProcessLauncher("npm", "install") { LogErrors = false })
    .AddProcess(ProcessTiming.AfterExecution,
        _ => new ProcessLauncher("npm", "run", "build:tailwind") { LogErrors = false, })
    .RunAsync();
