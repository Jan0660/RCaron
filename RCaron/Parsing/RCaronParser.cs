using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using RCaron.Classes;

namespace RCaron.Parsing;

public record RCaronParserContext(FileScope FileScope, bool AllowsExecution)
{
    public RCaronParserContext(FileScope? fileScope) : this(fileScope!, true)
    {
    }
}

public static class RCaronParser
{
    public static ParsingErrorThrowHandler DefaultThrowHandler { get; } = new();
    public static object FromPipelineObject { get; } = new();

    public static RCaronParserContext Parse(string text, bool returnIgnored = false, bool returnDescriptive = false,
        IParsingErrorHandler? errorHandler = null)
    {
        errorHandler ??= DefaultThrowHandler;
        var tokens = new List<PosToken>();
        var reader = new TokenReader(text, errorHandler, returnIgnored);
        var token = reader.Read();
        var blockDepth = -1;
        var blockNumber = -1;
        var unclosedPipeline = false;

        // todo(perf): this is absolutely horrendous, make this not be local method
        void DoCodeBlockToken()
        {
            if (tokens.Count < 2)
                return;
            if (tokens[^1] is not BlockPosToken { Type: TokenType.BlockEnd } endingBpt)
                return;
            var number = endingBpt.Number;
            var ind = tokens.FindIndex(t =>
                t is BlockPosToken { Type: TokenType.BlockStart } bpt && bpt.Number == number);
            var range = tokens.GetRangeAsArray(ind..);
            tokens.RemoveFrom(ind);
            var codeBlockLines = new List<Line>();
            for (int i = 0; i < range.Length; i++)
            {
                // skip semicolon
                if (range[i].Type == TokenType.LineEnding)
                    continue;
                codeBlockLines.Add(GetLine(range, ref i, text, errorHandler));
            }

            tokens.Add(new CodeBlockToken(codeBlockLines));
        }

        var hasDoneLastToken = false;
        while (token != null || !hasDoneLastToken)
        {
            if (token == null && !hasDoneLastToken)
            {
                hasDoneLastToken = true;
                token = new PosToken(TokenType.EndOfFile, (text.Length, text.Length));
            }

            if (token!.Type == TokenType.Whitespace || token.Type == TokenType.Comment ||
                token.Type == TokenType.Ignore)
            {
                token = reader.Read();
                continue;
            }

            DoCodeBlockToken();

            // for some reason, when not doing this, there is literally more memory allocated
            if (token is { } posToken)
            {
                switch (posToken)
                {
                    case BlockPosToken
                    {
                        Type: TokenType.BlockStart or TokenType.SimpleBlockStart or TokenType.IndexerStart
                    } blockPosToken:
                        blockDepth++;
                        blockNumber++;
                        blockPosToken.Depth = blockDepth;
                        blockPosToken.Number = blockNumber;
                        break;
                    case BlockPosToken
                    {
                        Type: TokenType.BlockEnd or TokenType.SimpleBlockEnd or TokenType.IndexerEnd
                    } blockPosToken:
                        blockPosToken.Depth = blockDepth;
                        blockPosToken.Number =
                            ((BlockPosToken)tokens.Last(t => t is BlockPosToken bpt && bpt.Depth == blockDepth)).Number;
                        blockDepth--;
                        break;
                }

                var dontAddCurrent = token.Type == TokenType.EndOfFile;

                if (token is BlockPosToken { Type: TokenType.IndexerEnd } ace)
                {
                    var number = ace.Number;
                    var acs = tokens.FindIndex(t => t is BlockPosToken blockPosToken && blockPosToken.Number == number);
                    var range = tokens.GetRangeAsArray((acs + 1)..);
                    var position = (tokens[acs].Position.Start, ace.Position.End);
                    tokens.RemoveFrom(acs);
                    tokens.Add(new IndexerToken(range, position));
                    dontAddCurrent = true;
                }

                (int index, PosToken[] tokens) BackwardsCollectDotThing()
                {
                    var i = tokens.Count - 1;
                    while ((i != 0 && i != -1) &&
                           tokens[i].IsDotJoinable() && tokens[i - 1].IsDotJoinable() &&
                           (tokens[i] is { Type: TokenType.Dot or TokenType.Indexer or TokenType.Range } ||
                            (tokens[i].Type == TokenType.Colon && tokens[i - 1].Type == TokenType.ExternThing) ||
                            (tokens[i - 1].Type == TokenType.Colon && tokens[i - 2].Type == TokenType.ExternThing) ||
                            tokens[i - 1] is { Type: TokenType.Dot or TokenType.Indexer or TokenType.Range } ||
                            (tokens.Count - i > 2 && tokens[i - 1].Type == TokenType.Number &&
                             tokens[i - 2].Type == TokenType.Dot)
                           ) && !(tokens[i - 1].Type == TokenType.Colon && tokens[i - 2].Type == TokenType.Keyword) &&
                           (tokens[i - 1].Position.End == tokens[i].Position.Start))
                        i--;
                    if (tokens.Count - i is 1 or 0)
                        return (-1, Array.Empty<PosToken>());
                    return (i, tokens.Take(i..).ToArray());
                }

                if (tokens.Count > 1 && tokens[^1].IsDotJoinable() &&
                    (tokens[^1].Type != TokenType.Dot && tokens[^1].Type != TokenType.Colon) &&
                    (!posToken.IsDotJoinable() || tokens[^1].Position.End != posToken.Position.Start) &&
                    posToken.Type != TokenType.IndexerStart &&
                    ((posToken.Type != TokenType.SimpleBlockStart && posToken.Type != TokenType.Dot) ||
                     tokens[^1].Position.End != posToken.Position.Start)
                    && posToken.Type != TokenType.IndexerEnd
                   )
                {
                    // todo(perf): it gets here when array literal
                    var h = BackwardsCollectDotThing();
                    if (h.index != -1)
                    {
                        tokens.RemoveFrom(h.index);
                        if (h.tokens[0].Type is TokenType.Dot or TokenType.Range or TokenType.Keyword ||
                            Array.Exists(h.tokens, t => t.Type == TokenType.Path))
                        {
                            var path = new StringBuilder();
                            foreach (var pathToken in h.tokens)
                            {
                                if (pathToken.Type == TokenType.Dot)
                                    path.Append('.');
                                else if (pathToken.Type == TokenType.Range)
                                    path.Append("..");
                                else if (pathToken is ConstToken { Type: TokenType.Path } actualPathToken)
                                    path.Append((string)actualPathToken.Value);
                                else if (pathToken is KeywordToken keywordToken)
                                    path.Append(keywordToken.String);
                                else if (pathToken.Type == TokenType.Number)
                                    path.Append(pathToken.ToString(text));
                                else
                                    throw new("Unexpected token type in path: " + pathToken.Type);
                            }

                            tokens.Add(new ConstToken(TokenType.Path,
                                (h.tokens.First().Position.Start, h.tokens.Last().Position.End), path.ToString()));
                        }
                        else
                        {
                            tokens.Add(new DotGroupPosToken(TokenType.DotGroup,
                                (h.tokens.First().Position.Start, h.tokens.Last().Position.End), h.tokens));
                        }
                    }
                }


                (int index, ValuePosToken[] tokens) BackwardsCollectValuePosToken()
                {
                    var i = tokens.Count - 1;
                    while ((i != 0 && i != -1) &&
                           tokens[i] is ValuePosToken && tokens[i - 1] is ValuePosToken &&
                           (tokens[i] is { Type: TokenType.MathOperator } ||
                            tokens[i - 1] is { Type: TokenType.MathOperator }))
                        i--;
                    if ((tokens.Count - i) % 2 == 0 || tokens.Count - i == 1)
                        return (-1, Array.Empty<ValuePosToken>());
                    return (i, tokens.Take(i..).Cast<ValuePosToken>().ToArray());
                }

                if (tokens.Count > 2 && tokens[^1] is ValuePosToken && tokens[^1].Type != TokenType.MathOperator &&
                    posToken.Type != TokenType.MathOperator && posToken.Type != TokenType.Dot &&
                    posToken.Type != TokenType.IndexerStart
                   )
                {
                    var h = BackwardsCollectValuePosToken();
                    if (h.index != -1 && h.tokens.Length != 1 && h.tokens.Length != 0 && h.tokens.Length != 2
                       )
                    {
                        // may not be needed?
                        if (h.tokens[1] is not { Type: TokenType.MathOperator })
                            goto beforeAdd;
                        var rem = h.index;
                        if (tokens[rem - 1] is not ValuePosToken && tokens[rem] is not BlockPosToken &&
                            tokens[rem] is not ValuePosToken)
                            goto beforeAdd;

                        tokens.RemoveFrom(rem);
                        tokens.Add(new TokenGroupPosToken(TokenType.TokenGroup,
                            (h.tokens.First().Position.Start, h.tokens.Last().Position.End), h.tokens));
                    }
                }

                // comparison and logical operation grouping
                if (tokens.Count > 2 && token is not ValuePosToken && !token.IsDotJoinable() &&
                    token.Type != TokenType.MathOperator && tokens[^1] is ValuePosToken right &&
                    tokens[^3] is ValuePosToken left)
                {
                    var ranComparison = false;
                    int? wouldEnd = token.Type == TokenType.SimpleBlockEnd ? token.Position.End : null;
                    // comparison group
                    if (tokens[^2].Type == TokenType.ComparisonOperation)
                    {
                        var comparison = new ComparisonValuePosToken(left, right, (OperationPosToken)tokens[^2],
                            (left.Position.Start, wouldEnd ?? right.Position.End));
                        tokens.RemoveFrom(tokens.Count - 3);
                        tokens.Add(comparison);
                        ranComparison = true;
                    }

                    // logical operation group
                    if (tokens[^2].Type == TokenType.LogicalOperation && token.Type != TokenType.ComparisonOperation)
                    {
                        // recheck ^3 and ^1 are still ValuePosTokens and reassign them
                        if (ranComparison && !((left = (tokens[^3] as ValuePosToken)!) != null &&
                                               (right = (tokens[^1] as ValuePosToken)!) != null))
                            goto afterComparisonAndLogicalGrouping;
                        var comparison = new LogicalOperationValuePosToken(left, right, (OperationPosToken)tokens[^2],
                            (left.Position.Start, wouldEnd ?? right.Position.End));
                        tokens.RemoveFrom(tokens.Count - 3);
                        tokens.Add(comparison);
                    }
                }

                afterComparisonAndLogicalGrouping: ;

                if (unclosedPipeline &&
                    (posToken.Type is TokenType.PipelineOperator or TokenType.LineEnding or TokenType.EndOfFile ||
                     _isNewLineBetweenTokens(tokens[^1], posToken, text)))
                {
                    // find the last pipeline operator
                    var lastPipelineIndex = tokens.FindLastIndex(t => t.Type == TokenType.PipelineOperator);
                    // collect the tokens before the last pipeline operator
                    var beforeStartIndex = lastPipelineIndex;
                    for (var i = lastPipelineIndex - 1; i >= 0; i--)
                    {
                        var wasNewline = false;
                        if ((tokens[i] is
                                { Type: TokenType.PipelineOperator or TokenType.Pipeline }
                                or OperationPosToken
                                {
                                    Operation: OperationEnum.Assignment,
                                } || (wasNewline = _isNewLineBetweenTokens(tokens[i], tokens[lastPipelineIndex], text))) &&
                            tokens[i].Type is not TokenType.Dot or TokenType.Keyword or TokenType.Path)
                        {
                            if (wasNewline)
                                i++;
                            beforeStartIndex = i;
                            break;
                        }

                        beforeStartIndex = i;
                    }

                    var before = tokens.Take(beforeStartIndex..lastPipelineIndex).ToArray();
                    var after = tokens.Take((lastPipelineIndex + 1)..).ToArray();
                    var pipeline = new PipelineValuePosToken(before, after);
                    tokens.RemoveFrom(beforeStartIndex);
                    tokens.Add(pipeline);
                    unclosedPipeline = false;
                }

                if (posToken.Type == TokenType.PipelineOperator)
                    unclosedPipeline = true;

                if (posToken is BlockPosToken
                    {
                        Type: TokenType.SimpleBlockEnd
                    } blockToken)
                {
                    var startIndex = tokens.FindIndex(
                        t => t is BlockPosToken { Type: TokenType.SimpleBlockStart } bpt &&
                             bpt.Number == blockToken.Number);

                    if (tokens[startIndex - 1].Type == TokenType.ArrayLiteralStart ||
                        // keyword with a ( immediately after it
                        (tokens[startIndex - 1].Type == TokenType.Keyword &&
                         (tokens[startIndex - 1].Position.End == tokens[startIndex].Position.Start
                          || tokens[startIndex - 1].IsKeywordWithIgnoredWhitespace(raw: text)))
                       )
                    {
                        // todo: dear lord
                        var tks = CollectionsMarshal.AsSpan(tokens)[(startIndex + 1)..];
                        var separator = TokenType.Comma;
                        if (tokens[startIndex - 1].EqualsString(text, "for") ||
                            tokens[startIndex - 1].EqualsString(text, "qfor"))
                            separator = TokenType.LineEnding;
                        var c = 1;
                        // count commas in tks
                        for (var i = 0; i < tks.Length; i++)
                        {
                            if (tks[i].Type == separator) c++;
                        }

                        if (tks.Length == 0)
                            c = 0;
                        var args = new PosToken[c][];
                        for (byte i = 0; i < c; i++)
                        {
                            // find index of next comma
                            var ind = 0;
                            for (; ind < tks.Length; ind++)
                            {
                                if (tks[ind].Type == separator) break;
                            }

                            args[i] = tks[..(ind != -1 ? new Index(ind) : Index.End)].ToArray();
                            // comma was not found
                            if (ind != tks.Length)
                                tks = tks[(ind + 1)..];
                        }

                        var h = new CallLikePosToken(TokenType.KeywordCall,
                            (tokens[startIndex - 1].Position.Start, posToken.Position.End), args,
                            tokens[startIndex - 1].Position.End, tokens[startIndex - 1].ToString(text)
                        );
                        tokens.RemoveFrom(startIndex - 1);
                        tokens.Add(h);
                        dontAddCurrent = true;
                    }
                    else
                    {
                        var group = new GroupValuePosToken(TokenType.Group,
                            (tokens[startIndex - 1].Position.Start, posToken.Position.End),
                            tokens.GetRangeAsArray((startIndex + 1)..)
                        );
                        tokens.RemoveFrom(startIndex);
                        tokens.Add(group);
                        dontAddCurrent = true;
                    }
                }

                afterCallLikePosTokenThing: ;

                beforeAdd: ;
                if (!dontAddCurrent)
                    tokens.Add(posToken);
            }

            token = reader.Read();
        }

        DoCodeBlockToken();

        // find lines
        var lines = new List<Line>();
        var fileScope = new FileScope
        {
            Raw = text,
            Lines = lines,
        };
        var t = tokens.ToArray();
        for (var i = 0; i < tokens.Count; i++)
        {
            // skip semicolon
            if (t[i].Type == TokenType.LineEnding)
                continue;
            // class definition
            if (t.Length - i > 2 && t[i].Type == TokenType.Keyword && t[i].EqualsString(text, "class"))
            {
                var className = ((KeywordToken)t[i + 1]).String;
                Dictionary<string, Function>? functions = null;
                Dictionary<string, Function>? staticFunctions = null;
                List<string>? propertyNames = null;
                List<PosToken[]?>? propertyInitializers = null;
                List<string>? staticPropertyNames = null;
                List<object?>? staticPropertyDefaultValues = null;
                // get properties and functions
                var body = (CodeBlockToken)t[i + 2];
                for (var j = 1; j < body.Lines.Count - 1; j++)
                {
                    if (body.Lines[j].Type == LineType.Function)
                    {
                        var tokenLine = (TokenLine)body.Lines[j];
                        var f = DoFunction(tokenLine.Tokens, errorHandler);
                        functions ??= new(StringComparer.InvariantCultureIgnoreCase);
                        functions[f.name] = new Function((CodeBlockToken)tokenLine.Tokens[2], f.arguments, fileScope);
                    }
                    else if (body.Lines[j].Type == LineType.StaticFunction)
                    {
                        var tokenLine = (TokenLine)body.Lines[j];
                        var f = DoFunction(tokenLine.Tokens, errorHandler, offset: 1);
                        staticFunctions ??= new(StringComparer.InvariantCultureIgnoreCase);
                        staticFunctions[f.name] =
                            new Function((CodeBlockToken)tokenLine.Tokens[3], f.arguments, fileScope);
                    }
                    else if (body.Lines[j].Type == LineType.VariableAssignment)
                    {
                        propertyNames ??= new();
                        propertyInitializers ??= new();
                        var tokenLine = (TokenLine)body.Lines[j];
                        propertyNames.Add(((VariableToken)tokenLine.Tokens[0]).Name);
                        propertyInitializers.Add(tokenLine.Tokens[2..]);
                    }
                    else if (body.Lines[j].Type == LineType.PropertyWithoutInitializer)
                    {
                        propertyNames ??= new();
                        propertyInitializers ??= new();
                        var tokenLine = (SingleTokenLine)body.Lines[j];
                        propertyNames.Add(((VariableToken)tokenLine.Token).Name);
                        propertyInitializers.Add(null);
                    }
                    else if (body.Lines[j].Type == LineType.StaticProperty)
                    {
                        staticPropertyNames ??= new();
                        staticPropertyDefaultValues ??= new();
                        var tokenLine = (TokenLine)body.Lines[j];
                        var propertyName = ((VariableToken)tokenLine.Tokens[1]).Name;
                        staticPropertyNames.Add(propertyName);
                        if (tokenLine.Tokens.Length < 3)
                        {
                            errorHandler.Handle(
                                ParsingException.StaticPropertyWithoutInitializer(propertyName,
                                    tokenLine.GetLocation()));
                            staticPropertyDefaultValues.Add(null);
                        }
                        else
                            staticPropertyDefaultValues.Add(EvaluateConstantToken(tokenLine.Tokens[3], errorHandler));
                    }
                    else
                    {
                        errorHandler.Handle(ParsingException.InvalidClassMember(body.Lines[j].Type,
                            body.Lines[j].GetLocation()));
                    }
                }

                if (returnDescriptive)
                    lines.Add(new TokenLine(new[] { t[i], t[i + 1], t[i + 2] }, LineType.ClassDefinition));
                i += 2;
                fileScope.ClassDefinitions ??= new();
                Debug.Assert(staticPropertyNames?.Count == staticPropertyDefaultValues?.Count,
                    "static property names and default values count mismatch");
                fileScope.ClassDefinitions.Add(
                    new ClassDefinition(className, propertyNames?.ToArray(), propertyInitializers?.ToArray())
                    {
                        Functions = functions, StaticFunctions = staticFunctions,
                        StaticPropertyValues = staticPropertyDefaultValues?.ToArray(),
                        StaticPropertyNames = staticPropertyNames?.ToArray(),
                    });
            }
            // function
            else if (t.Length - i > 2 && tokens[i].Type == TokenType.Keyword && tokens[i].EqualsString(text, "func"))
            {
                var (name, arguments) = DoFunction(tokens, errorHandler, i);
                fileScope.Functions ??= new(StringComparer.InvariantCultureIgnoreCase);
                fileScope.Functions[name] = new Function((CodeBlockToken)tokens[i + 2], arguments, fileScope);
                if (returnDescriptive)
                    lines.Add(new TokenLine(tokens.GetRangeAsArray(i..(i + 3)), LineType.Function));
                i += 2;
            }
            else
            {
                lines.Add(GetLine(t, ref i, text, errorHandler));
            }
        }

        return new RCaronParserContext(fileScope, errorHandler.AllowsExecution());
    }

