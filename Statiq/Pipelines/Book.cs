using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Statiq.Common;
using Statiq.Core;
using Statiq.Web;
using Statiq.Web.Modules;
using Statiq.Web.Pipelines;
using IDocument = Statiq.Common.IDocument;

namespace Thirty25.Statiq.Pipelines;

[UsedImplicitly]
public class Book : Pipeline
{
    public Book(Templates templates)
    {
        ExecutionPolicy = ExecutionPolicy.Manual;
        Dependencies.AddRange(nameof(Inputs), nameof(Content), nameof(Data), nameof(Assets));

        ProcessModules = new ModuleList
        {
            new GetPipelineDocuments(Config.FromDocument(doc =>
                doc.Get<ContentType>(WebKeys.ContentType) != ContentType.Asset ||
                doc.MediaTypeEquals(MediaTypes.CSharp))),
            new FilterDocuments(Config.FromDocument(IsBook)),
            new ForEachDocument
            {
                new ExecuteConfig(Config.FromDocument((bookDoc, _) =>
                {
                    var modules = new ModuleList
                    {
                        new ReplaceDocuments(bookDoc.GetList(BookKeys.BookPipelines, new[] { nameof(Content) })
                            .ToArray()),
                        new MergeMetadata(Config.FromValue(bookDoc.Yield())).KeepExisting(),
                        // we are gonna roll up all the pages into one so any relative link
                        // will be invalid so we need them to be absolute.
                        new MakeLinksAbsolute(),
                        new ProcessHtml("a[\"href\"]", link =>
                        {
                            // printed content so we don't want regular links.
                            // if the link is a simple url converted to a link with the same href
                            // as the text we can just drop the href and keep the text.
                            // otherwise we want to convert it to a footnote.
                            var href = link.GetAttribute("href");

                            if (link.TextContent == href)
                            {
                                link.RemoveAttribute("href");
                            }
                            else
                            {
                                // replace the link with a footnote.
                                var parser = new HtmlParser();
                                var document = parser.ParseFragment(
                                    $"<span class=\"link\">{link.TextContent} <span class=\"footnote\">{href}</span></span>",
                                    link.ParentElement!);
                                link.Replace(document.ToArray());
                            }
                        })
                    };

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
                    modules.Add(new CacheDocuments { new RenderContentProcessTemplates(templates) });

                    return modules;
                }))
            },
        };

        PostProcessModules = new ModuleList { new RenderContentPostProcessTemplates(templates) };

        OutputModules = new ModuleList
        {
            new FilterDocuments(Config.FromDocument(WebKeys.ShouldOutput, true)), new HtmlToPdf(), new WriteFiles()
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
        var content = await input.GetContentStringAsync();
        context.Logger.Log(LogLevel.Information, input, "Setting content");
        await page.SetContentAsync(content);
        context.Logger.Log(LogLevel.Information, input, "Waiting for request");

        await page.WaitForConsoleMessageAsync(new PageWaitForConsoleMessageOptions()
        {
            Predicate = message => message.Text == "after render"
        });

        context.Logger.Log(LogLevel.Information, input, "Writing PDF");
        var pdf = await page.PdfAsync(new PagePdfOptions()
        {
            Margin = new Margin() { Bottom = "0", Left = "0", Right = "0", Top = "0" },
            DisplayHeaderFooter = false,
            PrintBackground = true,
            PreferCSSPageSize = true,
        });

        await using var contentStream = context.GetContentStream();
        await contentStream.WriteAsync(pdf);
        return new[]
        {
            context.CreateDocument(input.Destination.ChangeExtension("pdf"),
                context.GetContentProvider(contentStream, "application/pdf"))
        };
    }
}

public static class BookKeys
{
    public static string BookOrderKey = nameof(BookOrderKey);
    public static string BookOrderDescending = nameof(BookOrderDescending);
    public static string BookSources = nameof(BookSources);
    public static string BookPipelines = nameof(BookPipelines);
}

/// <summary>
/// Queries HTML content of the input documents and adds a metadata value that contains it's headings.
/// </summary>
/// <remarks>
/// A new document is created for each heading, all of which are placed into a <c>IReadOnlyList&lt;IDocument&gt;</c>
/// in the metadata of each input document. The new heading documents contain metadata with the level of the heading,
/// the children of the heading (the following headings with one deeper level) and optionally the heading content, which
/// is also set as the content of each document. The output of this module is the input documents with the additional
/// metadata value containing the documents that present each heading.
/// </remarks>
/// <metadata cref="Keys.Headings" usage="Output"/>
/// <metadata cref="Keys.Level" usage="Output"/>
/// <metadata cref="Keys.HeadingId" usage="Output"/>
/// <metadata cref="Keys.Children" usage="Output">
/// The child heading documents of the current heading document.
/// </metadata>
/// <category name="Metadata" />
public class GatherHeadings : ParallelConfigModule<int>
{
    private bool _nesting;
    private bool _withNestedElements;
    private string _metadataKey = Keys.Headings;
    private string _levelKey = Keys.Level;
    private string _idKey = Keys.HeadingId;
    private string _childrenKey = Keys.Children;
    private string _headingKey;

