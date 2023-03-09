using BenchmarkDotNet.Attributes;
using RCaron.Parsing;

namespace RCaron.Benchmarks.Benchmarks;

public static class LNB
{
    public static List<PosToken> tokens;
    public static string text;
    public static int i;
}

[MemoryDiagnoser]
public class LineNumberBenchmark
{
    [GlobalSetup]
    public void Setup()
    {
        var text = @"$hello0 =           'aaa yee haawww hello worldy';
$hello1 =          'aaa yee haawww hello worldy';
$hello2 =         'aaa yee haawww hello worldy';
$hello3 =         'aaa yee haawww hello worldy';
$hello4 =        'aaa yee haawww hello worldy';
$hello5 =       'aaa yee haawww hello worldy';
$hello6 =      'aaa yee haawww hello worldy';
$hello7 =     'aaa yee haawww hello worldy';
$hello8 'a'     'aaa yee haawww hello worldy';";
        Console.WriteLine(text);
        Console.WriteLine("===============================");
        var tokens = new List<PosToken>();
        var reader = new TokenReader(text, RCaronParser.DefaultThrowHandler);
        var token = reader.Read();
        while (token != null)
        {
            if (token is PosToken posToken)
            {
                tokens.Add(posToken);
                Console.ForegroundColor = posToken.Type switch
                {
                    TokenType.ComparisonOperation => ConsoleColor.Black,
                    TokenType.String => ConsoleColor.Blue,
                    TokenType.Number => ConsoleColor.Cyan,
                    TokenType.VariableIdentifier => ConsoleColor.Green,
                    _ => ConsoleColor.Black,
                };
                Console.Write(posToken.ToString(text));
                // Console.WriteLine($"{posToken.Type}: {posToken.ToString(text)}");
            }

            token = reader.Read();
        }

        Console.WriteLine();
        Console.Out.Flush();
        tokens.RemoveAll(t => t.Type == TokenType.Whitespace || t.Type == TokenType.Comment);
        LNB.i = 32;
        LNB.tokens = tokens;
        LNB.text = text;
    }
    [Benchmark]
    public void For()
    {
        var lineNumber = 0;
        var pos = LNB.tokens[LNB.i].Position.Start;
        for (var index = 0; index < pos; ++index)
        {
            if (LNB.text[index] == '\n')
                lineNumber++;
        }
    }

    [Benchmark]
    public void Linq()
    {
        var pos = LNB.tokens[LNB.i].Position.Start;
        var lineNumber = LNB.text[..pos].Count(ch => ch == '\n');
    }

    [Benchmark]
    public void While()
    {
        var lineNumber = -1;
        var pos = LNB.tokens[LNB.i].Position.Start;
        var linesEn = LNB.text.AsSpan().EnumerateLines();
        var hgtrfdews = 0;
        while(linesEn.MoveNext())
        {
            hgtrfdews += linesEn.Current.Length;
            if(hgtrfdews == pos)
                break;
            lineNumber++;
        }
    }
}