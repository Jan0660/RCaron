using System.Diagnostics;
using System.Runtime.InteropServices;

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

    public static RCaronRunnerContext Parse(string text)
    {
        var tokens = new List<PosToken>();
        var reader = new TokenReader(text);
        var token = reader.Read();
        var blockDepth = -1;
        var blockNumber = -1;
        while (token != null)
        {
            if (token.Type == TokenType.Whitespace || token.Type == TokenType.Comment)
            {
                if (GlobalLog.HasFlag(RCaronRunnerLog.FunnyColors))
                    Console.Write(token.ToString(text));
                token = reader.Read();
                continue;
            }

            // for some reason, when not doing this, there is literally more memory allocated
            if (token is PosToken posToken)
            {
                switch (posToken)
                {
                    case BlockPosToken { Type: TokenType.BlockStart or TokenType.SimpleBlockStart } blockPosToken:
                        blockDepth++;
                        blockNumber++;
                        blockPosToken.Depth = blockDepth;
                        blockPosToken.Number = blockNumber;
                        break;
                    case BlockPosToken { Type: TokenType.BlockEnd or TokenType.SimpleBlockEnd } blockPosToken:
                        blockPosToken.Depth = blockDepth;
                        blockPosToken.Number =
                            ((BlockPosToken)tokens.Last(t => t is BlockPosToken bpt && bpt.Depth == blockDepth)).Number;
                        blockDepth--;
                        break;
                }

                if (posToken is BlockPosToken { Type: TokenType.SimpleBlockEnd } blockToken)
                {
                    var startIndex = tokens.FindIndex(
                        t => t is BlockPosToken { Type: TokenType.SimpleBlockStart } bpt &&
                             bpt.Number == blockToken.Number);
                    if (tokens[startIndex - 1] is { Type: TokenType.Keyword or TokenType.ExternThing or TokenType.VariableIdentifier })
                    {
                        // todo: dear lord
                        var tks = CollectionsMarshal.AsSpan(tokens)[(startIndex + 1)..];
                        var c = 1;
                        // count commas in tks
                        for (var i = 0; i < tks.Length; i++)
                        {
                            if (tks[i].Type == TokenType.Comma) c++;
                        }

                        var args = new PosToken[c][];
                        for (byte i = 0; i < c; i++)
                        {
                            // find index of next comma
                            var ind = 0;
                            for (; ind < tks.Length; ind++)
                            {
                                if (tks[ind].Type == TokenType.Comma) break;
                            }

                            // todo: maybe doesn't need to be set on every iteration?
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
                            tokens[startIndex - 1].Position.End, tokens[startIndex - 1].Type);
                        tokens.RemoveFrom(startIndex - 1);
                        tokens.Add(h);
                        goto afterAdd;
                    }
                }

                (int index, ValuePosToken[] tokens) BackwardsCollectValuePosToken()
                {
                    var i = tokens.Count - 1;
                    while ((i != 0 && i != -1) &&
                           tokens[i] is ValuePosToken && tokens[i - 1] is ValuePosToken &&
                           (tokens[i] is { Type: TokenType.Operator } || tokens[i - 1] is { Type: TokenType.Operator }))
                        // while ((i != 0 && i != -1) && 
                        //        tokens[i] is ValuePosToken && tokens[i - 1] is ValuePosToken && 
                        //        (tokens[i] is {Type: TokenType.Operator} || tokens[i-1] is {Type: TokenType.Operator})
                        //       )
                        i--;
                    if ((tokens.Count - i) % 2 == 0 || tokens.Count - i == 1)
                        return (-1, Array.Empty<ValuePosToken>());
                    return (i, tokens.Take(i..).Cast<ValuePosToken>().ToArray());
                }

                if (tokens.Count > 2 && tokens[^1] is ValuePosToken && tokens[^1].Type != TokenType.Operator &&
                     posToken.Type != TokenType.Operator
                )
                {
                    var h = BackwardsCollectValuePosToken();
                    if (h.index != -1 && h.tokens.Length != 1 && h.tokens.Length != 0 && h.tokens.Length != 2)
                    {
                        // may not be needed?
                        if (h.tokens[1] is not { Type: TokenType.Operator })
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
                        tokens.RemoveFrom(rem);
                        tokens.Add(new ValueGroupPosToken(TokenType.DumbShit,
                            (h.tokens.First().Position.Start, h.tokens.Last().Position.End), h.tokens));
                        if (posToken is { Type: TokenType.SimpleBlockEnd })
                            goto afterAdd;
                    }
                    else
                    {
                        // if (h.index != -1)
                        //     Debugger.Break();
                    }
                }

                beforeAdd: ;
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

        if (GlobalLog.HasFlag(RCaronRunnerLog.FunnyColors))
        {
            Console.WriteLine();
            Console.ResetColor();
            Console.Out.Flush();
        }

// find lines
        var lines = new List<Line>();
        // todo
        var t = tokens.ToArray();
        for (var i = 0; i < tokens.Count; i++)
        {
            lines.Add(GetLine(t, ref i, text));
        }

        if (GlobalLog.HasFlag(RCaronRunnerLog.Lines))
        {
            Console.WriteLine();
            Console.ResetColor();

            for (var i = 0; i < lines.Count; i++)
            {
                Console.WriteLine(
                    $"{i}({lines[i].Type}) {text[lines[i].Tokens[0].Position.Start..lines[i].Tokens.Last().Position.End]}");
            }
        }

        return new RCaronRunnerContext()
        {
            Code = text,
            Lines = lines
        };
    }

    // todo: tokens doesn't need to be alive maybee?
    public static Line GetLine(PosToken[] tokens, ref int i, in string text)
    {
        Line? res = default;
        var callToken = tokens[i] as CallLikePosToken;
        // variable assignment
        if (tokens[i].Type == TokenType.VariableIdentifier && tokens[i + 1].Type == TokenType.Operation &&
            tokens[i + 1].EqualsString(text, "="))
        {
            var endingIndex = Array.FindIndex(tokens, i, t => t.Type == TokenType.LineEnding);
            if (endingIndex == -1)
                endingIndex = tokens.Length;
            res = new Line(tokens[i..(endingIndex)], LineType.VariableAssignment);
            i = endingIndex;
        }
        // unary operation
        else if (tokens[i].Type == TokenType.VariableIdentifier && tokens[i + 1].Type == TokenType.UnaryOperation)
        {
            res = new Line(tokens[i..(i + 2)], LineType.UnaryOperation);
            i += 2;
        }
        // if statement
        else if (callToken is { Type: TokenType.KeywordCall } && callToken.NameEquals(text, "if"))
        {
            return new Line(new[] { tokens[i] }, LineType.IfStatement);
        }
        // loop loop
        else if (tokens[i].Type == TokenType.Keyword && tokens[i].EqualsString(text, "loop"))
        {
            return new Line(new[] { tokens[i] }, LineType.LoopLoop);
        }
        // while loop
        else if (callToken is { Type: TokenType.KeywordCall } && callToken.NameEquals(text, "while"))
        {
            return new Line(new[] { tokens[i] }, LineType.WhileLoop);
        }
        // do while loop
        else if (callToken is { Type: TokenType.KeywordCall } && callToken.NameEquals(text, "dowhile"))
        {
            return new Line(new[] { tokens[i] }, LineType.DoWhileLoop);
        }
        // for loop
        else if (callToken is { Type: TokenType.KeywordCall } && callToken.NameEquals(text, "for"))
        {
            return new Line(new[] { tokens[i] }, LineType.ForLoop);
        }
        // function
        else if (tokens[i].Type == TokenType.Keyword && tokens[i].EqualsString(text, "func"))
        {
            res = new Line(tokens[i..(i + 2)], LineType.Function);
            i += 1;
        }
        else if (tokens[i] is { Type: TokenType.BlockStart or TokenType.BlockEnd })
        {
            res = new Line(new[] { tokens[i] }, LineType.BlockStuff);
        }
        // keyword plain call
        else if (tokens[i].Type == TokenType.Keyword)
        {
            // check if keyword is lone keyword -- dont have to -- bruh
            // if (tokens[i].ToString(text) == "loop")
            //     continue;
            var endingIndex = Array.FindIndex(tokens, i, t => t.Type == TokenType.LineEnding);
            res = new Line(
                tokens[i..(endingIndex)],
                LineType.KeywordPlainCall);
            i = endingIndex;
        }
        else if (callToken is not null)
        {
            res = new Line(new[] { tokens[i] }, LineType.KeywordCall);
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

            throw new RCaronException($"Invalid line at line {lineNumber}", RCaronExceptionTime.Parsetime);
        }

        return res.Value;
    }
}

public class RCaronRunnerContext
{
    public string Code { get; set; }
    public IList<Line> Lines { get; set; }
}

[Flags]
public enum RCaronRunnerLog
{
    None = 0,
    FunnyColors = 1 << 0,
    FunnyColorsBrackets = 1 << 1,
    Lines = 1 << 2,
}