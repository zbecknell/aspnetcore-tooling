﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Syntax;
using Microsoft.AspNetCore.Razor.LanguageServer.Common;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using TextSpan = Microsoft.CodeAnalysis.Text.TextSpan;

namespace Microsoft.AspNetCore.Razor.LanguageServer.Formatting
{
    internal class DefaultRazorFormattingService : RazorFormattingService
    {
        private readonly RazorDocumentMappingService _documentMappingService;
        private readonly ILanguageServer _server;
        private readonly CSharpFormatter _csharpFormatter;
        private readonly HtmlFormatter _htmlFormatter;
        private readonly ILogger _logger;

        public DefaultRazorFormattingService(
            RazorDocumentMappingService documentMappingService,
            FilePathNormalizer filePathNormalizer,
            ILanguageServer server,
            ILoggerFactory loggerFactory)
        {
            if (documentMappingService is null)
            {
                throw new ArgumentNullException(nameof(documentMappingService));
            }

            if (filePathNormalizer is null)
            {
                throw new ArgumentNullException(nameof(filePathNormalizer));
            }

            if (server is null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            if (loggerFactory is null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _documentMappingService = documentMappingService;
            _server = server;
            _csharpFormatter = new CSharpFormatter(documentMappingService, server, filePathNormalizer);
            _htmlFormatter = new HtmlFormatter(server, filePathNormalizer);
            _logger = loggerFactory.CreateLogger<DefaultRazorFormattingService>();
        }

        public override async Task<TextEdit[]> FormatAsync(Uri uri, RazorCodeDocument codeDocument, Range range, FormattingOptions options)
        {
            if (uri is null)
            {
                throw new ArgumentNullException(nameof(uri));
            }

            if (codeDocument is null)
            {
                throw new ArgumentNullException(nameof(codeDocument));
            }

            if (range is null)
            {
                throw new ArgumentNullException(nameof(range));
            }

            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var formattingContext = FormattingContext.Create(uri, codeDocument, range, options);
            var edits = await FormatCodeBlockDirectivesAsync(formattingContext);
            return edits;
        }

        public override Task<TextEdit[]> ApplyFormattedEditsAsync(Uri uri, RazorCodeDocument codeDocument, RazorLanguageKind kind, TextEdit[] formattedEdits, FormattingOptions options)
        {
            // We have obtained a set of edits to the projected HTML/C# document from the client.
            // We need to apply those changes to the original Razor document as appropriate.

            if (kind == RazorLanguageKind.Html)
            {
                // We don't support formatting HTML edits yet.
                var edits = RemapTextEdits(codeDocument, formattedEdits);
                return Task.FromResult(edits);
            }

            // Create the formatting context for the razor document.
            var span = TextSpan.FromBounds(0, codeDocument.Source.Length);
            var range = span.AsRange(codeDocument.GetSourceText());
            var formattingContext = FormattingContext.Create(uri, codeDocument, range, options);

            // Normalize and re-map the C# edits.
            var csharpText = SourceText.From(codeDocument.GetCSharpDocument().GeneratedCode);
            var normalizedEdits = NormalizeTextEdits(csharpText, formattedEdits);
            var mappedEdits = RemapTextEdits(codeDocument, normalizedEdits);

            // Find the lines that were affected by these edits.
            var originalText = codeDocument.GetSourceText();
            var changes = mappedEdits.Select(e => e.AsTextChange(originalText));
            var changedText = originalText.WithChanges(changes);
            TrackEncompassingChange(originalText, changedText, out var spanBeforeChange, out var spanAfterChange);
            var rangeBeforeEdit = spanBeforeChange.AsRange(originalText);
            var rangeAfterEdit = spanAfterChange.AsRange(changedText);

            // Now, for each affected line in the edited version of the document, remove x amount of spaces
            // at the front to account for extra indentation applied by the C# formatter.
            // This should be based on context.
            // For instance, lines inside @code/@functions block should be reduced one level
            // and lines inside @{} should be reduced by two levels.
            var indentationEdits = new List<TextEdit>();
            for (var i = rangeAfterEdit.Start.Line; i <= rangeAfterEdit.End.Line; i++)
            {
                var isInClassBody = true; // TODO: This should come from context.
                var minCSharpIndentationLevel = isInClassBody ? 2 : 3;
                var minCSharpIndentationLength = formattingContext.GetIndentationLevelString(minCSharpIndentationLevel).Length;
                var existingWhitespaceLength = changedText.Lines[(int)i].GetFirstNonWhitespaceOffset() ?? 0;
                if (existingWhitespaceLength < minCSharpIndentationLength)
                {
                    // Safeguard so we don't nuke unnecessary characters. We can't nuke more that the available whitespace.
                    continue;
                }

                var whitespaceToRemoveLength = formattingContext.GetIndentationLevelString(minCSharpIndentationLevel - 1).Length;
                indentationEdits.Add(new TextEdit()
                {
                    NewText = string.Empty,
                    Range = new Range(new Position(i, 0), new Position(i, whitespaceToRemoveLength))
                });
            }

            // Apply the edits that remove indentation.
            changes = indentationEdits.Select(e => e.AsTextChange(changedText));
            changedText = changedText.WithChanges(changes);

            // Now that we have made all the necessary changes to the document. Let's diff the original vs final version and return the diff.
            var finalChanges = SourceTextDiffer.GetMinimalTextChanges(originalText, changedText, lineDiffOnly: false);
            var finalEdits = finalChanges.Select(f => f.AsTextEdit(originalText)).ToArray();

            return Task.FromResult(finalEdits);
        }

        private TextEdit[] RemapTextEdits(RazorCodeDocument codeDocument, TextEdit[] projectedTextEdits)
        {
            var edits = new List<TextEdit>();
            for (var i = 0; i < projectedTextEdits.Length; i++)
            {
                var projectedRange = projectedTextEdits[i].Range;
                if (codeDocument.IsUnsupported() ||
                    !_documentMappingService.TryMapFromProjectedDocumentRange(codeDocument, projectedRange, out var originalRange))
                {
                    // Can't map range. Discard this edit.
                    continue;
                }

                var edit = new TextEdit()
                {
                    Range = originalRange,
                    NewText = projectedTextEdits[i].NewText
                };

                edits.Add(edit);
            }

            return edits.ToArray();
        }

        private async Task<TextEdit[]> FormatCodeBlockDirectivesAsync(FormattingContext context)
        {
            // A code block directive is any extensible directive that can contain C# code. Here is how we represent it,
            // E.g,
            //
            //     @code {  public class Foo { }  }
            // ^                                  ^ ----> Full code block directive range (Includes preceding whitespace)
            //     ^                              ^ ----> Directive range
            //      ^                             ^ ----> DirectiveBody range
            //            ^                      ^  ----> inner codeblock range
            //
            // In this method, we are going to do the following for each code block directive,
            // 1. Format the inner codeblock using the C# formatter
            // 2. Adjust the absolute indentation of the lines formatted by the C# formatter while maintaining the relative indentation
            // 3. Indent the start of the code block (@code {) correctly and move any succeeding code to a separate line
            // 4. Indent the end of the code block (}) correctly and move it to a separate line if necessary
            // 5. Once all the edits are applied, compute the diff for this particular code block and add it to the global list of edits
            //
            var source = context.CodeDocument.Source;
            var syntaxTree = context.CodeDocument.GetSyntaxTree();
            var nodes = syntaxTree.GetCodeBlockDirectives();

            var allEdits = new List<TextEdit>();

            // Iterate in reverse so that the newline changes don't affect the next code block directive.
            for (var i = nodes.Length - 1; i >= 0; i--)
            {
                var directive = nodes[i];
                if (!(directive.Body is RazorDirectiveBodySyntax directiveBody))
                {
                    // This can't happen realistically. Just being defensive.
                    continue;
                }

                var directiveRange = directive.GetRange(source);
                if (!directiveRange.OverlapsWith(context.Range))
                {
                    // This block isn't in the selected range.
                    continue;
                }

                // Get the inner code block node that contains the actual code.
                var innerCodeBlockNode = directiveBody.CSharpCode.DescendantNodes().FirstOrDefault(n => n is CSharpCodeBlockSyntax);
                if (innerCodeBlockNode == null)
                {
                    // Nothing to indent.
                    continue;
                }

                if (innerCodeBlockNode.DescendantNodes().Any(n =>
                    n is MarkupBlockSyntax ||
                    n is CSharpTransitionSyntax ||
                    n is RazorCommentBlockSyntax))
                {
                    // We currently don't support formatting code block directives with Markup or other Razor constructs.
                    continue;
                }

                var originalText = context.SourceText;
                var changedText = originalText;
                var innerCodeBlockRange = innerCodeBlockNode.GetRange(source);

                // Compute the range inside the code block that overlaps with the provided input range.
                var csharprangeToFormat = innerCodeBlockRange.Overlap(context.Range);
                if (csharprangeToFormat != null)
                {
                    var codeEdits = await _csharpFormatter.FormatAsync(context.CodeDocument, csharprangeToFormat, context.Uri, context.Options);
                    changedText = ApplyCSharpEdits(context, innerCodeBlockRange, codeEdits, minCSharpIndentLevel: 2);
                }

                var edits = new List<TextEdit>();
                FormatCodeBlockStart(context, changedText, directiveBody, innerCodeBlockNode, edits);
                FormatCodeBlockEnd(context, changedText, directiveBody, innerCodeBlockNode, edits);
                changedText = ApplyChanges(changedText, edits.Select(e => e.AsTextChange(changedText)));

                // We've now applied all the edits we wanted to do. We now need to identify everything that changed in the given code block.
                // We need to include the preceding newline in our input range because we could have unindented the code block to achieve the correct indentation.
                // Without including the preceding newline, that edit would be lost.
                var fullCodeBlockDirectiveSpan = GetSpanIncludingPrecedingWhitespaceInLine(originalText, directive.Position, directive.EndPosition);
                var changes = Diff(originalText, changedText, fullCodeBlockDirectiveSpan);

                var transformedEdits = changes.Select(c => c.AsTextEdit(originalText));
                allEdits.AddRange(transformedEdits);
            }

            return allEdits.ToArray();
        }

        //
        // 'minCSharpIndentLevel' refers to the minimum level of how much the C# formatter would indent code.
        // @code/@functions blocks contain class members and so are typically indented by 2 levels.
        // @{} blocks are put inside method body which means they are typically indented by 3 levels.
        //
        private SourceText ApplyCSharpEdits(FormattingContext context, Range codeBlockRange, TextEdit[] edits, int minCSharpIndentLevel)
        {
            var originalText = context.SourceText;
            var originalCodeBlockSpan = codeBlockRange.AsTextSpan(originalText);

            // Sometimes the C# formatter edits outside the range we supply. Filter out those edits.
            var changes = edits.Select(e => e.AsTextChange(originalText)).Where(c => originalCodeBlockSpan.Contains(c.Span)).ToArray();
            if (changes.Length == 0)
            {
                return originalText;
            }

            // Apply the C# edits to the document.
            var changedText = originalText.WithChanges(changes);
            TrackChangeInSpan(originalText, originalCodeBlockSpan, changedText, out var changedCodeBlockSpan, out var changeEncompassingSpan);

            // We now have the changed document with C# edits. But it might be indented more/less than what we want depending on the context.
            // So, we want to bring each line to the right level of indentation based on where the block is in the document.
            // We also need to only do this for the lines that are part of the input range to respect range formatting.
            var desiredIndentationLevel = context.Indentations[(int)codeBlockRange.Start.Line].IndentationLevel + 1;
            var editsToApply = new List<TextChange>();
            var inputSpan = context.Range.AsTextSpan(originalText);
            TrackChangeInSpan(originalText, inputSpan, changedText, out var changedInputSpan, out _);
            var changedInputRange = changedInputSpan.AsRange(changedText);

            for (var i = (int)changedInputRange.Start.Line; i <= changedInputRange.End.Line; i++)
            {
                var line = changedText.Lines[i];
                if (line.Span.Length == 0)
                {
                    // Empty line. C# formatter didn't remove it so we won't either.
                    continue;
                }

                if (!changedCodeBlockSpan.Contains(line.Start))
                {
                    // Defensive check to make sure we're not handling lines that are not part of the current code block.
                    continue;
                }

                var leadingWhitespace = line.GetLeadingWhitespace();
                var minCSharpIndentLength = context.GetIndentationLevelString(minCSharpIndentLevel).Length;
                if (leadingWhitespace.Length < minCSharpIndentLength)
                {
                    // For whatever reason, the C# formatter decided to not indent this. Leave it as is.
                    continue;
                }
                else
                {
                    // At this point we assume the C# formatter has relatively indented this line to the correct level.
                    // All we want to do at this point is to indent/unindent this line based on the absolute indentation of the block
                    // and the minimum C# indent level. We don't need to worry about the actual existing indentation here because it doesn't matter.
                    var effectiveDesiredIndentationLevel = desiredIndentationLevel - minCSharpIndentLevel;
                    var effectiveDesiredIndentation = context.GetIndentationLevelString(Math.Abs(effectiveDesiredIndentationLevel));
                    if (effectiveDesiredIndentationLevel < 0)
                    {
                        // This means that we need to unindent.
                        var span = new TextSpan(line.Start, effectiveDesiredIndentation.Length);
                        editsToApply.Add(new TextChange(span, string.Empty));
                    }
                    else if (effectiveDesiredIndentationLevel > 0)
                    {
                        // This means that we need to indent.
                        var span = new TextSpan(line.Start, 0);
                        editsToApply.Add(new TextChange(span, effectiveDesiredIndentation));
                    }
                }
            }

            changedText = ApplyChanges(changedText, editsToApply);
            return changedText;
        }

        private void FormatCodeBlockStart(FormattingContext context, SourceText changedText, RazorDirectiveBodySyntax directiveBody, SyntaxNode innerCodeBlock, List<TextEdit> edits)
        {
            var sourceText = context.SourceText;
            var originalBodySpan = TextSpan.FromBounds(directiveBody.Position, directiveBody.EndPosition);
            var originalBodyRange = originalBodySpan.AsRange(sourceText);
            if (context.Range.Start.Line > originalBodyRange.Start.Line)
            {
                return;
            }

            // First line is within the selected range. Let's try and format the start.

            TrackChangeInSpan(sourceText, originalBodySpan, changedText, out var changedBodySpan, out _);
            var changedBodyRange = changedBodySpan.AsRange(changedText);

            // First, make sure the first line is indented correctly.
            var firstLine = changedText.Lines[(int)changedBodyRange.Start.Line];
            var desiredIndentationLevel = context.Indentations[firstLine.LineNumber].IndentationLevel;
            var desiredIndentation = context.GetIndentationLevelString(desiredIndentationLevel);
            var firstNonWhitespaceOffset = firstLine.GetFirstNonWhitespaceOffset();
            if (firstNonWhitespaceOffset.HasValue)
            {
                var edit = new TextEdit()
                {
                    Range = new Range(
                        new Position(firstLine.LineNumber, 0),
                        new Position(firstLine.LineNumber, firstNonWhitespaceOffset.Value)),
                    NewText = desiredIndentation
                };
                edits.Add(edit);
            }

            // We should also move any code that comes after '{' down to its own line.
            var originalInnerCodeBlockSpan = TextSpan.FromBounds(innerCodeBlock.Position, innerCodeBlock.EndPosition);
            TrackChangeInSpan(sourceText, originalInnerCodeBlockSpan, changedText, out var changedInnerCodeBlockSpan, out _);
            var innerCodeBlockRange = changedInnerCodeBlockSpan.AsRange(changedText);

            var innerCodeBlockLine = changedText.Lines[(int)innerCodeBlockRange.Start.Line];
            var textAfterBlockStart = innerCodeBlockLine.ToString().Substring(innerCodeBlock.Position - innerCodeBlockLine.Start);
            var isBlockStartOnSeparateLine = string.IsNullOrWhiteSpace(textAfterBlockStart);
            var innerCodeBlockIndentationLevel = desiredIndentationLevel + 1;
            var desiredInnerCodeBlockIndentation = context.GetIndentationLevelString(innerCodeBlockIndentationLevel);
            var whitespaceAfterBlockStart = textAfterBlockStart.GetLeadingWhitespace();

            if (!isBlockStartOnSeparateLine)
            {
                // If the first line contains code, add a newline at the beginning and indent it.
                var edit = new TextEdit()
                {
                    Range = new Range(
                        new Position(innerCodeBlockLine.LineNumber, innerCodeBlock.Position - innerCodeBlockLine.Start),
                        new Position(innerCodeBlockLine.LineNumber, innerCodeBlock.Position + whitespaceAfterBlockStart.Length - innerCodeBlockLine.Start)),
                    NewText = Environment.NewLine + desiredInnerCodeBlockIndentation
                };
                edits.Add(edit);
            }
            else
            {
                //
                // The code inside the code block directive is on its own line. Ideally the C# formatter would have already taken care of it.
                // Except, the first line of the code block is not indented because of how our SourceMappings work.
                // E.g,
                // @code {
                //     ...
                // }
                // Our source mapping for this code block only ranges between the { and }, exclusive.
                // If the C# formatter provides any edits that start from before the {, we won't be able to map it back and we will ignore it.
                // Unfortunately because of this, we partially lose some edits which would have indented the first line of the code block correctly.
                // So let's manually indent the first line here.
                //
                var innerCodeBlockText = changedText.GetSubTextString(changedInnerCodeBlockSpan);
                if (!string.IsNullOrWhiteSpace(innerCodeBlockText))
                {
                    var codeStart = innerCodeBlockText.GetFirstNonWhitespaceOffset() + changedInnerCodeBlockSpan.Start;
                    if (codeStart.HasValue && codeStart != changedInnerCodeBlockSpan.End)
                    {
                        // If we got here, it means this is a non-empty code block. We can safely indent the first line.
                        var codeStartLine = changedText.Lines.GetLineFromPosition(codeStart.Value);
                        var existingCodeStartIndentation = codeStartLine.GetFirstNonWhitespaceOffset() ?? 0;
                        var edit = new TextEdit()
                        {
                            Range = new Range(
                                new Position(codeStartLine.LineNumber, 0),
                                new Position(codeStartLine.LineNumber, existingCodeStartIndentation)),
                            NewText = desiredInnerCodeBlockIndentation
                        };
                        edits.Add(edit);
                    }
                }
            }
        }

        private void FormatCodeBlockEnd(FormattingContext context, SourceText changedText, RazorDirectiveBodySyntax directiveBody, SyntaxNode innerCodeBlock, List<TextEdit> edits)
        {
            var sourceText = context.SourceText;
            var originalBodySpan = TextSpan.FromBounds(directiveBody.Position, directiveBody.EndPosition);
            var originalBodyRange = originalBodySpan.AsRange(sourceText);
            if (context.Range.End.Line < originalBodyRange.End.Line)
            {
                return;
            }

            // Last line is within the selected range. Let's try and format the end.

            TrackChangeInSpan(sourceText, originalBodySpan, changedText, out var changedBodySpan, out _);
            var changedBodyRange = changedBodySpan.AsRange(changedText);

            var firstLine = changedText.Lines[(int)changedBodyRange.Start.Line];
            var desiredIndentationLevel = context.Indentations[firstLine.LineNumber].IndentationLevel;
            var desiredIndentation = context.GetIndentationLevelString(desiredIndentationLevel);

            // we want to keep the close '}' on its own line. So bring it to the next line.
            var originalInnerCodeBlockSpan = TextSpan.FromBounds(innerCodeBlock.Position, innerCodeBlock.EndPosition);
            TrackChangeInSpan(sourceText, originalInnerCodeBlockSpan, changedText, out var changedInnerCodeBlockSpan, out _);
            var closeCurlyLocation = changedInnerCodeBlockSpan.End;
            var closeCurlyLine = changedText.Lines.GetLineFromPosition(closeCurlyLocation);
            var firstNonWhitespaceOffset = closeCurlyLine.GetFirstNonWhitespaceOffset() ?? 0;
            if (closeCurlyLine.Start + firstNonWhitespaceOffset != closeCurlyLocation)
            {
                // This means the '}' is on the same line as some C# code.
                // Bring it down to the next line and apply the desired indentation.
                var edit = new TextEdit()
                {
                    Range = new Range(
                        new Position(closeCurlyLine.LineNumber, closeCurlyLocation - closeCurlyLine.Start),
                        new Position(closeCurlyLine.LineNumber, closeCurlyLocation - closeCurlyLine.Start)),
                    NewText = Environment.NewLine + desiredIndentation
                };
                edits.Add(edit);
            }
            else if (firstNonWhitespaceOffset != desiredIndentation.Length)
            {
                // This means the '}' is on its own line but is not indented correctly. Correct it.
                var edit = new TextEdit()
                {
                    Range = new Range(
                    new Position(closeCurlyLine.LineNumber, 0),
                    new Position(closeCurlyLine.LineNumber, firstNonWhitespaceOffset)),
                    NewText = desiredIndentation
                };
                edits.Add(edit);
            }
        }

        private SourceText ApplyChanges(SourceText original, IEnumerable<TextChange> changes)
        {
            var changed = original.WithChanges(changes);
            return changed;
        }

        private void TrackChangeInSpan(SourceText oldText, TextSpan originalSpan, SourceText newText, out TextSpan changedSpan, out TextSpan changeEncompassingSpan)
        {
            var affectedRange = newText.GetEncompassingTextChangeRange(oldText);

            // The span of text before the edit which is being changed
            changeEncompassingSpan = affectedRange.Span;

            if (!originalSpan.Contains(changeEncompassingSpan))
            {
                _logger.LogDebug($"The changed region {changeEncompassingSpan} was not a subset of the span {originalSpan} being tracked. This is unexpected.");
            }

            // We now know what was the range that changed and the length of that span after the change.
            // Let's now compute what the original span looks like after the change.
            // We know it still starts from the same location but could have grown or shrunk in length.
            // Compute the change in length and then update the original span.
            var changeInOriginalSpanLength = affectedRange.NewLength - changeEncompassingSpan.Length;
            changedSpan = TextSpan.FromBounds(originalSpan.Start, originalSpan.End + changeInOriginalSpanLength);
        }

        private TextChange[] Diff(SourceText oldText, SourceText newText, TextSpan? spanToDiff = default)
        {
            // Once https://github.com/dotnet/roslyn/issues/41413 is fixed,
            // the following lines can be replaced with `newText.GetTextChanges(oldText)`.

            var spanToTrack = spanToDiff ?? TextSpan.FromBounds(0, oldText.Length);
            TrackChangeInSpan(oldText, spanToTrack, newText, out var changedSpanToTrack, out _);
            var change = new TextChange(spanToTrack, newText.GetSubText(changedSpanToTrack).ToString());
            return new[] { change };
        }

        private static TextSpan GetSpanIncludingPrecedingWhitespaceInLine(SourceText sourceText, int start, int end)
        {
            var line = sourceText.Lines.GetLineFromPosition(start);
            var precedingLineText = sourceText.GetSubTextString(TextSpan.FromBounds(line.Start, start));
            var precedingWhitespaceLength = precedingLineText.GetTrailingWhitespace().Length;

            return TextSpan.FromBounds(start - precedingWhitespaceLength, end);
        }

        private static TextEdit[] NormalizeTextEdits(SourceText originalText, TextEdit[] edits)
        {
            var changes = edits.Select(e => e.AsTextChange(originalText));
            var changedText = originalText.WithChanges(changes);
            var cleanChanges = SourceTextDiffer.GetMinimalTextChanges(originalText, changedText, lineDiffOnly: false);
            var cleanEdits = cleanChanges.Select(c => c.AsTextEdit(originalText)).ToArray();
            return cleanEdits;
        }

        private static void TrackEncompassingChange(SourceText oldText, SourceText newText, out TextSpan spanBeforeChange, out TextSpan spanAfterChange)
        {
            var affectedRange = newText.GetEncompassingTextChangeRange(oldText);

            spanBeforeChange = affectedRange.Span;
            spanAfterChange = new TextSpan(spanBeforeChange.Start, affectedRange.NewLength);
        }
    }
}
