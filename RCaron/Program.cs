// See https://aka.ms/new-console-template for more information


using System.Diagnostics;
using BenchmarkDotNet.Running;
using Log73;
using RCaron;
using RCaron.Benchmarks;
using Log = Log73.Console;
using Console = System.Console;

// Log.Options.LogLevel = LogLevel.Debug;
Log.Configure.EnableVirtualTerminalProcessing();

// BenchmarkRunner.Run<LineNumberBenchmark>();
// BenchmarkRunner.Run<RCaronBenchmarks>();
// return;

Console.WriteLine("Hello, World!");
var stopwatch = Stopwatch.StartNew();

// var text = @"$hello0 = 'text';
// $hello1 = 'text';
// $hello2 = 'text';
// $hello3 = 'text';
// println 123;
// println 123 + 123;
// println 0.123;
// if ($hello3 == 'text') {
//     $hello3 = 'fun';
//     if ($hello3 == 'fun') {
//         $hello3 = 'funny';
//     }
// }
// if ($hello3 != 'funny') {
//     $hello3 = 'wtf';
// }
// if ($hello3 != 'wtf') {
//     $hello3 = 'yeehaw';
// }
// println 'welcome to hell';
// println $hello3;
// dbg_println $hello3;
// ";
// = (9 + 2) * 2 = 11 * 2 = 22 
// var text = @"$hello0 = ((3 * 3) + 2) * 2;
// println $hello0;
// $hello0 = ((3 * 3) + 2) * 2;
// println $hello0;
// ";
// var text = @"println 2 * (3 + 2);";
var opt = new MotorOptions()
{
    EnableDumb = true
};
var text = @"$a = 0; $b = 1; $c = 0;
$c = $a + $b;
println $c;
$a = $b;
$b = $c;
goto_line 2;";
Console.WriteLine(text);
Console.WriteLine("===============================");
bruh: ;
RCaronRunner.Run(text, opt);
RCaronRunner.Run("$h = 2 * (2 + 3);");
// while (true)
// {
//     stopwatch = Stopwatch.StartNew();
//     RCaronRunner.Run(text);
//     Console.WriteLine(stopwatch.ElapsedMilliseconds + "ms");
//     Console.Out.Flush();
// }
// Console.WriteLine(runtime.ElapsedMilliseconds);
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