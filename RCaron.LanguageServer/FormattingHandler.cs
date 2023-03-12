using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace RCaron.LanguageServer;

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
            DocumentSelector = Util.DocumentSelector,
        };
    }

    public override async Task<TextEditContainer?> Handle(DocumentFormattingParams request,
        CancellationToken cancellationToken)
    {
        var res = new List<TextEdit>();
        var content = await Util.GetDocumentTextAsync(request.TextDocument.Uri.GetFileSystemPath());

        void SingleSpaceAfter(int index)
        {
            // check already has space
            if (index + 1 >= content.Length || (content[index] == ' ' && content[index + 1] != ' '))
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
            if (index + 0 >= content.Length)
            {
                if (request.Options.InsertFinalNewline)
                {
                    var pos2 = Util.GetPosition(index, content);
                    res.Add(new TextEdit()
                    {
                        NewText = "\n",
                        Range = new Range
                        {
                            Start = pos2,
                            End = pos2,
                        }
                    });
                }

                return;
            }

            // check already has newline
            if (content[index + 0] == '\r' || content[index + 0] == '\n')
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

        void Indent(int index, int depth)
        {
            // indent according to depth and current indentation
            var curIndex = index - 1;
            var indent = 0;
            while (curIndex >= 0 && (content[curIndex] == ' ' || content[curIndex] == '\t'))
            {
                indent += content[curIndex] == ' ' ? 1 : request.Options.TabSize;
                curIndex--;
            }

            var shouldBeIndent = GetIndentationLengthForDepth(depth);
            _logger.LogTrace($"shouldBeIndent: {shouldBeIndent}; indent: {indent}");
            if (indent == shouldBeIndent)
                return;

            curIndex += 1;

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
            // NewlineAfter(startBlockPosToken.Position.End);
            if (cbt.Lines.Count != 2)
                NewlineBefore(endBlockPosToken.Position.Start);
            else
                NewlineAfter(startBlockPosToken.Position.End);
            Indent(endBlockPosToken.Position.Start, depth);
            // NewlineAfter(endBlockPosToken.Position.End);
        }

        void NoSpaceUntilSemicolon(int index)
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
                NewText = string.Empty,
                Range = new Range
                {
                    Start = pos,
                    End = endPos,
                }
            });
        }

        void NewlineBefore(int index)
        {
            var curIndex = index - 1;
            while (curIndex >= 0 && (content[curIndex] == ' ' || content[curIndex] == '\t'))
                curIndex--;

            if (curIndex <= 0 || content[curIndex] == '\n')
                return;
            curIndex++;
            var pos = Util.GetPosition(curIndex, content);
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
                    if (tokenLine.Type != LineType.BlockStuff)
                        NewlineBefore(tokenLine.Tokens[0].Position.Start);
                    switch (tokenLine.Type)
                    {
                        case LineType.SwitchStatement:
                        case LineType.WhileLoop:
                        case LineType.DoWhileLoop:
                        case LineType.IfStatement:
                        case LineType.ForeachLoop:
                        {
                            // space after first keyword
                            if (tokenLine.Tokens[0] is CallLikePosToken callLikePosToken)
                                SingleSpaceAfter(callLikePosToken.NameEndIndex);

                            SingleSpaceAfter(tokenLine.Tokens[0].Position.End);

                            // if (tokenLine.Type == LineType.SwitchCase)
                            // {
                            //     var cbt = ((CodeBlockToken)tokenLine.Tokens[1]);
                            //     BodyBlockToken(cbt);
                            //     EvaluateLines(cbt.Lines, ((BlockPosToken)((TokenLine)cbt.Lines[0]).Tokens[0]).Depth);
                            // }
                            break;
                        }
                        case LineType.SwitchCase:
                        {
                            SingleSpaceAfter(tokenLine.Tokens[0].Position.End);
                            break;
                        }
                        case LineType.VariableAssignment:
                            // space after variable
                            SingleSpaceAfter(tokenLine.Tokens[0].Position.End);
                            // space after equals
                            SingleSpaceAfter(tokenLine.Tokens[1].Position.End);

                            NoSpaceUntilSemicolon(tokenLine.Tokens[^1].Position.End);
                            break;
                        case LineType.ClassDefinition:
                        {
                            // space after class name
                            SingleSpaceAfter(tokenLine.Tokens[1].Position.End);
                            // space after 'class'
                            SingleSpaceAfter(tokenLine.Tokens[0].Position.End);
                            break;
                        }
                        case LineType.Function:
                        {
                            SingleSpaceAfter(tokenLine.Tokens[0].Position.End);
                            SingleSpaceAfter(tokenLine.Tokens[1].Position.End);
                            break;
                        }
                        case LineType.TryBlock:
                        {
                            SingleSpaceAfter(tokenLine.Tokens[0].Position.End);
                            break;
                        }
                        case LineType.CatchBlock:
                        {
                            SingleSpaceAfter(tokenLine.Tokens[0].Position.End);
                            break;
                        }
                        case LineType.FinallyBlock:
                        {
                            SingleSpaceAfter(tokenLine.Tokens[0].Position.End);
                            break;
                        }
                    }

                    if (depth != -1 && tokenLine.Type != LineType.BlockStuff)
                        Indent(tokenLine.Tokens[0].Position.Start, depth + 1);

                    // do code blocks in tokens
                    foreach (var token in tokenLine.Tokens)
                    {
                        if (token is CodeBlockToken codeBlockToken)
                        {
                            BodyBlockToken(codeBlockToken);
                            EvaluateLines(codeBlockToken.Lines,
                                ((BlockPosToken)((TokenLine)codeBlockToken.Lines[0]).Tokens[0]).Depth);
                        }
                    }
                }

                if (line is CodeBlockLine codeBlockLine)
                {
                    BodyBlockToken(codeBlockLine.Token);
                    EvaluateLines(codeBlockLine.Token.Lines,
                        ((BlockPosToken)((TokenLine)codeBlockLine.Token.Lines[0]).Tokens[0]).Depth);
                }

                if (line is SingleTokenLine { Type: LineType.PropertyWithoutInitializer } singleTokenLine)
                {
                    NewlineBefore(singleTokenLine.Token.Position.Start);
                    NoSpaceUntilSemicolon(singleTokenLine.Token.Position.End);
                    Indent(singleTokenLine.Token.Position.Start, depth + 1);
                }

                if (line is ForLoopLine forLoopLine)
                {
                    NewlineBefore(forLoopLine.CallToken.Position.Start);
                    SingleSpaceAfter(forLoopLine.CallToken.NameEndIndex);
                    SingleSpaceAfter(forLoopLine.CallToken.Position.End);
                }
            }
        }

        EvaluateLines(parsed.FileScope.Lines, -1);

#if DEBUG
        foreach (var edit in res)
            _logger.LogTrace($"TextEdit: NewText: {edit.NewText} Range: {edit.Range}");
#endif
        return res;
    }
}