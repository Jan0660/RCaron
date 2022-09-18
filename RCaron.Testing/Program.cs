﻿// See https://aka.ms/new-console-template for more information


using System.Diagnostics;
using BenchmarkDotNet.Configs;
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
// BenchmarkRunner.Run<TypeResolverBenchmarks>();
// return;

for (var i = 0; i < 5_000_000; i++)
{
    RCaronRunner.Parse(@"$h = 2 * (2 + 3);");
}
// Debugger.Break();
// for (var i = 0; i < 1_000_000; i++)
// {
//     RCaronRunner.Parse(@"$h = 2 * (2 + 3);");
// }
return;

Console.WriteLine("Hello, World!");
var stopwatch = Stopwatch.StartNew();

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
// var text = @"$a = 0; $b = 1; $c = 0;
// $c = $a + $b;
// println $c;
// $a = $b;
// $b = $c;
// goto_line 2;";
// var text = @"loop {
// println 'balls'; println 'balls'; println 'balls'; break;
// }";

// var text = @"
// $a = 0; $b = 1; $c = 0;
//
// func DoMath{
//     $c = $a + $b;
// }
//
// for($i = 0, $i < 90, $i = $i + 1) {
//     DoMath;
//     println $i;
//     println $c;
//     $a = $b;
//     $b = $c;
// }";
var text = @"
#RCaron.Testing.StaticDummy.Field = 1;
print 'f' #RCaron.Testing.StaticDummy.Field;
";
// $g = @(1, 2).Length;
// print $g;
Console.WriteLine(text);
Console.WriteLine("===============================");
bruh: ;
RCaronRunner.GlobalLog = RCaronRunnerLog.FunnyColors;
var m = RCaronRunner.Run(text, opt);
var range = new RCaronRange(0, 10);
foreach (var num in range)
{
    Console.WriteLine(num);
}