---
title: Adding Configuration to an Incremental Generator
description: Using AnalyzerConfigOptionsProvider and GlobalAnalyzerConfigFiles to configure an .NET Incremental Generator
date: 2022-05-12
tags:

- Source Generators

---

[Incremental Generators](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md) solve a lot
of problems with performance of source generators. They add a layer of caching around the generator which drastically
improves performance. But to achieve this performance you need to play within a certain set of rules.

We must use `IncrementalGeneratorInitializationContext` to pull in our data via pipelines. By routing everything through
this object, the compiler can keep track of values used between builds and cache them. It has the follow providers:

* CompilationProvider
* AdditionalTextsProvider
* AnalyzerConfigOptionsProvider
* MetadataReferencesProvider
* ParseOptionsProvider

Take this initializer from a generator. We are using the `AdditionalTextsProvider` to find all the razor files in a
project and extract CSS classes from them via a regular expression. We'll use `AdditionalTextsProvider` to access the
razor files. Because
we are accessing these files via this property, the compiler can keep track of which ones we are accessing and cache the
transforms.

```csharp
public void Initialize(IncrementalGeneratorInitializationContext context)
{
    var cssClasses = context.AdditionalTextsProvider
        .Where(static value => value.Path.EndsWith(".cshtml") || value.Path.EndsWith(".razor"))
        .Select(static (value, token) => value.GetText(token)!.ToString())
        .Select(static (value, _) =>  Helpers.GetCssClassFromHtml(value, @"(class\s*=\s*[\'\""](?<value>[^<]*?)[\'\""])"));

    context.RegisterSourceOutput(cssClasses, static (spc, source) => Execute(source, spc));
}
```

In this example we are accessing files with the extension `.cshtml` and `.razor`., extracting their full text and then
using a regular expression to parse out all the css classes. In a regular source generator this would be madness. We'd
have to parse every file constantly, dramatically slowing down the IDE. The caching of the incremental generator,
however, will make sure we only parse when the file changes. The are two obvious things that we want to configure - the
file extensions our generator cares about as well as the regular expression. An end user might also have some html files
they want to include, or maybe they have some custom components that use `cssclass` as the attribute.

To configure our value provider we can use `AnalyzerConfigOptionsProvider`. This works like the value provider for
AdditionalContext, but instead of looking at the files it looks at the config.

```csharp
var config = context.AnalyzerConfigOptionsProvider.Select((provider, _) =>
{
    var regex = @"(class\s*=\s*[\'\""](?<value>[^<]*?)[\'\""])";
    var additionalFileFilter = new[] { ".cshtml", ".razor" };

    if (provider.GlobalOptions.TryGetValue("parser_regex", out var configValue))
        regex = configValue;

    if (provider.GlobalOptions.TryGetValue("parser_filter", out var fileFilter))
        additionalFileFilter = fileFilter.Split('|');

    return (Regex: regex, Filter: additionalFileFilter);
});
```

Here we are using the `TryGetValue` method to pull the two configuration values out and if we don't find them we'll fall
back to our defaults.

We now need to adjust our original value provider to use these values. We'll
use [`Combine`](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md#combine) to merge the
two providers together. This will create a tuple with a left and right side. The left side, the one calling combine,
will be the original value.
The right side will have the configuration.

Add just the combine call makes our `cssClasses` value provider now looks like this:

```csharp
var cssClasses = context.AdditionalTextsProvider
    .Combine(config)
    .Where(static value => value.Left.Path.EndsWith(".cshtml") || value.Left.Path.EndsWith(".razor"))
    .Select(static (value, token) => value.Left.GetText(token)!.ToString())
    .Select(static (value, _) =>  Helpers.GetCssClassFromHtml(value, @"(class\s*=\s*[\'\""](?<value>[^<]*?)[\'\""])"));
```

Note that now we are access `value.Left` to get at the `AdditionalTextProvider` values. `value.Right` contains our
configuration tuple.

We can now adjust our call to use that tuple instead.

```csharp
var cssClasses = context.AdditionalTextsProvider
    .Combine(config)
    .Where(static value => value.Right.Filter.Any(i => value.Left.Path.EndsWith(i)))
    .Select(static (value, token) => (Config: value.Right, Content: value.Left.GetText(token)!.ToString()))
    .Select(static (value, _) =>  Helpers.GetCssClassFromHtml(value.Content, value.Config.Regex));
```

Note that in the first `Select` we are creating a new tuple that passes the content and also the config along with it to
be used in the final call.

So we are now using a configuration, time to actually do the configuration.

For this global option we'll use
a [global AnalyzerConfig file](https://docs.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-files#global-analyzerconfig)
. file. These files look like an `.editorconfig` file, but everything is
at the top level. The preferred naming convention is `generatorname.globalconfig`. E.g. if our generator was
named `CssClassGenerator` our file name would be
`cssclassgenerator.globalconfig`

For example, if we wanted to configure our regex to look for `class` and `cssclass` tags our config

```text
is_global = true

parser_regex = (class\s*=\s*[\'\"](?<value>[^<]*?)[\'\"])|(cssclass\s*=\s*[\'\"](?<value>[^<]*?)[\'\"])
```

We'll put this file in the root of our project. But we still aren't done! Our last step is to tell msbuild about it.
This requires adding a new element named `GlobalAnalyzerConfigFiles`to provide the value.

```xml
<ItemGroup>
    <GlobalAnalyzerConfigFiles Include="cssclassgenerator.globalconfig"/>
</ItemGroup>
```

Once this is all in place you can finally build and see our configured values in place!