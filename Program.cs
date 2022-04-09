﻿using JetBrains.Annotations;
using Statiq.App;
using Statiq.Common;
using Statiq.Web;
using Statiq.Web.Pipelines;
using Thirty25;
using Thirty25.Statiq.Helpers;

[assembly: AspMvcPartialViewLocationFormat("~/input/{0}.cshtml")]

var dotnetPath = System.Environment.GetEnvironmentVariable("DOTNET_PATH") ?? "dotnet";

await Bootstrapper.Factory
    .CreateWeb(args)
    .SetOutputPath("public")
    .AddShortcode<FullUrlShortCode>("FullUrl")
    .AddSetting(Constants.CssFile, "assets/styles.css")
    .ModifyPipeline(nameof(Content), pipeline =>
    {
        pipeline.PostProcessModules.Add(new RoslynHighlightModule());
    })
    .AddProcess(ProcessTiming.Initialization,
        _ => new ProcessLauncher(dotnetPath, "tool restore") { LogErrors = false })
    .AddProcess(ProcessTiming.Initialization,
        _ => new ProcessLauncher(dotnetPath, "tool run playwright install chromium") { LogErrors = false })
    .RunAsync();
