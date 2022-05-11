// See https://aka.ms/new-console-template for more information


using System.Diagnostics;
using Log73;
using RCaron;
using Log = Log73.Console;
using Console = System.Console;

Log.Options.LogLevel = LogLevel.Debug;
Log.Configure.EnableVirtualTerminalProcessing();

// BenchmarkRunner.Run<LineNumberBenchmark>();

Console.WriteLine("Hello, World!");
var stopwatch = Stopwatch.StartNew();

var text = @"$hello0 = 'text';
$hello1 = 'text';
$hello2 = 'text';
$hello3 = 'text';
println 123;
println 0.123;
if ($hello3 == 'text') {
    $hello3 = 'fun';
    if ($hello3 == 'fun') {
        $hello3 = 'funny';
    }
}
if ($hello3 != 'funny') {
    $hello3 = 'wtf';
}
if ($hello3 != 'wtf') {
    $hello3 = 'yeehaw';
}
println 'welcome to hell';
println $hello3;
dbg_println $hello3;
";
Console.WriteLine(text);
Console.WriteLine("===============================");
bruh: ;
var tokens = new List<PosToken>();
var reader = new TokenReader(text);
var token = reader.Read();
var blockDepth = -1;
var blockNumber = -1;
while (token != null)
{
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

        tokens.Add(posToken);
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
        if (posToken is BlockPosToken bpt)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($"({bpt.Depth}, {bpt.Number})");
        }
        // Console.WriteLine($"{posToken.Type}: {posToken.ToString(text)}");
    }

    token = reader.Read();
}

Console.WriteLine();
Console.ResetColor();
Console.Out.Flush();
tokens.RemoveAll(t => t.Type == TokenType.Whitespace);

// find lines
var lines = new List<Line>();
for (var i = 0; i < tokens.Count; i++)
{
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
    else if (tokens[i].Type == TokenType.Keyword && tokens[i].ToString(text) == "if")
    {
        // var startingSimpleBlock = tokens[i + 1];
        var endingSimpleBlockIndex = tokens.IndexOf(tokens.Skip(i).First(t => t.Type == TokenType.SimpleBlockEnd));
        // var endingSimpleBlock = tokens[endingSimpleBlockIndex];
        lines.Add(new Line(tokens.GetRange((i), endingSimpleBlockIndex - i + 1).ToArray(), LineType.IfStatement));
        i = endingSimpleBlockIndex;
        // Console.WriteLine(text[startingSimpleBlock.Position.Start..endingSimpleBlock.Position.End]);
    }
    else if (tokens[i] is { Type: TokenType.BlockStart or TokenType.BlockEnd })
    {
        lines.Add(new Line(new[] { tokens[i] }, LineType.BlockStuff));
    }
    // keyword plain call
    else if (tokens[i].Type == TokenType.Keyword)
    {
        var endingIndex = tokens.IndexOf(tokens.Skip(i).First(t => t.Type == TokenType.LineEnding));
        lines.Add(new Line(
            tokens.Take(i..(endingIndex)).ToArray(),
            LineType.KeywordPlainCall));
        i = endingIndex;
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

        Console.Error.WriteLine("Invalid line at line {0}", lineNumber);
        Environment.Exit(-1);
    }
}

Console.WriteLine();
Console.ResetColor();

for (var i = 0; i < lines.Count; i++)
{
    Console.WriteLine(
        $"{i}({lines[i].Type}) {text[lines[i].Tokens[0].Position.Start..lines[i].Tokens.Last().Position.End]}");
}

var motor = new Motor(lines.ToArray())
{
    Raw = text
};
motor.Run();
while (true)
{
    text = Console.ReadLine() + ";";
    goto bruh;
}


namespace RCaron
{
    public enum EnumBlockType : byte
    {
        Normal,
    }
}

// ref struct FunnySpanReader
// {
//     public ReadOnlySpan<char> Span;
//     public FunnySpanReader(ReadOnlySpan<char> span) => Span = span;
//     public void Skip(int count) => Span = Span.Slice(count);
// }