using System.Runtime.InteropServices;
using RCaron.Classes;

namespace RCaron;

public static class RCaronRunner
{
    public static RCaronRunnerLog GlobalLog = RCaronRunnerLog.None;

    public static Motor Run(string text, MotorOptions? motorOptions = null)
    {
        var ctx = Parse(text);
        var motor = new Motor(ctx, motorOptions);
        motor.Run();
        return motor;
    }

    public static RCaronRunnerContext Parse(string text, bool returnIgnored = false)
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
                codeBlockLines.Add(GetLine(range, ref i, text));
            }

            tokens.Add(new CodeBlockToken(codeBlockLines));
        }

        while (token != null)
        {
            if (token.Type == TokenType.Whitespace || token.Type == TokenType.Comment || token.Type == TokenType.Ignore)
            {
                if (GlobalLog.HasFlag(RCaronRunnerLog.FunnyColors))
                    Console.Write(token.ToString(text));
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

                var dontAddCurrent = false;
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
                           (tokens[i] is { Type: TokenType.Dot or TokenType.Indexer } ||
                            (tokens[i].Type == TokenType.Colon && tokens[i-1].Type == TokenType.ExternThing ) ||
                            (tokens[i-1].Type == TokenType.Colon && tokens[i-2].Type == TokenType.ExternThing ) ||
                            tokens[i - 1] is { Type: TokenType.Dot or TokenType.Indexer }))
                        // while ((i != 0 && i != -1) && 
                        //        tokens[i] is ValuePosToken && tokens[i - 1] is ValuePosToken && 
                        //        (tokens[i] is {Type: TokenType.Operator} || tokens[i-1] is {Type: TokenType.Operator})
                        //       )
                        i--;
                    if (tokens.Count - i == 1)
                        return (-1, Array.Empty<PosToken>());
                    return (i, tokens.Take(i..).ToArray());
                }

                if (tokens.Count > 2 && tokens[^1].IsDotJoinableSomething() &&
                    (tokens[^1].Type != TokenType.Dot && tokens[^1].Type != TokenType.Colon) &&
                    !posToken.IsDotJoinableSomething() && posToken.Type != TokenType.IndexerStart &&
                    posToken.Type != TokenType.SimpleBlockStart
                   )
                {
                    // todo(perf): it gets here when array literal
                    var h = BackwardsCollectDotThing();
                    if (h.index != -1 && h.tokens.Length != 1 && h.tokens.Length != 0)
                    {
                        // may not be needed?
                        // if (h.tokens[1].Type != TokenType.Dot)
                        //     goto beforeAdd;
                        // AAAAAA
                        // remove those replace with fucking imposter thing
                        var rem = h.index;
                        // if (rem < 1 || tokens[rem].Type != TokenType.SimpleBlockStart ||
                        //     posToken.Type != TokenType.SimpleBlockEnd)
                        //     rem += 1;
                        // if (!tokens[rem - 1].IsDotJoinableSomething() &&
                        //     tokens[rem] is not ValuePosToken)
                        //     goto beforeAdd;
                        tokens.RemoveFrom(rem);
                        tokens.Add(new DotGroupPosToken(TokenType.DotGroup,
                            (h.tokens.First().Position.Start, h.tokens.Last().Position.End), h.tokens));
                        // goto beforeAdd;
                    }
                    else
                    {
                        // if (h.index != -1)
                        //     Debugger.Break();
                    }
                }


                (int index, ValuePosToken[] tokens) BackwardsCollectValuePosToken()
                {
                    var i = tokens.Count - 1;
                    while ((i != 0 && i != -1) &&
                           tokens[i] is ValuePosToken && tokens[i - 1] is ValuePosToken &&
                           (tokens[i] is { Type: TokenType.MathOperator } ||
                            tokens[i - 1] is { Type: TokenType.MathOperator }))
                        // while ((i != 0 && i != -1) && 
                        //        tokens[i] is ValuePosToken && tokens[i - 1] is ValuePosToken && 
                        //        (tokens[i] is {Type: TokenType.Operator} || tokens[i-1] is {Type: TokenType.Operator})
                        //       )
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
                        // if (posToken is { Type: TokenType.SimpleBlockEnd })
                        // {
                        //     dontDoSimpleBlockEndCheck = true;
                        //     dontAddCurrent = true;
                        // }
                    }
                    else
                    {
                        // if (h.index != -1)
                        //     Debugger.Break();
                    }
                }

                // comparison and logical operation grouping
                if (tokens.Count > 2 && token is not ValuePosToken && !token.IsDotJoinableSomething() &&
                    token.Type != TokenType.MathOperator && tokens[^1] is ValuePosToken right &&
                    tokens[^3] is ValuePosToken left)
                {
                    var ranComparison = false;
                    // comparison group
                    if (tokens[^2].Type == TokenType.ComparisonOperation)
                    {
                        var comparison = new ComparisonValuePosToken(left, right, tokens[^2]);
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
                        var comparison = new LogicalOperationValuePosToken(left, right, tokens[^2]);
                        tokens.RemoveFrom(tokens.Count - 3);
                        tokens.Add(comparison);
                    }
                }

                afterComparisonAndLogicalGrouping: ;

                if ( /*!dontDoSimpleBlockEndCheck && */posToken is BlockPosToken
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
                        var m = (MathValueGroupPosToken)tokens[^1];
                        var nameToken = tokens[^2];
                        if (!(nameToken.Type == TokenType.Keyword || nameToken.Type == TokenType.ArrayLiteralStart ||
                              nameToken.IsDotJoinableSomething()))
                            goto afterCallLikePosTokenThing;
                        tokens.RemoveAt(tokens.Count - 1);
                        tokens.RemoveAt(tokens.Count - 1);
                        tokens.Add(new CallLikePosToken(TokenType.KeywordCall,
                            (nameToken.Position.Start, m.Position.End),
                            new PosToken[][] { m.ValueTokens },
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
                        var c = 1;
                        // count commas in tks
                        for (var i = 0; i < tks.Length; i++)
                        {
                            if (tks[i].Type == TokenType.Comma) c++;
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
                                if (tks[ind].Type == TokenType.Comma) break;
                            }

                            args[i] = tks[..(ind != -1 ? new Index(ind) : Index.End)].ToArray();
                            // comma was not found
                            if (ind != tks.Length)
                                tks = tks[(ind + 1)..];
                        }

                        var h = new CallLikePosToken(TokenType.KeywordCall,
                            (tokens[startIndex - 1].Position.Start, posToken.Position.End), args,
                            // new[]
                            // {
                            //     tokens.GetRange((startIndex + 1)..).ToArray()
                            // }
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
                afterAdd: ;
                if (GlobalLog.HasFlag(RCaronRunnerLog.FunnyColors))
                {
                    Console.ForegroundColor = posToken.Type switch
                    {
                        TokenType.Operation => ConsoleColor.Black,
                        TokenType.String => ConsoleColor.Blue,
                        TokenType.Number => ConsoleColor.Cyan,
                        TokenType.DecimalNumber => ConsoleColor.Cyan,
                        TokenType.VariableIdentifier => ConsoleColor.Green,
                        TokenType.Keyword or TokenType.ExternThing => ConsoleColor.Magenta,
                        _ => ConsoleColor.Black,
                    };
                    Console.Write(posToken.ToString(text));
                    if (GlobalLog.HasFlag(RCaronRunnerLog.FunnyColorsBrackets) && posToken is BlockPosToken bpt)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write($"({bpt.Depth}, {bpt.Number})");
                    }
                }
            }

            token = reader.Read();
        }

        DoCodeBlockToken();

        if (GlobalLog.HasFlag(RCaronRunnerLog.FunnyColors))
        {
            Console.WriteLine();
            Console.ResetColor();
            Console.Out.Flush();
        }

        // find lines
        var lines = new List<Line>();
        List<ClassDefinition> classDefinitions = null;
        var t = tokens.ToArray();
        for (var i = 0; i < tokens.Count; i++)
        {
            // class definition
            if (t[i].Type == TokenType.Keyword && t[i].EqualsString(text, "class"))
            {
                var name = ((KeywordToken)t[i + 1]).String;
                Dictionary<string, CodeBlockToken> functions = null;
                // todo: use array
                List<string>? propertyNames = null;
                List<PosToken[]?>? propertyInitializers = null;
                // get properties and functions
                var body = (CodeBlockToken)t[i + 2];
                for (var j = 1; j < body.Lines.Count - 1; j++)
                {
                    if (body.Lines[j].Type == LineType.Function)
                    {
                        var funcName = ((CallLikePosToken)((TokenLine)body.Lines[j]).Tokens[1]).Name;
                        functions ??= new(StringComparer.InvariantCultureIgnoreCase);
                        functions[funcName] = ((CodeBlockLine)body.Lines[j + 1]).Token;
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

                i += 2;
                classDefinitions ??= new();
                classDefinitions.Add(
                    new ClassDefinition(name, propertyNames?.ToArray(), propertyInitializers?.ToArray())
                        { Functions = functions });
            }
            else
                lines.Add(GetLine(t, ref i, text));
        }

        return new RCaronRunnerContext(text, lines, classDefinitions);
    }

    public static Line GetLine(PosToken[] tokens, ref int i, in string text)
    {
        Line? res = default;
        var callToken = tokens[i] as CallLikePosToken;
        // variable assignment
        if (tokens[i] is VariableToken && tokens[i + 1].Type == TokenType.Operation &&
            tokens[i + 1].EqualsString(text, "="))
        {
            var endingIndex = Array.FindIndex(tokens, i, t => t.Type == TokenType.LineEnding);
            if (endingIndex == -1)
                endingIndex = tokens.Length;
            res = new TokenLine(tokens[i..(endingIndex)], LineType.VariableAssignment);
            i = endingIndex;
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
        // assigner assignment
        else if (tokens[i].Type is TokenType.ExternThing or TokenType.DotGroup
                 && tokens[i + 1].Type == TokenType.Operation
                 && tokens[i + 1].EqualsString(text, "="))
        {
            var endingIndex = Array.FindIndex(tokens, i, t => t.Type == TokenType.LineEnding);
            if (endingIndex == -1)
                endingIndex = tokens.Length;
            res = new TokenLine(tokens[i..(endingIndex)], LineType.AssignerAssignment);
            i = endingIndex;
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
            return new TokenLine(new[] { tokens[i] }, LineType.IfStatement);
        }
        // else if statement
        else if (tokens[i].EqualsString(text, "else") && tokens[i + 1] is CallLikePosToken ifCallToken &&
                 ifCallToken.NameEquals(text, "if"))
        {
            res = new TokenLine(new[] { tokens[i], tokens[i + 1] }, LineType.ElseIfStatement);
            i++;
        }
        // else statement
        else if (tokens[i].EqualsString(text, "else"))
        {
            return new TokenLine(new[] { tokens[i] }, LineType.ElseStatement);
        }
        // loop loop
        else if (tokens[i] is KeywordToken keywordToken && tokens[i].EqualsString(text, "loop"))
        {
            return new SingleTokenLine(tokens[i], LineType.LoopLoop);
        }
        // while loop
        else if (callToken is { Type: TokenType.KeywordCall } && callToken.NameEquals(text, "while"))
        {
            return new TokenLine(new[] { tokens[i] }, LineType.WhileLoop);
        }
        // do while loop
        else if (callToken is { Type: TokenType.KeywordCall } && callToken.NameEquals(text, "dowhile"))
        {
            return new TokenLine(new[] { tokens[i] }, LineType.DoWhileLoop);
        }
        // for loop
        else if (callToken is { Type: TokenType.KeywordCall } && callToken.NameEquals(text, "for"))
        {
            var falseI = 0;
            var initializer = GetLine(callToken.Arguments[0], ref falseI, text);
            falseI = 0;
            var iterator = GetLine(callToken.Arguments[2], ref falseI, text);
            return new ForLoopLine(callToken, initializer, iterator);
        }
        // qfor loop
        else if (callToken is { Type: TokenType.KeywordCall } && callToken.NameEquals(text, "qfor"))
        {
            var falseI = 0;
            var initializer = GetLine(callToken.Arguments[0], ref falseI, text);
            falseI = 0;
            var iterator = GetLine(callToken.Arguments[2], ref falseI, text);
            return new ForLoopLine(callToken, initializer, iterator, LineType.QuickForLoop);
        }
        // foreach loop
        else if (callToken is { Type: TokenType.KeywordCall } && callToken.NameEquals(text, "foreach"))
        {
            return new TokenLine(new[] { tokens[i] }, LineType.ForeachLoop);
        }
        // function
        else if (tokens[i].Type == TokenType.Keyword && tokens[i].EqualsString(text, "func"))
        {
            res = new TokenLine(tokens[i..(i + 2)], LineType.Function);
            i += 1;
        }
        else if (tokens[i] is { Type: TokenType.BlockStart or TokenType.BlockEnd })
        {
            res = new TokenLine(new[] { tokens[i] }, LineType.BlockStuff);
        }
        // keyword plain call
        else if (tokens[i].Type == TokenType.Keyword)
        {
            // check if keyword is lone keyword -- dont have to -- bruh
            var endingIndex = Array.FindIndex(tokens, i, t => t.Type == TokenType.LineEnding);
            res = new TokenLine(
                tokens[i..(endingIndex)],
                LineType.KeywordPlainCall);
            i = endingIndex;
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
            i++;
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
}

public record RCaronRunnerContext(string Code, IList<Line> Lines, List<ClassDefinition>? ClassDefinitions);

[Flags]
public enum RCaronRunnerLog
{
    None = 0,
    FunnyColors = 1 << 0,
    FunnyColorsBrackets = 1 << 1,
}