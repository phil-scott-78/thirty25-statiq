﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConcurrentCollections;
using JetBrains.Annotations;
using MonorailCss;
using Statiq.Common;
using Statiq.Core;
using Statiq.Web.Pipelines;
using IDocument = Statiq.Common.IDocument;

namespace Thirty25.Statiq.Pipelines;

[UsedImplicitly]
public class Monorail : Pipeline
{
    public Monorail()
    {
        Deployment = true;
        ExecutionPolicy = ExecutionPolicy.Normal;
        Dependencies.AddRange(
            nameof(Content),
            nameof(Data),
            nameof(Archives));

        DependencyOf.AddRange(nameof(AnalyzeContent));

        ProcessModules = new ModuleList
        {
            new ConcatDocuments(nameof(Content)),
            new ConcatDocuments(nameof(Data)),
            new ConcatDocuments(nameof(Archives)),
            new JitCss()
        };

        OutputModules = new ModuleList { new WriteFiles() };
    }
}

public class JitCss : Module
{
    protected override async Task<IEnumerable<IDocument>> ExecuteContextAsync(IExecutionContext context)
    {
        var results = new ConcurrentHashSet<string>();

        foreach (var result in await context.Inputs.ParallelSelectAsync(
                     async input => await ParseCssClassesAsync(input)))
        {
            results.AddRange(result);
        }

        var framework = context.GetService(typeof(CssFramework)) as CssFramework ?? new CssFramework();
        var style = framework.Process(results);

        return context.Inputs.Add(context.CreateDocument(context.GetString(Constants.CssFile),
            context.GetContentProvider(style, MediaTypes.Css)));
    }


    private static async Task<HashSet<string>> ParseCssClassesAsync(
        IDocument input)
    {
        var htmlDocument = await input.ParseHtmlAsync(false);
        if (htmlDocument is null)
        {
            return default;
        }

        var elements = new HashSet<string>();
        foreach (var element in htmlDocument.All.Where(x => x.ClassName != null))
        {
            var classes = element.ClassName!.Split(' ',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            elements.AddRange(classes);
        }

        return elements;
    }
}
