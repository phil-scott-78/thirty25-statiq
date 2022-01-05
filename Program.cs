using Statiq.App;
using Statiq.Common;
using Statiq.Web;

var dotnetPath = System.Environment.GetEnvironmentVariable("DOTNET_PATH") ?? "dotnet";

await Bootstrapper.Factory
    .CreateWeb(args)
    .SetOutputPath("public")
    .AddShortcode<FullUrlShortCode>("FullUrl")
    .AddProcess(ProcessTiming.Initialization,
        _ => new ProcessLauncher("npm", "install") { LogErrors = false })
    .AddProcess(ProcessTiming.Initialization,
        _ => new ProcessLauncher(dotnetPath, "tool restore") { LogErrors = false })
    .AddProcess(ProcessTiming.Initialization,
        _ => new ProcessLauncher(dotnetPath, "tool run playwright install chromium") { LogErrors = false })
    .AddProcess(ProcessTiming.AfterExecution,
        _ => new ProcessLauncher("npm", "run", "build:tailwind") { LogErrors = false, })
    .RunAsync();
