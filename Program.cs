using System;
using JetBrains.Annotations;
using Statiq.App;
using Statiq.Common;
using Statiq.Feeds;
using Statiq.Web;
using Statiq.Web.Pipelines;
using Thirty25.Statiq.Helpers;

[assembly: AspMvcPartialViewLocationFormat("~/input/{0}.cshtml")]

var dotnetPath = System.Environment.GetEnvironmentVariable("DOTNET_PATH") ?? "dotnet";

await Bootstrapper.Factory
    .CreateWeb(args)
    .AddSetting(Keys.Host, "thirty25.com")
    .AddSetting(Keys.Title, "Thirty25")
    .AddSetting(FeedKeys.Author, "Phil Scott")
    .AddSetting(FeedKeys.Copyright, DateTime.UtcNow.Year.ToString())
    .SetOutputPath("public")
    .AddShortcode<FullUrlShortCode>("FullUrl")
    .ModifyPipeline(nameof(Content), pipeline =>
    {
        pipeline.PostProcessModules.Add(new RoslynHighlightModule());
    })
    .AddProcess(ProcessTiming.Initialization,
        _ => new ProcessLauncher("npm", "install") { LogErrors = false })
    .AddProcess(ProcessTiming.Initialization,
        _ => new ProcessLauncher(dotnetPath, "tool restore") { LogErrors = false })
    .AddProcess(ProcessTiming.Initialization,
        _ => new ProcessLauncher(dotnetPath, "tool run playwright install chromium") { LogErrors = false })
    .AddProcess(ProcessTiming.AfterExecution,
        _ => new ProcessLauncher("npm", "run", "build:tailwind") { LogErrors = false, })
    .RunAsync();
