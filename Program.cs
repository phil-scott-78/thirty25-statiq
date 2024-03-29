﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using MonorailCss;
using MonorailCss.Css;
using MonorailCss.Plugins;
using MonorailCss.Plugins.Prose;
using Statiq.App;
using Statiq.Common;
using Statiq.Core;
using Statiq.Feeds;
using Statiq.Web;
using Statiq.Web.Pipelines;
using Thirty25;
using Thirty25.Statiq;
using Thirty25.Statiq.Pipelines;
using GatherHeadings = Statiq.Core.GatherHeadings;
using ISettings = MonorailCss.Plugins.ISettings;

[assembly: AspMvcPartialViewLocationFormat("~/input/{0}.cshtml")]

await Bootstrapper.Factory
    .CreateWeb(args)
    .AddSetting(Keys.Host, "thirty25.com")
    .AddSetting(Keys.Title, "Thirty25")
    .AddSetting(FeedKeys.Author, "Phil Scott")
    .AddSetting(FeedKeys.Copyright, DateTime.UtcNow.Year.ToString())
    .AddSetting(Constants.CssFile, "assets/styles.css")
    .SetOutputPath("public")
    .AddShortcode<FullUrlShortCode>("FullUrl")
    .ConfigureServices(i =>
    {
        i.AddSingleton(GetCssFramework());
    })
    .ModifyPipeline(nameof(Content), pipeline =>
    {
        // make sure to include child content like code blocks.
        pipeline.ProcessModules
            .GetLast<CacheDocuments>().Children
            .GetLast<ExecuteIf>()[0]
            .ReplaceFirst<GatherHeadings>(_ => true, new GatherHeadings(2).WithNestedElements());
        // in addition to the archive pipeline we also have the book so make sure the regular content skips those too
        pipeline.ProcessModules.ReplaceFirst<FilterDocuments>(_ => true, new FilterDocuments(Config.FromDocument(doc => !Archives.IsArchive(doc) && !Book.IsBook(doc))));
        pipeline.PostProcessModules.Add(new RoslynHighlightModule());
    })
    .RunAsync();


CssFramework GetCssFramework()
{
    var proseSettings = new Prose.Settings()
    {
        CustomSettings = designSystem => new Dictionary<string, CssSettings>()
        {
            {
                "DEFAULT", new CssSettings()
                {
                    ChildRules = new CssRuleSetList()
                    {
                        new("a",
                            new CssDeclarationList()
                            {
                                new(CssProperties.FontWeight, "inherit"),
                                new(CssProperties.TextDecoration, "none"),
                                new(CssProperties.BorderBottomWidth, "1px"),
                                new(CssProperties.BorderBottomColor,
                                    designSystem.Colors[ColorNames.Blue][ColorLevels._500].AsRgbWithOpacity("75%"))
                            })
                    }
                }
            }
        }.ToImmutableDictionary()
    };

    return new CssFramework(
        new CssFrameworkSettings()
        {
            DesignSystem = DesignSystem.Default with
            {
                Colors = DesignSystem.Default.Colors.AddRange(
                    new Dictionary<string, ImmutableDictionary<string, CssColor>>()
                    {
                        { "primary", DesignSystem.Default.Colors[ColorNames.Sky] },
                        { "base", DesignSystem.Default.Colors[ColorNames.Gray] },
                    })
            },
            PluginSettings = new List<ISettings> { proseSettings },
            Applies = new Dictionary<string, string>
            {
                { "body", "font-sans" },
                { ".token.comment,.token.prolog,.token.doctype,.token.cdata,.token.punctuation,.token.selector,.token.tag", "text-gray-300" },
                { ".token.boolean,.token.number,.token.constant,.token.attr-name,.token.deleted", "text-blue-300" },
                { ".token.string,.token.char,.token.attr-value,.token.builtin,.token.inserted", "text-green-300" },
                { ".token.operator,.token.entity,.token.url,.token.symbol,.token.class-name,.language-css .token.string,.style .token.string", "text-cyan-300" },
                { ".token.atrule,.token.keyword", "text-indigo-300" },
                { ".token.property,.token.function", "text-orange-300" },
                { ".token.regex,.token.important", "text-red-300" },
            }
        });
}