    public GatherHeadings()
        : this(1)
    {
    }

    public GatherHeadings(Config<int> level)
        : base(level, true)
    {
    }

    /// <summary>
    /// Includes nested HTML elements in the heading content (the default is <c>false</c>).
    /// </summary>
    /// <param name="nestedElements"><c>true</c> to include nested elements, <c>false</c> otherwise.</param>
    /// <returns>The current module instance.</returns>
    public GatherHeadings WithNestedElements(bool nestedElements = true)
    {
        _withNestedElements = true;
        return this;
    }

    /// <summary>
    /// Sets the key to use in the heading documents to store the level.
    /// </summary>
    /// <param name="levelKey">The key to use for the level.</param>
    /// <returns>The current module instance.</returns>
    public GatherHeadings WithLevelKey(string levelKey)
    {
        _levelKey = levelKey;
        return this;
    }

    /// <summary>
    /// Sets the key to use in the heading documents to store the heading
    /// <c>id</c> attribute (if it has one).
    /// </summary>
    /// <param name="idKey">The key to use for the <c>id</c>.</param>
    /// <returns>The current module instance.</returns>
    public GatherHeadings WithIdKey(string idKey)
    {
        _idKey = idKey;
        return this;
    }

    /// <summary>
    /// Sets the key to use in the heading documents to store the children
    /// of a given heading. In other words, the metadata for this key will
    /// contain all the headings following the one in the document with a
    /// level one deeper than the current heading.
    /// </summary>
    /// <param name="childrenKey">The key to use for children.</param>
    /// <returns>The current module instance.</returns>
    public GatherHeadings WithChildrenKey(string childrenKey)
    {
        _childrenKey = childrenKey;
        return this;
    }

    /// <summary>
    /// Sets the key to use for storing the heading content in the heading documents.
    /// The default is <c>null</c> which means only store the heading content in the
    /// content of the heading document. Setting this can be useful when you want
    /// to use the heading documents in downstream modules, setting their content
    /// to something else while maintaining the heading content in metadata.
    /// </summary>
    /// <param name="headingKey">The key to use for the heading content.</param>
    /// <returns>The current module instance.</returns>
    public GatherHeadings WithHeadingKey(string headingKey)
    {
        _headingKey = headingKey;
        return this;
    }

    /// <summary>
    /// Controls whether the heading documents are nested. If nesting is
    /// used, only the level 1 headings will be in the root set of documents.
    /// The rest of the heading documents will only be accessible via the
    /// metadata of the root heading documents.
    /// </summary>
    /// <param name="nesting"><c>true</c> to turn on nesting.</param>
    /// <returns>The current module instance.</returns>
    public GatherHeadings WithNesting(bool nesting = true)
    {
        _nesting = true;
        return this;
    }

