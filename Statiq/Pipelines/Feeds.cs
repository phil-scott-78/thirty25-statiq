using System;
using JetBrains.Annotations;
using Statiq.Common;
using Statiq.Core;
using Statiq.Feeds;
using Statiq.Web.Pipelines;

namespace Thirty25.Statiq.Pipelines;

[UsedImplicitly]
public class Feeds : Pipeline
{
    public Feeds()
    {
        Dependencies.Add(nameof(Content));

        ProcessModules = new ModuleList
        {
            new ConcatDocuments(nameof(Content)),
            new FilterDocuments(Config.FromDocument(doc => !doc.GetBool("Draft"))),
            new OrderDocuments(Config.FromDocument((x => x.GetDateTime("Date")))).Descending(),
            new GenerateFeeds()
                .WithItemDescription(Config.FromDocument(doc => doc.GetString("Excerpt")))
                .WithItemPublished(Config.FromDocument(doc => (DateTime?)doc.GetDateTime("Date")))
                .WithRssPath(new NormalizedPath("rss.xml"))
                .WithAtomPath(new NormalizedPath("atom.xml"))
        };

        OutputModules = new ModuleList
        {
            new WriteFiles()
        };
    }
}