    /// <summary>
    /// If there is a newline(\n) between the two tokens, returns true.
    /// </summary>
    /// <param name="first">The first token</param>
    /// <param name="second">The second token</param>
    /// <param name="code">Raw code</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool _isNewLineBetweenTokens(PosToken first, PosToken second, string code)
    {
        for (var j = first.Position.End; j < second.Position.Start; j++)
            if (code[j] == '\n')
                return true;
        return false;
    }

    private static (int toUse, int toSet) _findLineEnding(PosToken[] tokens, in int i, string text)
    {
        var ind = i;
        for (; ind < tokens.Length; ind++)
        {
            if (tokens[ind].Type == TokenType.LineEnding)
                return (ind, ind);
            if (tokens[ind].Type == TokenType.BlockEnd)
                return (ind, ind);
            if (tokens.Length - ind > 1)
            {
                if (_isNewLineBetweenTokens(tokens[ind], tokens[ind + 1], text))
                    return (ind + 1, ind);
            }
        }

        return (ind, ind);
    }

    public static Line GetLine(PosToken[] tokens, ref int i, in string text, IParsingErrorHandler errorHandler)
    {
        Line? res;
        var callToken = tokens[i] as CallLikePosToken;
        // variable assignment
        if (tokens.Length - i > 2 && tokens[i] is VariableToken && tokens[i + 1].Type == TokenType.Operation &&
            tokens[i + 1].EqualsString(text, "="))
        {
            var endingIndex = _findLineEnding(tokens, i, text);
            res = new TokenLine(tokens[i..(endingIndex.toUse)], LineType.VariableAssignment);
            i = endingIndex.toSet;
        }
        // let variable assignment
        else if (tokens[i].Type == TokenType.Keyword && tokens[i].EqualsString(text, "let") &&
                 tokens[i + 1] is VariableToken && tokens[i + 2].Type == TokenType.Operation &&
                 tokens[i + 2].EqualsString(text, "="))
        {
            var endingIndex = _findLineEnding(tokens, i, text);
            res = new TokenLine(tokens[i..(endingIndex.toUse)], LineType.LetVariableAssignment);
            i = endingIndex.toSet;
        }
        else if (callToken != null && callToken.NameEquals(text, "switch"))
        {
            res = new TokenLine(tokens[i..(i + 2)], LineType.SwitchStatement);
            i++;
        }
        else if (tokens.Length - i > 1 && (tokens[i].IsLiteral() || //tokens[i].Type == TokenType.CodeBlock ||
                                           (tokens[i].Type == TokenType.Keyword &&
                                            tokens[i].EqualsStringCaseInsensitive(text, "default"))) &&
                 tokens[i + 1].Type == TokenType.CodeBlock)
        {
            res = new TokenLine(tokens[i..(i + 2)], LineType.SwitchCase);
            i++;
        }
        // property without initializer in class
        else if (tokens.Length - i > 1 && tokens[i].Type == TokenType.VariableIdentifier &&
                 tokens[i + 1].Type == TokenType.LineEnding)
        {
            res = new SingleTokenLine(tokens[i], LineType.PropertyWithoutInitializer);
            i++;
        }
        // try block
        else if (tokens[i] is KeywordToken { String: "try" })
        {
            res = new TokenLine(tokens[i..(i + 2)], LineType.TryBlock);
            i++;
        }
        // catch block
        else if (tokens[i] is KeywordToken { String: "catch" })
        {
            res = new TokenLine(tokens[i..(i + 2)], LineType.CatchBlock);
            i++;
        }
        // finally block
        else if (tokens[i] is KeywordToken { String: "finally" })
        {
            res = new TokenLine(tokens[i..(i + 2)], LineType.FinallyBlock);
            i++;
        }
        // assigner assignment
        else if (tokens.Length - i > 2 && tokens[i].Type is TokenType.ExternThing or TokenType.DotGroup
                                       && tokens[i + 1].Type == TokenType.Operation
                                       && tokens[i + 1].EqualsString(text, "="))
        {
            var endingIndex = _findLineEnding(tokens, i, text);
            res = new TokenLine(tokens[i..(endingIndex.toUse)], LineType.AssignerAssignment);
            i = endingIndex.toSet;
        }
        // unary operation
        else if (tokens.Length - i > 1 && tokens[i].Type == TokenType.VariableIdentifier &&
                 tokens[i + 1].Type == TokenType.UnaryOperation)
        {
            res = new UnaryOperationLine(tokens[i..(i + 2)], LineType.UnaryOperation);
            i += 2;
        }
        // if statement
        else if (callToken is { Type: TokenType.KeywordCall } && callToken.NameEquals(text, "if"))
        {
            return new TokenLine(new[] { tokens[i], tokens[++i] }, LineType.IfStatement);
        }
        // else if statement
        else if (tokens[i].EqualsString(text, "else") && tokens[i + 1] is CallLikePosToken ifCallToken &&
                 ifCallToken.NameEquals(text, "if"))
        {
            res = new TokenLine(new[] { tokens[i], tokens[++i], tokens[++i] }, LineType.ElseIfStatement);
        }
        // else statement
        else if (tokens[i].EqualsString(text, "else"))
        {
            return new TokenLine(new[] { tokens[i], tokens[++i] }, LineType.ElseStatement);
        }
        // loop loop
        else if (tokens[i] is KeywordToken keywordToken && keywordToken.String == "loop")
        {
            return new TokenLine(new[] { tokens[i], tokens[++i] }, LineType.LoopLoop);
        }
        // while loop
        else if (callToken is { Type: TokenType.KeywordCall } && callToken.NameEquals(text, "while"))
        {
            return new TokenLine(new[] { tokens[i], tokens[++i] }, LineType.WhileLoop);
        }
        // do while loop
        else if (callToken is { Type: TokenType.KeywordCall } && callToken.NameEquals(text, "dowhile"))
        {
            return new TokenLine(new[] { tokens[i], tokens[++i] }, LineType.DoWhileLoop);
        }
        // for loop
        else if (callToken is { Type: TokenType.KeywordCall } && callToken.NameEquals(text, "for"))
        {
            var falseI = 0;
            var initializer = callToken.Arguments[0].Length == 0
                ? null
                : GetLine(callToken.Arguments[0], ref falseI, text, errorHandler);
            falseI = 0;
            var iterator = callToken.Arguments[2].Length == 0
                ? null
                : GetLine(callToken.Arguments[2], ref falseI, text, errorHandler);
            return new ForLoopLine(callToken, initializer, iterator, (CodeBlockToken)tokens[++i]);
        }
        // qfor loop
        else if (callToken is { Type: TokenType.KeywordCall } && callToken.NameEquals(text, "qfor"))
        {
            var falseI = 0;
            var initializer = callToken.Arguments[0].Length == 0
                ? null
                : GetLine(callToken.Arguments[0], ref falseI, text, errorHandler);
            falseI = 0;
            var iterator = callToken.Arguments[2].Length == 0
                ? null
                : GetLine(callToken.Arguments[2], ref falseI, text, errorHandler);
            return new ForLoopLine(callToken, initializer, iterator, (CodeBlockToken)tokens[++i],
                LineType.QuickForLoop);
        }
        // foreach loop
        else if (callToken is { Type: TokenType.KeywordCall } && callToken.NameEquals(text, "foreach"))
        {
            return new TokenLine(new[] { tokens[i], tokens[++i] }, LineType.ForeachLoop);
        }
        // static property
        else if (tokens[i].Type == TokenType.Keyword && tokens[i].EqualsString(text, "static") &&
                 tokens[i + 1].Type == TokenType.VariableIdentifier)
        {
            var endingIndex = _findLineEnding(tokens, i, text);
            res = new TokenLine(tokens[i..(endingIndex.toUse)], LineType.StaticProperty);
            i = endingIndex.toSet;
        }
        // static function
        else if (tokens[i].Type == TokenType.Keyword && tokens[i].EqualsString(text, "static") &&
                 tokens[i + 1].Type == TokenType.Keyword && tokens[i + 1].EqualsString(text, "func"))
        {
            res = new TokenLine(tokens[i..(i + 4)], LineType.StaticFunction);
            i += 3;
        }
        // function (should happen only inside a code block)
        // todo(perf): do not do this in code blocks too
        else if (tokens[i].Type == TokenType.Keyword && tokens[i].EqualsString(text, "func"))
        {
            res = new TokenLine(tokens[i..(i + 3)], LineType.Function);
            i += 2;
        }
        else if (tokens[i] is { Type: TokenType.BlockStart or TokenType.BlockEnd })
        {
            res = new TokenLine(new[] { tokens[i] }, LineType.BlockStuff);
        }
        // keyword plain call
        else if (tokens[i].Type == TokenType.Keyword)
        {
            var endingIndex = _findLineEnding(tokens, i, text);
            res = new TokenLine(
                tokens[i..(endingIndex.toUse)],
                LineType.KeywordPlainCall);
            i = endingIndex.toSet;
        }
        // path call
        else if (tokens[i].Type == TokenType.Path)
        {
            var endingIndex = _findLineEnding(tokens, i, text);
            res = new TokenLine(
                tokens[i..(endingIndex.toUse)],
                LineType.PathCall);
            i = endingIndex.toSet;
        }
        // code block
        else if (tokens[i] is CodeBlockToken { Type: TokenType.CodeBlock } cbt)
        {
            res = new CodeBlockLine(cbt);
        }
        else if (callToken is not null)
        {
            res = new TokenLine(new[] { tokens[i] }, LineType.KeywordCall);
            i++;
        }
        else if (tokens[i] is DotGroupPosToken)
        {
            res = new TokenLine(new[] { tokens[i] }, LineType.DotGroupCall);
        }
        // pipeline run
        else if (tokens[i] is { Type: TokenType.Pipeline })
        {
            res = new SingleTokenLine(tokens[i], LineType.PipelineRun);
        }
        // invalid line
        else
        {
            var lineNumber = 0;
            var pos = tokens[i].Position.Start;
            for (var index = 0; index < pos; ++index)
            {
                if (text[index] == '\n')
                    lineNumber++;
            }

            errorHandler.Handle(ParsingException.InvalidLine(lineNumber, new TextSpan(pos, 1)));
            return new TokenLine(new[] { tokens[i] }, LineType.InvalidLine);
        }

        return res;
    }

