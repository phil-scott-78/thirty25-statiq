using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Statiq.Common;
using Statiq.Core;
using Statiq.Web;
using Statiq.Web.Modules;
using Statiq.Web.Pipelines;

namespace Thirty25.Statiq.Helpers;

public class Book : Pipeline
{
    public Book(Templates templates)
    {
        ExecutionPolicy = ExecutionPolicy.Manual;
        Dependencies.AddRange(nameof(Inputs), nameof(Content), nameof(Data));

        ProcessModules = new ModuleList
        {
            new GetPipelineDocuments(Config.FromDocument(doc =>
                doc.Get<ContentType>(WebKeys.ContentType) != ContentType.Asset ||
                doc.MediaTypeEquals(MediaTypes.CSharp))),
            new FilterDocuments(Config.FromDocument(IsBook)),
            new ForEachDocument
            {
                new ExecuteConfig(Config.FromDocument((bookDoc, ctx) =>
                {
                    var modules = new ModuleList();
                    modules.Add(new ReplaceDocuments(bookDoc.GetList(BookKeys.BookPipelines, new[] { nameof(Content) })
                        .ToArray()));
                    modules.Add(new MergeMetadata(Config.FromValue(bookDoc.Yield())).KeepExisting());
                    modules.Add(new MakeLinksAbsolute());
                    modules.Add(new ProcessHtml("a[\"href\"]", link =>
                    {
                        var href = link.GetAttribute("href");

                        if (link.TextContent == href)
                        {
                            link.RemoveAttribute("href");
                        }
                        else
                        {
                            var parser = new HtmlParser();
                            var document = parser.ParseFragment($"<span class=\"link\">{link.TextContent} <span class=\"footnote\">{href}</span></span>", link.ParentElement!);
                            link.Replace(document.ToArray());
                        }
                    }));

                    // Filter by document source
                    if (bookDoc.ContainsKey(BookKeys.BookSources))
                    {
                        modules.Add(new FilterSources(bookDoc.GetList<string>(BookKeys.BookSources)));
                    }

                    // Order the documents
                    if (bookDoc.ContainsKey(BookKeys.BookOrderKey))
                    {
                        modules.Add(
                            new OrderDocuments(bookDoc.GetString(BookKeys.BookOrderKey))
                                .Descending(bookDoc.GetBool(BookKeys.BookOrderDescending)));
                    }

                    modules.Add(new ExecuteIf(Config.FromContext(ctx => ctx.Inputs.Length > 0),
                        GetTopLevelIndexModules(bookDoc)));

                    // If it's a script, evaluate it now (deferred from inputs pipeline)
                    modules.Add(new ProcessScripts(false));

                    // Now execute templates
                    modules.Add(
                        new ExecuteSwitch(Config.FromDocument(doc => doc.Get<ContentType>(WebKeys.ContentType)))
                            .Case(ContentType.Data, templates.GetModule(ContentType.Data, Phase.Process))
                            .Case(
                                ContentType.Content,
                                (IModule)new CacheDocuments { new RenderContentProcessTemplates(templates) }));

                    return modules;
                }))
            },
        };

        PostProcessModules = new ModuleList
        {
            new ExecuteSwitch(Config.FromDocument(doc => doc.Get<ContentType>(WebKeys.ContentType)))
                .Case(ContentType.Data, templates.GetModule(ContentType.Data, Phase.PostProcess))
                .Case(ContentType.Content, (IModule)new RenderContentPostProcessTemplates(templates))
        };

        OutputModules = new ModuleList
        {
            new FilterDocuments(Config.FromDocument(WebKeys.ShouldOutput, true)),
            new HtmlToPdf(),
            new WriteFiles()
        };
    }

    private static IModule[] GetTopLevelIndexModules(IDocument bookDoc) => new IModule[]
    {
        new ReplaceDocuments(Config.FromContext(ctx =>
            bookDoc.Clone(new MetadataItems { { Keys.Children, ctx.Inputs } }).Yield())),
        new AddTitle(),
        new SetDestination(Config.FromSettings(s =>
            bookDoc.Destination.ChangeExtension(s.GetPageFileExtensions()[0])))
    };

    public static bool IsBook(IDocument document) =>
        document.ContainsKey(BookKeys.BookPipelines) || document.ContainsKey(BookKeys.BookSources);
}

public class HtmlToPdf : Module
{
    protected override async Task<IEnumerable<IDocument>> ExecuteInputAsync(IDocument input, IExecutionContext context)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        await using var app = builder.Build();
        var root = Path.Combine(Directory.GetCurrentDirectory(), @"public");
        app.UseStaticFiles(new StaticFileOptions()
        {
            FileProvider = new PhysicalFileProvider(root), RequestPath = "/static"
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
        var content = await input.GetContentStringAsync();
        await page.SetContentAsync(content);
        await page.WaitForRequestFinishedAsync();
        var pdf = await page.PdfAsync();
        
        await using var contentStream = context.GetContentStream();
        await contentStream.WriteAsync(pdf);
        return new[] { context.CreateDocument(input.Destination.ChangeExtension("pdf"), context.GetContentProvider(contentStream, "application/pdf")) };
    }
}

public static class BookKeys
{
    public static string BookOrderKey = nameof(BookOrderKey);
    public static string BookOrderDescending = nameof(BookOrderDescending);
    public static string BookSources = nameof(BookSources);

    public static string BookPipelines = nameof(BookPipelines);
}
