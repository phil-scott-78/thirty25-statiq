using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using AngleSharp.Dom;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Text;
using Statiq.Common;
using IDocument = Statiq.Common.IDocument;
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
        // we can reuse the workspace, solution and project across all the documents and their children.
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var project = solution.AddProject("projectName", "assemblyName", LanguageNames.CSharp);

        var results = await context.Inputs.ParallelSelectAsync(async input =>
        {
            var htmlDocument = await input.ParseHtmlAsync();
            var highlighted = false;
            foreach (var element in htmlDocument.QuerySelectorAll(_codeQuerySelector))
            {
                highlighted = true;
                await HighlightElement(element, project);
            }

            return highlighted ? input.Clone(context.GetContentProvider(htmlDocument)) : input;
        });

        return results.ToList();
    }


    private static async Task HighlightElement(IElement element, Project project)
    {
        // markdig will html escape our code for html, let's decode it.
        var original = element.TextContent;
        original = WebUtility.HtmlDecode(original);

        // we'll be running concurrently here so create a randomish name so we don't have a collision.
        var filename = $"name.{original.GetHashCode()}.{Environment.CurrentManagedThreadId}.cs";
        var document = project.AddDocument(filename, original);
        var text = await document.GetTextAsync();
        var classifiedSpans = await Classifier.GetClassifiedSpansAsync(document, TextSpan.FromBounds(0, text.Length));
        var ranges = classifiedSpans.Select(classifiedSpan => new Range(classifiedSpan, text.GetSubText(classifiedSpan.TextSpan).ToString()));

        // the classified text won't include the whitespace so we need to add to fill in those gaps.
        ranges = FillGaps(text, ranges);

        var sb = new StringBuilder(element.TextContent.Length);

        foreach (var range in ranges)
        {
            var cssClass = ClassificationTypeToPrismClass(range.ClassificationType);
            if (string.IsNullOrWhiteSpace(cssClass))
            {
                sb.Append(range.Text);
            }
            else
            {
                // include the prism css class but also include the roslyn classification.
                sb.Append($"<span class=\"token {cssClass} roslyn-{range.ClassificationType.Replace(" ", "-")}\">{range.Text}</span>");
            }
        }

        // if prism.js runs client side we want it to skip this one, so mark it as language-none and remove the csharp identifier.
        element.ClassList.Remove("language-csharp");
        element.ClassList.Add("language-none");
        element.InnerHtml = sb.ToString();
    }

    private static string ClassificationTypeToPrismClass(string rangeClassificationType)
    {
        if (rangeClassificationType == null)
            return string.Empty;

        switch (rangeClassificationType)
        {
            case ClassificationTypeNames.Identifier:
                return "symbol";
            case ClassificationTypeNames.LocalName:
                return "variable";
            case ClassificationTypeNames.ParameterName:
            case ClassificationTypeNames.PropertyName:
            case ClassificationTypeNames.EnumMemberName:
            case ClassificationTypeNames.FieldName:
                return "property";
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
            case ClassificationTypeNames.ExtensionMethodName:
                return "title.function";
            case ClassificationTypeNames.Comment:
                return "comment";
            case ClassificationTypeNames.Keyword:
            case ClassificationTypeNames.ControlKeyword:
            case ClassificationTypeNames.PreprocessorKeyword:
                return "keyword";
            case ClassificationTypeNames.StringLiteral:
            case ClassificationTypeNames.VerbatimStringLiteral:
                return "string";
            case ClassificationTypeNames.NumericLiteral:
                return "number";
            case ClassificationTypeNames.Operator:
            case ClassificationTypeNames.StringEscapeCharacter:
                return "operator";
            case ClassificationTypeNames.Punctuation:
                return "punctuation";
            case ClassificationTypeNames.StaticSymbol:
                return string.Empty;
            case ClassificationTypeNames.XmlDocCommentComment:
            case ClassificationTypeNames.XmlDocCommentDelimiter:
            case ClassificationTypeNames.XmlDocCommentName:
            case ClassificationTypeNames.XmlDocCommentText:
            case ClassificationTypeNames.XmlDocCommentAttributeName:
            case ClassificationTypeNames.XmlDocCommentAttributeQuotes:
            case ClassificationTypeNames.XmlDocCommentAttributeValue:
            case ClassificationTypeNames.XmlDocCommentEntityReference:
            case ClassificationTypeNames.XmlDocCommentProcessingInstruction:
            case ClassificationTypeNames.XmlDocCommentCDataSection:
                return "comment";
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

    private class Range
    {
        private ClassifiedSpan ClassifiedSpan { get; }
        public string Text { get; }

        public Range(string classification, TextSpan span, SourceText text) :
            this(classification, span, text.GetSubText(span).ToString())
        {
        }

        private Range(string classification, TextSpan span, string text) :
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
