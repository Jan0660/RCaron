using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using RCaron.Classes;

namespace RCaron;

public static class RCaronRunner
{
    public static Motor Run(string text, MotorOptions? motorOptions = null)
    {
        var ctx = Parse(text);
#if RCARONJIT
        var fakedMotor =
 System.Linq.Enumerable.First(System.AppDomain.CurrentDomain.GetAssemblies(), ass => ass.GetName().Name == "RCaron.Jit").GetType("RCaron.Jit.Hook").GetMethod("Run").Invoke(null, new object[] { ctx, motorOptions, null });
        return (Motor)fakedMotor;
#endif
        var motor = new Motor(ctx, motorOptions);
        motor.Run();
        return motor;
    }

    public static RCaronRunnerContext Parse(string text, bool returnIgnored = false, bool returnDescriptive = false)
    {
        var tokens = new List<PosToken>();
        var reader = new TokenReader(text, returnIgnored);
        var token = reader.Read();
        var blockDepth = -1;
        var blockNumber = -1;

        // todo(perf): this is absolutely horrendous
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
                codeBlockLines.Add(GetLine(range, ref i, text));
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

            if (token.Type == TokenType.Whitespace || token.Type == TokenType.Comment || token.Type == TokenType.Ignore)
            {
                token = reader.Read();
                continue;
            }

            DoCodeBlockToken();

            // for some reason, when not doing this, there is literally more memory allocated
            if (token is PosToken posToken)
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
                var dontDoSimpleBlockEndCheck = false;

                if (token is BlockPosToken { Type: TokenType.IndexerEnd } ace)
                {
                    var number = ace.Number;
                    var acs = tokens.FindIndex(t => t is BlockPosToken blockPosToken && blockPosToken.Number == number);
                    var range = tokens.GetRangeAsArray((acs + 1)..);
                    tokens.RemoveFrom(acs);
                    tokens.Add(new IndexerToken(range));
                    dontAddCurrent = true;
                }

                (int index, PosToken[] tokens) BackwardsCollectDotThing()
                {
                    var i = tokens.Count - 1;
                    while ((i != 0 && i != -1) &&
                           tokens[i].IsDotJoinableSomething() && tokens[i - 1].IsDotJoinableSomething() &&
                           (tokens[i] is { Type: TokenType.Dot or TokenType.Indexer or TokenType.Range } ||
                            (tokens[i].Type == TokenType.Colon && tokens[i - 1].Type == TokenType.ExternThing) ||
                            (tokens[i - 1].Type == TokenType.Colon && tokens[i - 2].Type == TokenType.ExternThing) ||
                            tokens[i - 1] is { Type: TokenType.Dot or TokenType.Indexer or TokenType.Range }
                           ) && !(tokens[i - 1].Type == TokenType.Colon && tokens[i - 2].Type == TokenType.Keyword))
                        i--;
                    if (tokens.Count - i == 1)
                        return (-1, Array.Empty<PosToken>());
                    return (i, tokens.Take(i..).ToArray());
                }

                if (tokens.Count > 2 && tokens[^1].IsDotJoinableSomething() &&
                    (tokens[^1].Type != TokenType.Dot && tokens[^1].Type != TokenType.Colon) &&
                    (!posToken.IsDotJoinableSomething() || _isNewLineBetweenTokens(tokens[^1], posToken, text)) &&
                    posToken.Type != TokenType.IndexerStart &&
                    posToken.Type != TokenType.SimpleBlockStart
                   )
                {
                    // todo(perf): it gets here when array literal
                    var h = BackwardsCollectDotThing();
                    if (h.index != -1 && h.tokens.Length != 1 && h.tokens.Length != 0)
                    {
                        // AAAAAA
                        // remove those replace with fucking imposter thing
                        var rem = h.index;
                        tokens.RemoveFrom(rem);
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
                                else
                                    throw new("something went very wrong with the parsing it seems");
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
                    if (h.index != -1 && h.tokens.Length != 1 && h.tokens.Length != 0 && h.tokens.Length != 2)
                    {
                        // may not be needed?
                        if (h.tokens[1] is not { Type: TokenType.MathOperator })
                            goto beforeAdd;
                        // AAAAAA
                        // remove those replace with fucking imposter thing
                        var rem = h.index - 1;
                        if (rem < 1 || tokens[rem].Type != TokenType.SimpleBlockStart ||
                            posToken.Type != TokenType.SimpleBlockEnd)
                            rem += 1;
                        if (tokens[rem - 1] is not ValuePosToken && tokens[rem] is not BlockPosToken &&
                            tokens[rem] is not ValuePosToken)
                            goto beforeAdd;
                        if (tokens[rem].Type == TokenType.SimpleBlockStart)
                        {
                            dontDoSimpleBlockEndCheck = true;
                            dontAddCurrent = true;
                        }

                        tokens.RemoveFrom(rem);
                        tokens.Add(new MathValueGroupPosToken(TokenType.DumbShit,
                            (h.tokens.First().Position.Start, h.tokens.Last().Position.End), h.tokens));
                    }
                }

                // comparison and logical operation grouping
                if (tokens.Count > 2 && token is not ValuePosToken && !token.IsDotJoinableSomething() &&
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
                        if (ranComparison && !((left = tokens[^3] as ValuePosToken) != null &&
                                               (right = tokens[^1] as ValuePosToken) != null))
                            goto afterComparisonAndLogicalGrouping;
                        var comparison = new LogicalOperationValuePosToken(left, right, (OperationPosToken)tokens[^2],
                            (left.Position.Start, wouldEnd ?? right.Position.End));
                        tokens.RemoveFrom(tokens.Count - 3);
                        tokens.Add(comparison);
                    }
                }

                afterComparisonAndLogicalGrouping: ;

                if (posToken is BlockPosToken { Type: TokenType.SimpleBlockEnd } &&
                    tokens[^1] is { Type: TokenType.EqualityOperationGroup or TokenType.LogicalOperationGroup } &&
                    tokens[^2].Type == TokenType.SimpleBlockStart)
                {
                    tokens.RemoveAt(tokens.Count - 2);
                    dontDoSimpleBlockEndCheck = true;
                    dontAddCurrent = true;
                }

                if (posToken is BlockPosToken
                    {
                        Type: TokenType.SimpleBlockEnd
                    } blockToken)
                {
                    int startIndex = -1;
                    if (!dontDoSimpleBlockEndCheck)
                        startIndex = tokens.FindIndex(
                            t => t is BlockPosToken { Type: TokenType.SimpleBlockStart } bpt &&
                                 bpt.Number == blockToken.Number);
                    else
                    {
                        if (!(tokens[^1].Type is TokenType.DumbShit or TokenType.EqualityOperationGroup
                                or TokenType.LogicalOperationGroup))
                            throw new();
                        var m = tokens[^1];
                        var nameToken = tokens[^2];
                        if (!(nameToken.Type == TokenType.Keyword || nameToken.Type == TokenType.ArrayLiteralStart ||
                              nameToken.IsDotJoinableSomething()))
                            goto afterCallLikePosTokenThing;
                        tokens.RemoveAt(tokens.Count - 1);
                        tokens.RemoveAt(tokens.Count - 1);
                        tokens.Add(new CallLikePosToken(TokenType.KeywordCall,
                            (nameToken.Position.Start, m.Position.End),
                            new PosToken[][] { new PosToken[] { m } },
                            nameToken.Position.End, nameToken.ToString(text)
                        ));
                        goto afterCallLikePosTokenThing;
                    }

                    if (tokens[startIndex - 1] is
                        {
                            Type: TokenType.Keyword or TokenType.ArrayLiteralStart
                        }
                        || tokens[startIndex - 1].IsDotJoinableSomething())
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
            if (t[i].Type == TokenType.Keyword && t[i].EqualsString(text, "class"))
            {
                var name = ((KeywordToken)t[i + 1]).String;
                Dictionary<string, Function> functions = null;
                // todo: use array
                List<string>? propertyNames = null;
                List<PosToken[]?>? propertyInitializers = null;
                // get properties and functions
                var body = (CodeBlockToken)t[i + 2];
                for (var j = 1; j < body.Lines.Count - 1; j++)
                {
                    if (body.Lines[j].Type == LineType.Function)
                    {
                        var tokenLine = (TokenLine)body.Lines[j];
                        var f = DoFunction(tokenLine.Tokens);
                        functions ??= new(StringComparer.InvariantCultureIgnoreCase);
                        functions[f.name] = new Function((CodeBlockToken)tokenLine.Tokens[2], f.arguments, fileScope);
                        j++;
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
                }

                if (returnDescriptive)
                    lines.Add(new TokenLine(new[] { t[i], t[i + 1], t[i + 2] }, LineType.ClassDefinition));
                i += 2;
                fileScope.ClassDefinitions ??= new();
                fileScope.ClassDefinitions.Add(
                    new ClassDefinition(name, propertyNames?.ToArray(), propertyInitializers?.ToArray())
                        { Functions = functions });
            }
            // function
            else if (tokens[i].Type == TokenType.Keyword && tokens[i].EqualsString(text, "func"))
            {
                var (name, arguments) = DoFunction(tokens, i);
                fileScope.Functions ??= new(StringComparer.InvariantCultureIgnoreCase);
                fileScope.Functions[name] = new Function((CodeBlockToken)tokens[i + 2], arguments, fileScope);
                if (returnDescriptive)
                    lines.Add(new TokenLine(tokens.GetRangeAsArray(i..(i + 3)), LineType.Function));
                i += 2;
            }
            else
                lines.Add(GetLine(t, ref i, text));
        }

        return new RCaronRunnerContext(fileScope);
    }

    /// <summary>
    /// If there is a newline(\n) between the two tokens, returns true.
    /// </summary>
    /// <param name="text">Raw code</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool _isNewLineBetweenTokens(PosToken first, PosToken second, string text)
    {
        for (var j = first.Position.End; j < second.Position.Start; j++)
            if (text[j] == '\n')
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

    public static Line GetLine(PosToken[] tokens, ref int i, in string text)
    {
        Line? res = default;
        var callToken = tokens[i] as CallLikePosToken;
        // variable assignment
        if (tokens[i] is VariableToken && tokens[i + 1].Type == TokenType.Operation &&
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
        else if ((tokens[i].IsLiteral() || //tokens[i].Type == TokenType.CodeBlock ||
                  (tokens[i].Type == TokenType.Keyword && tokens[i].EqualsStringCaseInsensitive(text, "default"))) &&
                 tokens[i + 1].Type == TokenType.CodeBlock)
        {
            res = new TokenLine(tokens[i..(i + 2)], LineType.SwitchCase);
            i++;
        }
        // property without initializer in class
        else if (tokens[i].Type == TokenType.VariableIdentifier && tokens[i + 1].Type == TokenType.LineEnding)
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
        else if (tokens.Length - i > 1 && tokens[i].Type is TokenType.ExternThing or TokenType.DotGroup
                                       && tokens[i + 1].Type == TokenType.Operation
                                       && tokens[i + 1].EqualsString(text, "="))
        {
            var endingIndex = _findLineEnding(tokens, i, text);
            res = new TokenLine(tokens[i..(endingIndex.toUse)], LineType.AssignerAssignment);
            i = endingIndex.toSet;
        }
        // unary operation
        else if (tokens[i].Type == TokenType.VariableIdentifier && tokens[i + 1].Type == TokenType.UnaryOperation)
        {
            res = new TokenLine(tokens[i..(i + 2)], LineType.UnaryOperation);
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
            var initializer = GetLine(callToken.Arguments[0], ref falseI, text);
            falseI = 0;
            var iterator = GetLine(callToken.Arguments[2], ref falseI, text);
            return new ForLoopLine(callToken, initializer, iterator, (CodeBlockToken)tokens[++i]);
        }
        // qfor loop
        else if (callToken is { Type: TokenType.KeywordCall } && callToken.NameEquals(text, "qfor"))
        {
            var falseI = 0;
            var initializer = GetLine(callToken.Arguments[0], ref falseI, text);
            falseI = 0;
            var iterator = GetLine(callToken.Arguments[2], ref falseI, text);
            return new ForLoopLine(callToken, initializer, iterator, (CodeBlockToken)tokens[++i],
                LineType.QuickForLoop);
        }
        // foreach loop
        else if (callToken is { Type: TokenType.KeywordCall } && callToken.NameEquals(text, "foreach"))
        {
            return new TokenLine(new[] { tokens[i], tokens[++i] }, LineType.ForeachLoop);
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

            throw new RCaronException($"invalid line at line {lineNumber}", RCaronExceptionCode.ParseInvalidLine);
        }

        return res;
    }

    public static (string name, FunctionArgument[]? arguments) DoFunction(IList<PosToken> tokens, int offset = 0)
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
                    arguments[j].DefaultValue = EvaluateConstantToken(cur[2]);
                }
            }
        }
        else if (tokens[offset + 1] is KeywordToken keywordToken)
            name = keywordToken.String;
        else
            throw new Exception("Invalid function name token");

        return (name, arguments);
    }

    public static object? EvaluateConstantToken(PosToken token)
        => token switch
        {
            // todo(current): in TokenReader make true, false and null into ConstTokens instead of variables
            VariableToken { Name: "true" } => true,
            VariableToken { Name: "false" } => false,
            VariableToken { Name: "null" } => null,
            ConstToken constToken => constToken.Value,
        };
}

public record RCaronRunnerContext(FileScope FileScope);

[Flags]
public enum RCaronRunnerLog
{
    None = 0,
    FunnyColors = 1 << 0,
    FunnyColorsBrackets = 1 << 1,
}