using BenchmarkDotNet.Attributes;
using Dynamitey;
using RCaron.Jit;

namespace RCaron.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(StringBenchmarks.Config))]
public class RCaronBenchmarks
{
    public const string SimpleMathOp = "$h = 2 * (2 + 3);";
    public const string FibonacciCode = @"
$a = 0; $b = 1; $c = 0;

for($i = 0; $i < 2; $i++) {
    $c = $a + $b;
    $a = $b;
    $b = $c;
}
";
    public Motor SimpleMathOpMotor { get; set; }
    public Motor FibonacciMotor { get; set; }
    public Delegate SimpleMathOpCompiled { get; set; }
    public Delegate FibonacciCompiled { get; set; }
    public Delegate SimpleMathOpInterpreted { get; set; }
    public Delegate FibonacciInterpreted { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        SimpleMathOpMotor = new Motor(RCaronRunner.Parse(SimpleMathOp));
        FibonacciMotor = new Motor(RCaronRunner.Parse(FibonacciCode));
        SimpleMathOpCompiled = Hook.CompileWithNoMotor(SimpleMathOp);
        FibonacciCompiled = Hook.CompileWithNoMotor(FibonacciCode);
        SimpleMathOpInterpreted = Hook.MakeInterpretedWithNoMotor(SimpleMathOp, int.MaxValue);
        FibonacciInterpreted = Hook.MakeInterpretedWithNoMotor(FibonacciCode, int.MaxValue);
    }
    [Benchmark(Baseline = true)]
    public void SimpleMathOpFull()
    {
        RCaronRunner.Run(SimpleMathOp);
    }

    [Benchmark]
    public void SimpleMathOpParsed()
    {
        SimpleMathOpMotor.Run();
    }
    [Benchmark]
    public void FibonacciFull()
    {
        RCaronRunner.Run(FibonacciCode);
    }

    [Benchmark]
    public void FibonacciParsed()
    {
        FibonacciMotor.Run();
    }
    
    [Benchmark]
    public void SimpleMathOpFull_Jit()
    {
        Hook.RunWithNoMotor(SimpleMathOp);
    }

    [Benchmark]
    public void SimpleMathOpParsed_Jit()
    {
        SimpleMathOpCompiled.DynamicInvoke();
    }
    [Benchmark]
    public void FibonacciFull_Jit()
    {
        Hook.RunWithNoMotor(FibonacciCode);
    }

    [Benchmark]
    public void FibonacciParsed_Jit()
    {
        FibonacciCompiled.DynamicInvoke();
    }
    
    [Benchmark]
    public void SimpleMathOpFull_JitInterpret()
    {
        Hook.MakeInterpretedWithNoMotor(SimpleMathOp).DynamicInvoke();
    }

    [Benchmark]
    public void SimpleMathOpParsed_JitInterpret()
    {
        SimpleMathOpInterpreted.DynamicInvoke();
    }
    [Benchmark]
    public void FibonacciFull_JitInterpret()
    {
        Hook.MakeInterpretedWithNoMotor(FibonacciCode).DynamicInvoke();
    }

    [Benchmark]
    public void FibonacciParsed_JitInterpret()
    {
        FibonacciInterpreted.DynamicInvoke();
    }
}