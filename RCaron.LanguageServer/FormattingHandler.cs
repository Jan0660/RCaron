using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RCaron;

namespace SampleServer;

public class FormattingHandler : DocumentFormattingHandlerBase
{
    private readonly ILogger<FormattingHandler> _logger;

    public FormattingHandler(ILogger<FormattingHandler> logger)
    {
        _logger = logger;
    }

    protected override DocumentFormattingRegistrationOptions CreateRegistrationOptions(
        DocumentFormattingCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new()
        {
            DocumentSelector = DocumentSelector.ForLanguage("rcaron"),
        };
    }

    public override async Task<TextEditContainer> Handle(DocumentFormattingParams request,
        CancellationToken cancellationToken)
    {
        var res = new List<TextEdit>();
        var content = await Util.GetDocumentText(request.TextDocument.Uri.GetFileSystemPath());

        TextEdit SpaceAt(int index)
        {
            var pos = Util.GetPosition(index, content);
            return new TextEdit
            {
                NewText = " ",
                Range = new Range
                {
                    Start = pos,
                    End = pos
                }
            };
        }

        void SingleSpaceAfter(int index)
        {
            // check already has space
            if (content[index + 1] == ' ' && content[index + 2] != ' ')
                return;

            var curIndex = index;
            while (curIndex < content.Length && content[curIndex] == ' ')
            {
                curIndex++;
            }

            var pos = Util.GetPosition(index, content);
            var endPos = Util.GetPosition(curIndex, content, pos.Line, pos.Character, index);
            res.Add(new TextEdit
            {
                NewText = " ",
                Range = new Range
                {
                    Start = pos,
                    End = endPos,
                }
            });
        }

        void NewlineAfter(int index)
        {
            // check already has newline
            if (content[index + 1] == '\r' || content[index + 1] == '\n')
                return;

            var pos = Util.GetPosition(index, content);
            res.Add(new TextEdit
            {
                NewText = "\n",
                Range = new Range
                {
                    Start = pos,
                    End = pos
                }
            });
        }

        int GetIndentationLengthForDepth(int depth)
        {
            if (request.Options.InsertSpaces)
                return request.Options.TabSize * depth;
            return depth;
        }

        void NewlineBefore(BlockPosToken blockPosToken)
        {
            var index = blockPosToken.Position.Start;
            var curIndex = index;
            while (curIndex >= 0 && (content[curIndex] == ' ' || content[curIndex] == '\t'))
            {
                curIndex--;
            }

            if (content[curIndex] == '\n')
                return;

            var pos = Util.GetPosition(curIndex, content);
            res.Add(new TextEdit
            {
                NewText = "\n", // + new string(' ', GetIndentationLengthForDepth(depth)),
                Range = new Range
                {
                    Start = pos,
                    End = pos
                }
            });
        }

        void Indent(int index, int depth)
        {
            // indent according to depth and current indentation
            var curIndex = index;
            var indent = 0;
            while (curIndex >= 0 && (content[curIndex] == ' ' || content[curIndex] == '\t'))
            {
                indent += content[curIndex] == ' ' ? 1 : request.Options.TabSize;
                curIndex--;
            }

            var shouldBeIndent = GetIndentationLengthForDepth(depth);
            if (indent == shouldBeIndent)
                return;

            // var pos = Util.GetPosition(index, content);
            // var endPos = Util.GetPosition(curIndex, content, pos.Line, pos.Character, index);
            var pos = Util.GetPosition(curIndex, content);
            var endPos = Util.GetPosition(index, content, pos.Line, pos.Character, curIndex);

            _logger.LogTrace("shouldBeIndent: " + shouldBeIndent);

            res.Add(new TextEdit
            {
                NewText = request.Options.InsertSpaces
                    ? new string(' ', shouldBeIndent)
                    : new string('\t', shouldBeIndent / request.Options.TabSize),
                Range = new Range
                {
                    Start = pos,
                    End = endPos,
                }
            });
        }

        void BodyBlockToken(CodeBlockToken cbt)
        {
            var startBlockPosToken = ((BlockPosToken)((TokenLine)cbt.Lines[0]).Tokens[0]);
            var endBlockPosToken = ((BlockPosToken)((TokenLine)cbt.Lines[^1]).Tokens[0]);
            var depth = startBlockPosToken.Depth;
            NewlineAfter(startBlockPosToken.Position.End);
            NewlineBefore(endBlockPosToken);
            Indent(endBlockPosToken.Position.Start, depth);
        }

        // returns index at which semicolon is
        int NoSpaceUntilSemicolon(int index)
        {
            var curIndex = index;
            while (curIndex < content.Length && content[curIndex] == ' ')
            {
                curIndex++;
            }
            var pos = Util.GetPosition(index, content);
            var endPos = Util.GetPosition(curIndex, content, pos.Line, pos.Character, index);
            res.Add(new TextEdit()
            {
                NewText = "",
                Range = new Range
                {
                    Start = pos,
                    End = endPos,
                }
            });
            return curIndex;
        }

        void NewLineAfterSemicolon(int index)
        {
            if(content[index + 1] == '\r' || content[index + 1] == '\n')
                return;
            var pos = Util.GetPosition(index + 1, content);
            res.Add(new TextEdit()
            {
                NewText = "\n",
                Range = new Range()
                {
                    Start = pos,
                    End = pos,
                }
            });
        }

        var parsed = RCaronRunner.Parse(content, returnDescriptive: true);

        void EvaluateLines(IList<Line> lines, int depth)
        {
            _logger.LogTrace($"EvaluateLines: lines.Count: {lines.Count} depth: {depth}");
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                if (line is TokenLine tokenLine)
                {
                    switch (tokenLine.Type)
                    {
                        case LineType.SwitchStatement:
                        case LineType.WhileLoop:
                        case LineType.DoWhileLoop:
                        case LineType.IfStatement:
                            // space after first keyword
                            if (tokenLine.Tokens[0] is CallLikePosToken callLikePosToken)
                                SingleSpaceAfter(callLikePosToken.NameEndIndex);

                            SingleSpaceAfter(tokenLine.Tokens[0].Position.End);
                            BodyBlockToken(((CodeBlockLine)lines[i + 1]).Token);
                            break;
                        case LineType.VariableAssignment:
                            // space after variable
                            SingleSpaceAfter(tokenLine.Tokens[0].Position.End);
                            // space after equals
                            SingleSpaceAfter(tokenLine.Tokens[1].Position.End);

                            var sci = NoSpaceUntilSemicolon(tokenLine.Tokens[^1].Position.End);
                            NewLineAfterSemicolon(sci);
                            break;
                        case LineType.ClassDefinition:
                        {
                            // space after class name
                            SingleSpaceAfter(tokenLine.Tokens[1].Position.End);
                            // space after 'class'
                            SingleSpaceAfter(tokenLine.Tokens[0].Position.End);

                            var cbt = (CodeBlockToken)tokenLine.Tokens[2];
                            BodyBlockToken(cbt);
                            EvaluateLines(cbt.Lines, ((BlockPosToken)((TokenLine)cbt.Lines[0]).Tokens[0]).Depth);
                            break;
                        }
                        case LineType.Function:
                        {
                            SingleSpaceAfter(tokenLine.Tokens[0].Position.End);
                            SingleSpaceAfter(tokenLine.Tokens[1].Position.End);
                            var cbt = ((CodeBlockLine)lines[i+1]).Token;
                            BodyBlockToken(cbt);
                            EvaluateLines(cbt.Lines, ((BlockPosToken)((TokenLine)cbt.Lines[0]).Tokens[0]).Depth);
                            break;
                        }
                    }

                    if (depth != -1 && tokenLine.Type != LineType.BlockStuff) 
                        Indent(tokenLine.Tokens[0].Position.Start, depth + 1);
                }

                if (line is CodeBlockLine codeBlockLine)
                    EvaluateLines(codeBlockLine.Token.Lines,
                        ((BlockPosToken)((TokenLine)codeBlockLine.Token.Lines[0]).Tokens[0]).Depth);
                if (line is SingleTokenLine { Type: LineType.PropertyWithoutInitializer } singleTokenLine)
                {
                    var sci = NoSpaceUntilSemicolon(singleTokenLine.Token.Position.End);
                    NewLineAfterSemicolon(sci);
                    Indent(singleTokenLine.Token.Position.Start, depth + 1);
                }
            }
        }

        EvaluateLines(parsed.Lines, -1);

        return res;
    }
}