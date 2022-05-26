using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Statiq.Common;
using Statiq.Core;
using Statiq.Web.Pipelines;

namespace Thirty25.Statiq.Helpers;

public class Book : Pipeline
{
    public Book()
    {
        Deployment = true;
        ExecutionPolicy = ExecutionPolicy.Normal;
        Dependencies.AddRange(
            nameof(Content));

        ProcessModules = new ModuleList
        {
            new PrintBook()
        };

        OutputModules = new ModuleList { new WriteFiles() };
    }
}

public class PrintBook : Module
{
    protected override async Task<IEnumerable<IDocument>> ExecuteContextAsync(IExecutionContext ctx)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        await using var app = builder.Build();
        var root = Path.Combine(Directory.GetCurrentDirectory(), @"public");
        app.UseStaticFiles(new StaticFileOptions()
        {
            FileProvider = new PhysicalFileProvider(root),
            RequestPath = "/static"
        });
        await app.StartAsync();

        using var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync();
        var browserContext = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1200, Height = 628 },
        });

        var page = await browserContext.NewPageAsync();
        var url = app.Urls.First(u => u.StartsWith("http://"));
        await page.GotoAsync($"{url}/static/book.html", new PageGotoOptions()
        {
            WaitUntil = WaitUntilState.NetworkIdle
        });

        var pdf = await page.PdfAsync();

        await using var contentStream = ctx.GetContentStream();
        await contentStream.WriteAsync(pdf);
        return new[] { ctx.CreateDocument("book.pdf", ctx.GetContentProvider(contentStream, "application/pdf")) };
    }
}