    /// <summary>
    /// Allows you to specify an alternate metadata key for the heading documents.
    /// </summary>
    /// <param name="metadataKey">The metadata key to store the heading documents in.</param>
    /// <returns>The current module instance.</returns>
    public GatherHeadings WithMetadataKey(string metadataKey)
    {
        _metadataKey = metadataKey;
        return this;
    }

    protected override async Task<IEnumerable<IDocument>> ExecuteConfigAsync(IDocument input,
        IExecutionContext context, int value)
    {
        // Return the original document if no metadata key
        if (string.IsNullOrWhiteSpace(_metadataKey))
        {
            return input.Yield();
        }

        // Parse the HTML content
        IHtmlDocument htmlDocument = await input.ParseHtmlAsync(false);
        if (htmlDocument is null)
        {
            return input.Yield();
        }

        // Validate the level
        if (value < 1)
        {
            throw new ArgumentException("Heading level cannot be less than 1");
        }

        if (value > 6)
        {
            throw new ArgumentException("Heading level cannot be greater than 6");
        }

        // Evaluate the query and create the holding nodes
        Heading previousHeading = null;
        var headings = htmlDocument
            .QuerySelectorAll(GetHeadingQuery(value))
            .Select(x =>
            {
                previousHeading = new Heading
                {
                    Element = x, Previous = previousHeading, Level = int.Parse(x.NodeName.Substring(1))
                };
                return previousHeading;
            })
            .ToList();

        // Build the tree from the bottom-up
        for (var level = value; level >= 1; level--)
        {
            var currentLevel = level;
            foreach (var heading in headings.Where(x => x.Level == currentLevel))
            {
                // Get the parent
                Heading parent = null;
                if (currentLevel > 1)
                {
                    parent = heading.Previous;
                    while (parent is not null && parent.Level >= currentLevel)
                    {
                        parent = parent.Previous;
                    }
                }

                // Create the document
                var metadata = new MetadataItems();
                var content = _withNestedElements
                    ? heading.Element.TextContent
                    : string.Join(
                            string.Empty,
                            heading.Element.ChildNodes
                                .Select(x =>
                                {
                                    if (x is IText text)
                                    {
                                        return text.Text;
                                    }

                                    return string.Join(
                                        string.Empty,
                                        x.ChildNodes.OfType<IText>().Select(t => t.Text));
                                })
                                .Where(x => !x.IsNullOrEmpty()))
                        .Trim();
                if (_levelKey is not null)
                {
                    metadata.Add(_levelKey, heading.Level);
                }

                if (_idKey is not null && heading.Element.HasAttribute("id"))
                {
                    metadata.Add(_idKey, heading.Element.GetAttribute("id"));
                }

                if (_headingKey is not null)
                {
                    metadata.Add(_headingKey, content);
                }

                if (_childrenKey is not null)
                {
                    metadata.Add(_childrenKey, heading.Children.AsReadOnly());
                }

                heading.Document = context.CreateDocument(metadata, content);

                // Add to parent
                parent?.Children.Add(heading.Document);
            }
        }

        return input
            .Clone(new MetadataItems
            {
                {
                    _metadataKey, _nesting
                        ? headings
                            .Where(x => x.Level == headings.Min(y => y.Level))
                            .Select(x => x.Document)
                            .ToArray()
                        : headings
                            .Select(x => x.Document)
                            .ToArray()
                }
            })
            .Yield();
    }

    private static string GetHeadingQuery(int level)
    {
        var query = new StringBuilder();
        for (var l = 1; l <= level; l++)
        {
            if (l > 1)
            {
                query.Append(",");
            }

            query.Append("h");
            query.Append(l);
        }

        return query.ToString();
    }

    private class Heading
    {
        public IElement Element { get; set; }
        public Heading Previous { get; set; }
        public int Level { get; set; }
        public IDocument Document { get; set; }
        public List<IDocument> Children { get; } = new List<IDocument>();
    }
}
