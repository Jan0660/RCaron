﻿namespace RCaron;

public static class RCaronRunner
{
    public static RCaronRunnerLog GlobalLog = RCaronRunnerLog.None;

    public static Motor Run(string text, MotorOptions? motorOptions = null)
    {
        var ctx = Parse(text);

        var motor = new Motor(ctx, motorOptions);
        // var runtime = Stopwatch.StartNew();
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
            if (token is PosToken { Type: TokenType.Whitespace })
            {
                if (GlobalLog.HasFlag(RCaronRunnerLog.FunnyColors))
                    Console.Write(token.ToString(text));
                token = reader.Read();
                continue;
            }

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

                (int index, ValuePosToken[] tokens) BackwardsCollectValuePosToken()
                {
                    var i = tokens.Count - 1;
                    while ((i != 0 && i != -1) && tokens[i] is ValuePosToken && tokens[i - 1] is ValuePosToken)
                        i--;
                    return (i, tokens.Take(i..).Cast<ValuePosToken>().ToArray());
                }

                if (posToken is not ValuePosToken && tokens.LastOrDefault() is ValuePosToken)
                {
                    var h = BackwardsCollectValuePosToken();
                    if (h.tokens.Length != 1 && h.tokens.Length != 0)
                    {
                        // AAAAAA
                        // remove those replace with fucking imposter thing
                        var rem = h.index - 1;
                        var g = 0;
                        if (rem < 1 || (tokens[rem] is not BlockPosToken { Type: TokenType.SimpleBlockStart }) || posToken is not BlockPosToken{Type: TokenType.SimpleBlockEnd})
                            rem += 1;
                        if (tokens[rem - 1] is not ValuePosToken && tokens[rem] is not BlockPosToken)
                            goto beforeAdd;
                        tokens.RemoveFrom(rem);
                        tokens.Add(new ValueGroupPosToken(TokenType.DumbShit,
                            (h.tokens.First().Position.Start, h.tokens.Last().Position.End), h.tokens));
                        if (posToken is { Type: TokenType.SimpleBlockEnd })
                            goto afterAdd;
                    }
                }

                if (posToken is BlockPosToken { Type: TokenType.SimpleBlockEnd } blockToken)
                {
                    var startIndex = tokens.FindIndex(
                        t => t is BlockPosToken { Type: TokenType.SimpleBlockStart } bpt &&
                             bpt.Number == blockToken.Number);
                    if (tokens[startIndex - 1] is { Type: TokenType.Keyword })
                    {
                        // todo: dear lord
                        var tks = tokens.GetRange((startIndex + 1)..);
                        var c = tks.Count(t => t.Type == TokenType.Comma) + 1;
                        var args = new PosToken[c][];
                        for (byte i = 0; i < c; i++)
                        {
                            var ind = tks.FindIndex(t => t.Type == TokenType.Comma);
                            args[i] = tks.GetRange(..(ind != -1 ? new Index(ind) : Index.End)).ToArray();
                            if (ind != -1)
                                tks = tks.GetRange((ind + 1)..);
                        }

                        var h = new CallLikePosToken(TokenType.KeywordCall,
                            (tokens[startIndex - 1].Position.Start, posToken.Position.End), args,
                            // new[]
                            // {
                            //     tokens.GetRange((startIndex + 1)..).ToArray()
                            // }
                            tokens[startIndex - 1].Position.End);
                        tokens.RemoveFrom(startIndex - 1);
                        tokens.Add(h);
                        goto afterAdd;
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
                        TokenType.Keyword => ConsoleColor.Magenta,
                        _ => ConsoleColor.Black,
                    };
                    Console.Write(posToken.ToString(text));
                    if (GlobalLog.HasFlag(RCaronRunnerLog.FunnyColorsBrackets) && posToken is BlockPosToken bpt)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write($"({bpt.Depth}, {bpt.Number})");
                    }
                }

                // if (posToken is not { Type: TokenType.Whitespace })
                //     Console.WriteLine($"{posToken.Type}: {posToken.ToString(text)}");
            }

            token = reader.Read();
        }

        if (GlobalLog.HasFlag(RCaronRunnerLog.FunnyColors))
        {
            Console.WriteLine();
            Console.ResetColor();
            Console.Out.Flush();
        }

        // tokens.RemoveAll(t => t.Type == TokenType.Whitespace);

// find lines
        var lines = new List<Line>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var callToken = tokens[i] as CallLikePosToken;
            // variable assignment
            if (tokens[i].Type == TokenType.VariableIdentifier && tokens[i + 1].Type == TokenType.Operation &&
                tokens[i + 1].ToString(text) == "=")
            {
                var endingIndex = tokens.IndexOf(tokens.Skip(i).First(t => t.Type == TokenType.LineEnding));
                lines.Add(new Line(
                    tokens.Take(i..(endingIndex)).ToArray(),
                    LineType.VariableAssignment));
                i = endingIndex;
            }
            // if statement
            else if (callToken is { Type: TokenType.KeywordCall } && callToken.GetName(text) == "if")
            {
                // var endingSimpleBlockIndex =
                //     tokens.IndexOf(tokens.Skip(i).First(t => t.Type == TokenType.SimpleBlockEnd));
                lines.Add(
                    new Line(tokens.GetRange((i), 1).ToArray(), LineType.IfStatement));
                // i = endingSimpleBlockIndex;
            }
            // loop loop
            else if (tokens[i].Type == TokenType.Keyword && tokens[i].ToString(text) == "loop")
            {
                lines.Add(
                    new Line(tokens.GetRange(i, 1).ToArray(), LineType.LoopLoop));
            }
            // while loop
            else if (callToken is { Type: TokenType.KeywordCall } && callToken.GetName(text) == "while")
            {
                lines.Add(
                    new Line(tokens.GetRange((i), 1).ToArray(), LineType.WhileLoop));
            }
            // do while loop
            else if (callToken is { Type: TokenType.KeywordCall } && callToken.GetName(text) == "dowhile")
            {
                lines.Add(
                    new Line(tokens.GetRange((i), 1).ToArray(), LineType.DoWhileLoop));
            }
            // function
            else if (tokens[i].Type == TokenType.Keyword && tokens[i].ToString(text) == "func")
            {
                lines.Add(new Line(tokens.GetRange((i), 2).ToArray(), LineType.Function));
                i += 1;
            }
            else if (tokens[i] is { Type: TokenType.BlockStart or TokenType.BlockEnd })
            {
                lines.Add(new Line(new[] { tokens[i] }, LineType.BlockStuff));
            }
            // keyword plain call
            else if (tokens[i].Type == TokenType.Keyword)
            {
                // check if keyword is lone keyword -- dont have to -- bruh
                // if (tokens[i].ToString(text) == "loop")
                //     continue;
                var endingIndex = tokens.IndexOf(tokens.Skip(i).First(t => t.Type == TokenType.LineEnding));
                lines.Add(new Line(
                    tokens.Take(i..(endingIndex)).ToArray(),
                    LineType.KeywordPlainCall));
                i = endingIndex;
            }
            else if (callToken is not null)
            {
                lines.Add(new Line(tokens.GetRange(i..).ToArray(), LineType.KeywordCall));
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
            Lines = lines.ToArray()
        };
    }
}

public class RCaronRunnerContext
{
    public string Code { get; set; }
    public Line[] Lines { get; set; }
}

[Flags]
public enum RCaronRunnerLog
{
    None = 0,
    FunnyColors = 1 << 0,
    FunnyColorsBrackets = 1 << 1,
    Lines = 1 << 2,
}