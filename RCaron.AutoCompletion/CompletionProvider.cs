﻿using Dynamitey.Internal.Optimization;
using RCaron.Parsing;

namespace RCaron.AutoCompletion;

public partial class CompletionProvider
{
    static CompletionProvider()
    {
        foreach (var builtInFunction in BuiltInFunctions)
            if (builtInFunction.Detail == null)
                builtInFunction.Detail =
                    $"{(builtInFunction.DetailPreface != null ? builtInFunction.DetailPreface + "\n" : "")}(method) {builtInFunction.Word}(???)";
    }

    public List<Completion> GetCompletions(string code, int caretPosition, int maxCompletions = 40)
    {
        var list = new List<Completion>(maxCompletions);
        var parsed = RCaronParser.Parse(code, returnDescriptive: true, errorHandler: new ParsingErrorDontCareHandler());
        DoLines(parsed.FileScope.Lines);

        void DoTokens(IList<PosToken> tokens)
        {
            foreach (var token in tokens)
            {
                if (list.Count >= maxCompletions) return;
                // check caret position is within token
                if (caretPosition > token.Position.Start && caretPosition <= token.Position.End)
                {
                    switch (token)
                    {
                        case KeywordToken keywordToken:
                        {
                            foreach (var builtInFunction in BuiltInFunctions)
                            {
                                if (list.Count >= maxCompletions) return;
                                if (builtInFunction.Word.StartsWith(keywordToken.String,
                                        StringComparison.InvariantCultureIgnoreCase))
                                    list.Add(new Completion(builtInFunction, token.Position));
                            }

                            foreach (var keyword in Keywords)
                            {
                                if (list.Count >= maxCompletions) return;
                                if (keyword.Word.StartsWith(keywordToken.String,
                                        StringComparison.InvariantCultureIgnoreCase))
                                    list.Add(new Completion(keyword, token.Position));
                            }

                            break;
                        }
                        case CodeBlockToken codeBlockToken:
                            DoLines(codeBlockToken.Lines);
                            break;
                        case VariableToken variableToken:
                            foreach (var constant in Constants)
                            {
                                if (list.Count >= maxCompletions) return;
                                if (constant.Word.AsSpan().StartsWith(variableToken.ToSpan(code),
                                        StringComparison.InvariantCultureIgnoreCase))
                                    list.Add(new Completion(constant, token.Position));
                            }

                            break;
                    }
                }
            }
        }

        void DoLines(IList<Line> lines)
        {
            foreach (var line in lines)
            {
                if (list.Count >= maxCompletions) return;
                if (line is TokenLine tokenLine)
                {
                    if (caretPosition < tokenLine.Tokens[0].Position.Start ||
                        tokenLine.Tokens[^1].Position.End < caretPosition)
                        continue;
                    DoTokens(tokenLine.Tokens);
                }

                if (line is CodeBlockLine codeBlockLine)
                    DoLines(codeBlockLine.Token.Lines);
            }
        }

        return list;
    }
}