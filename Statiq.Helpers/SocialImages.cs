using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Statiq.Common;
using Statiq.Core;
using Statiq.Html;
using Statiq.Web;
using Statiq.Web.Modules;
using Statiq.Web.Pipelines;

namespace Thirty25.Statiq.Helpers
{
    public class SocialImages : Pipeline
    {
        public SocialImages()
        {
            Dependencies.AddRange(nameof(Inputs));

            ProcessModules = new ModuleList
            {
                new GetPipelineDocuments(ContentType.Content),

                // Filter to non-archive content
                new FilterDocuments(Config.FromDocument(doc => !Archives.IsArchive(doc))),

                // Process the content
                new CacheDocuments
                {
                    new AddTitle(),
                    new SetDestination(true),
                    new ExecuteIf(Config.FromSetting(WebKeys.OptimizeContentFileNames, true))
                    {
                        new OptimizeFileName()
                    },
                    new GenerateSocialImage(),
                }
            };

            OutputModules = new ModuleList { new WriteFiles() };
        }
    }

    internal class GenerateSocialImage : ParallelModule
    {
        private WebApplication _app;
        private IPlaywright _playwright;
        private IBrowser _browser;

        protected override async Task BeforeExecutionAsync(IExecutionContext context)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Services
                .AddRazorPages()
                .WithRazorPagesRoot("/Statiq.Helpers");

            _app = builder.Build();
            _app.MapRazorPages();
            await _app.StartAsync();

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync();

            await base.BeforeExecutionAsync(context);
        }

        protected override async Task FinallyAsync(IExecutionContext context)
        {
            await _browser.DisposeAsync();
            _playwright.Dispose();
            await _app.DisposeAsync();
            await base.FinallyAsync(context);
        }

        protected override async Task<IEnumerable<IDocument>> ExecuteInputAsync(IDocument input,
            IExecutionContext context)
        {
            var url = _app.Urls.FirstOrDefault(u => u.StartsWith("http://"));
            var page = await _browser.NewPageAsync(new BrowserNewPageOptions
                {
                    ViewportSize = new ViewportSize { Width = 1200, Height = 628 }
                }
            );

            var title = input.GetString("Title");
            var description = input.GetString("Description");
            var tags = input.GetList<string>("tags") ?? Array.Empty<string>();

            await page.GotoAsync($"{url}/SocialCard?title={title}&desc={description}&tags={string.Join(';', tags)}");
            var bytes = await page.ScreenshotAsync();

            var destination = input.Destination.InsertSuffix("-social").ChangeExtension("png");
            // can we set this property then pull it when rendering the page?
            var doc = context.CreateDocument(
                input.Source,
                destination,
                new MetadataItems { { "DocId", input.Id } },
                context.GetContentProvider(bytes));

            return new[] { doc };
        }
    }
}
