using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Log73;
using RCaron.Classes;
using Log = Log73.Console;
using Console = System.Console;

// Log.Options.LogLevel = LogLevel.Debug;
Log.Configure.EnableVirtualTerminalProcessing();

void PrintProps(dynamic d)
    => Console.WriteLine($"Prop1: {d.Prop1}; Prop2: {d.Prop2}");

void AssertProps(ClassInstance instance, dynamic d, string prop1, string prop2)
{
    var prop1Val = (string)d.Prop1;
    var prop2Val = (string)d.Prop2;
    Debug.Assert(prop1Val == prop1);
    Debug.Assert(prop2Val == prop2);
    Debug.Assert(instance.TryGetPropertyValue("prop1", out var prop1Val2));
    Debug.Assert((string)prop1Val2! == prop1);
    Debug.Assert(instance.TryGetPropertyValue("prop2", out var prop2Val2));
    Debug.Assert((string)prop2Val2! == prop2);
}

var def = new ClassDefinition("MyClass", new[] { "Prop1", "Prop2" }, null);
var instance = new ClassInstance(def);
instance.PropertyValues![0] = "value1";
instance.PropertyValues[1] = "value2";
dynamic d = instance;
PrintProps(d);
d.Prop1 = "value1-changed";
AssertProps(instance, d, "value1-changed", "value2");
PrintProps(d);
var second = new ClassInstance(def);
second.PropertyValues![0] = "value1-second";
second.PropertyValues[1] = "value2-second";
var d2 = (dynamic)second;
PrintProps(d2);
d2.Prop1 = "value1-second-changed";
PrintProps(d2);
PrintProps(d);
AssertProps(second, d2, "value1-second-changed", "value2-second");

Console.WriteLine();

var defSwapped = new ClassDefinition("MyClass", new[] { "Prop2", "Prop1" }, null);
var instanceSwapped = new ClassInstance(defSwapped);
instanceSwapped.PropertyValues![0] = "value2-swapped";
instanceSwapped.PropertyValues[1] = "value1-swapped";
dynamic dSwapped = instanceSwapped;
AssertProps(instanceSwapped, dSwapped, "value1-swapped", "value2-swapped");
PrintProps(dSwapped);
dSwapped.Prop1 = "value1-swapped-changed";
PrintProps(dSwapped);
var secondSwapped = new ClassInstance(defSwapped);
secondSwapped.PropertyValues![0] = "value2-second-swapped";
secondSwapped.PropertyValues[1] = "value1-second-swapped";
var d2Swapped = (dynamic)secondSwapped;
PrintProps(d2Swapped);
d2Swapped.Prop1 = "value1-second-swapped-changed";
PrintProps(d2Swapped);
PrintProps(dSwapped);
AssertProps(secondSwapped, d2Swapped, "value1-second-swapped-changed", "value2-second-swapped");
return;
BenchmarkRunner.Run<Benchmarks>();
return;

[MemoryDiagnoser(false)]
public class Benchmarks
{
    private ClassDefinition _def;
    public ClassInstance Instance { get; set; }
    public dynamic Dynamic { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _def = new ClassDefinition("MyClass", new[] { "Prop1", "Prop2" }, null);
        Instance = new ClassInstance(_def);
        Instance.PropertyValues![0] = "value-prop1";
        Instance.PropertyValues[1] = "value-prop2";
        Dynamic = Instance;
    }

    [Benchmark()]
    public object? GetViaDynamic()
    {
        return Dynamic.prop1;
    }

    [Benchmark(Baseline = true)]
    public object? GetViaArrayAccess()
    {
        return Instance.PropertyValues![0];
    }

    [Benchmark]
    public object? GetViaTryGetPropertyValue()
    {
        Instance.TryGetPropertyValue("prop1", out var prop1Val);
        return prop1Val;
    }
}


// // for (var i = 0; i < 5_000_000; i++)
// // {
// //     RCaronRunner.Parse(@"$h = 2 * (2 + 3);");
// // }
// // Debugger.Break();
// // for (var i = 0; i < 1_000_000; i++)
// // {
// //     RCaronRunner.Parse(@"$h = 2 * (2 + 3);");
// // }
// // return;
//
// Console.WriteLine("Hello, World!");
// var stopwatch = Stopwatch.StartNew();
//
// // = (9 + 2) * 2 = 11 * 2 = 22 
// // var text = @"$hello0 = ((3 * 3) + 2) * 2;
// // println $hello0;
// // $hello0 = ((3 * 3) + 2) * 2;
// // println $hello0;
// // ";
// // var text = @"println 2 * (3 + 2);";
// var opt = new MotorOptions()
// {
//     EnableDumb = true
// };
// // var text = @"$a = 0; $b = 1; $c = 0;
// // $c = $a + $b;
// // println $c;
// // $a = $b;
// // $b = $c;
// // goto_line 2;";
// // var text = @"loop {
// // println 'balls'; println 'balls'; println 'balls'; break;
// // }";
//
// // var text = @"
// // $a = 0; $b = 1; $c = 0;
// //
// // func DoMath{
// //     $c = $a + $b;
// // }
// //
// // for($i = 0, $i < 90, $i = $i + 1) {
// //     DoMath;
// //     println $i;
// //     println $c;
// //     $a = $b;
// //     $b = $c;
// // }";
// var text = @"
// throw #System.Exception:new('balls');
// ";
// // $g = @(1, 2).Length;
// // print $g;
// Console.WriteLine(text);
// Console.WriteLine("===============================");
// bruh: ;
// RCaronRunner.GlobalLog = RCaronRunnerLog.FunnyColors;
// var context = RCaronRunner.Parse(text, true);
// var m = new Motor(context, opt);
// m.Run();
// return;
// var range = new RCaronRange(0, 10);
// foreach (var num in range)
// {
//     Console.WriteLine(num);
// }