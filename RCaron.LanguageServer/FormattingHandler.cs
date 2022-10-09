using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RCaron;

namespace SampleServer;

public class FormattingHandler : DocumentFormattingHandlerBase
{
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

        var parsed = RCaronRunner.Parse(content);

        void EvaluateLines(IList<Line> lines)
        {
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
                            if (content[tokenLine.Tokens[0].Position.End] != ' ')
                            {
                                res.Add(SpaceAt(tokenLine.Tokens[0].Position.End));
                            }

                            if (tokenLine.Tokens[0] is CallLikePosToken callLikePosToken &&
                                content[callLikePosToken.NameEndIndex] != ' ')
                            {
                                res.Add(SpaceAt(callLikePosToken.NameEndIndex));
                            }

                            break;
                        case LineType.VariableAssignment:
                            // space after variable
                            if (content[tokenLine.Tokens[0].Position.End] != ' ')
                            {
                                res.Add(SpaceAt(tokenLine.Tokens[0].Position.End));
                            }

                            // space after equals
                            if (content[tokenLine.Tokens[1].Position.End] != ' ')
                            {
                                res.Add(SpaceAt(tokenLine.Tokens[1].Position.End));
                            }

                            break;
                    }
                }

                if (line is CodeBlockLine codeBlockLine)
                    EvaluateLines(codeBlockLine.Token.Lines);
            }
        }

        EvaluateLines(parsed.Lines);

        return res;
    }
}