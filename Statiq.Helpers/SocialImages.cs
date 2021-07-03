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
            Dependencies.AddRange(nameof(Inputs), nameof(Data));

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

    class GenerateSocialImage : Module
    {
        protected override async Task<IEnumerable<IDocument>> ExecuteContextAsync(IExecutionContext context)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Services
                .AddRazorPages()
                .WithRazorPagesRoot("/Statiq.Helpers");

            using var app = builder.Build();
            app.MapRazorPages();
            await app.StartAsync();

            var url = app.Addresses.FirstOrDefault(u => u.StartsWith("http://"));

            using var playwright = await Playwright.CreateAsync();
            
            await using var browser = await playwright.Chromium.LaunchAsync();
            var page = await browser.NewPageAsync(new BrowserNewPageOptions
                {
                    ViewportSize = new ViewportSize { Width = 680, Height = 357 }
                }
            );

            var outputs = new List<IDocument>();
            foreach (var input in context.Inputs)
            {
                var title = input.GetString("Title");
                var description = input.GetString("Description");

                await page.GotoAsync($"{url}/SocialCard?title={title}&desc={description}");
                var bytes = await page.ScreenshotAsync();

                var destination = input.Destination.InsertSuffix("-social").ChangeExtension("png");
                // can we set this property then pull it when rendering the page?
                outputs.Add(context.CreateDocument(input.Source, destination, context.GetContentProvider(bytes)));
            }

            return outputs;
        }
    }
}
