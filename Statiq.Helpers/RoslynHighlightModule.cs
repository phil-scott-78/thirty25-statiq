using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Text;
using NetFabric.Hyperlinq;
using Statiq.Common;
using Statiq.Core;
using Statiq.Web.Pipelines;
using Task = System.Threading.Tasks.Task;

namespace Thirty25.Statiq.Helpers;

public class RoslynHighlightModule : Module
{
    private string _codeQuerySelector = "pre code.language-csharp";

    /// <summary>
    /// Sets the query selector to use to find code blocks.
    /// </summary>
    /// <param name="querySelector">The query selector to use to select code blocks. The default value is pre code</param>
    /// <returns>The current instance.</returns>
    public RoslynHighlightModule WithCodeQuerySelector(string querySelector)
    {
        _codeQuerySelector = querySelector;
        return this;
    }

    protected override async Task<IEnumerable<IDocument>> ExecuteContextAsync(IExecutionContext context)
    {
        IEnumerable<IDocument> results = await context.Inputs.ParallelSelectAsync(async input =>
        {
            var htmlDocument = await input.ParseHtmlAsync();
            var highlighted = false;
            foreach (var element in htmlDocument.QuerySelectorAll(_codeQuerySelector))
            {
                // Don't highlight anything that potentially is already highlighted
                if (element.ClassList.Contains("hljs"))
                {
                    continue;
                }

                highlighted = true;
                await HighlightElement(element);
            }

            return highlighted ? input.Clone(context.GetContentProvider(htmlDocument)) : input;
        });

        return results.ToList();
    }


    internal static async Task HighlightElement(AngleSharp.Dom.IElement element)
    {
        var original = element.TextContent;
        original = WebUtility.HtmlDecode(original);
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var project = solution.AddProject("projectName", "assemblyName", LanguageNames.CSharp);
        var document = project.AddDocument("name.cs", original);
        var text = await document.GetTextAsync();
        var classifiedSpans = await Classifier.GetClassifiedSpansAsync(document, TextSpan.FromBounds(0, text.Length));
        var ranges = classifiedSpans.Select(classifiedSpan =>
            new Range(classifiedSpan, text.GetSubText(classifiedSpan.TextSpan).ToString()));
        ranges = FillGaps(text, ranges);

        var sb = new StringBuilder(element.TextContent.Length);

        foreach (var range in ranges)
        {
            var cssClass = ClassificationTypeToHljs(range.ClassificationType);
            if (string.IsNullOrWhiteSpace(cssClass))
            {
                sb.Append(range.Text);
            }
            else
            {
                sb.Append($"<span class=\"token {cssClass}\">{range.Text}</span>");
            }
        }

        element.ClassList.Add("hljs");
        element.InnerHtml = sb.ToString();
    }

    private static string ClassificationTypeToHljs(string rangeClassificationType)
    {
        if (rangeClassificationType == null)
            return string.Empty;
        
        switch (rangeClassificationType)
        {
            case ClassificationTypeNames.Identifier:
                return "symbol";
            case ClassificationTypeNames.ClassName:
            case ClassificationTypeNames.StructName:
            case ClassificationTypeNames.RecordClassName:
            case ClassificationTypeNames.RecordStructName:
            case ClassificationTypeNames.InterfaceName:
            case ClassificationTypeNames.DelegateName:
            case ClassificationTypeNames.EnumName:
            case ClassificationTypeNames.ModuleName:
            case ClassificationTypeNames.TypeParameterName:
                return "title.class";
            case ClassificationTypeNames.MethodName:
                return "title.function";
            case ClassificationTypeNames.Comment:
                return "comment";
            case ClassificationTypeNames.Keyword:
            case ClassificationTypeNames.ControlKeyword:
                return "keyword";
            case ClassificationTypeNames.StringLiteral:
            case ClassificationTypeNames.VerbatimStringLiteral:
                return "string";
            case ClassificationTypeNames.NumericLiteral:
                return "number";
            case ClassificationTypeNames.Operator:
                return "operator";
            case ClassificationTypeNames.Punctuation:
                return "punctuation";
            default:
                return rangeClassificationType.Replace(" ", "-");
        }
    }

    private static IEnumerable<Range> FillGaps(SourceText text, IEnumerable<Range> ranges)
    {
        const string WhitespaceClassification = null;
        var current = 0;
        Range previous = null;

        foreach (var range in ranges)
        {
            var start = range.TextSpan.Start;
            if (start > current)
            {
                yield return new Range(WhitespaceClassification, TextSpan.FromBounds(current, start), text);
            }

            if (previous == null || range.TextSpan != previous.TextSpan)
            {
                yield return range;
            }

            previous = range;
            current = range.TextSpan.End;
        }

        if (current < text.Length)
        {
            yield return new Range(WhitespaceClassification, TextSpan.FromBounds(current, text.Length), text);
        }
    }

    public class Range
    {
        public ClassifiedSpan ClassifiedSpan { get; private set; }
        public string Text { get; private set; }

        public Range(string classification, TextSpan span, SourceText text) :
            this(classification, span, text.GetSubText(span).ToString())
        {
        }

        public Range(string classification, TextSpan span, string text) :
            this(new ClassifiedSpan(classification, span), text)
        {
        }

        public Range(ClassifiedSpan classifiedSpan, string text)
        {
            ClassifiedSpan = classifiedSpan;
            Text = text;
        }

        public string ClassificationType => ClassifiedSpan.ClassificationType;

        public TextSpan TextSpan => ClassifiedSpan.TextSpan;
    }
}