    public static (string name, FunctionArgument[]? arguments) DoFunction(IList<PosToken> tokens,
        IParsingErrorHandler errorHandler, int offset = 0)
    {
        string name;
        FunctionArgument[]? arguments = null;
        if (tokens[offset + 1] is CallLikePosToken callToken)
        {
            name = callToken.Name;
            arguments = new FunctionArgument[callToken.Arguments.Length];
            for (int j = 0; j < callToken.Arguments.Length; j++)
            {
                var cur = callToken.Arguments[j];
                var argName = ((VariableToken)cur[0]).Name;
                arguments[j] = new FunctionArgument(argName);
                if (cur is [_, OperationPosToken { Operation: OperationEnum.Assignment }, ..])
                {
                    // todo: support constant expressions like if 1 + 1
                    arguments[j].DefaultValue = EvaluateConstantToken(cur[2], errorHandler);
                }
            }
        }
        else if (tokens[offset + 1] is KeywordToken keywordToken)
            name = keywordToken.String;
        else
            throw new Exception("Invalid function name token");

        return (name, arguments);
    }

    public static object? EvaluateConstantToken(PosToken token, IParsingErrorHandler errorHandler)
        => token switch
        {
            // todo(current): in TokenReader make true, false and null into ConstTokens instead of variables
            VariableToken { Name: "true" } => true,
            VariableToken { Name: "false" } => false,
            VariableToken { Name: "null" } => null,
            VariableToken { Name: "fromPipeline" } => FromPipelineObject,
            ConstToken constToken => constToken.Value,
            _ => errorHandler.Handle(ParsingException.ExpectedConstant(TextSpan.FromToken(token))),
        };
}