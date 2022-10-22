using BenchmarkDotNet.Attributes;

namespace RCaron.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(StringBenchmarks.Config))]
public class RCaronBenchmarks
{
    public const string SimpleMathOp = "$h = 2 * (2 + 3);";
    public const string FibbonaciCode = @"
$a = 0; $b = 1; $c = 0;

for($i = 0; $i < 2; $i++) {
    $c = $a + $b;
    $a = $b;
    $b = $c;
}
";
    public Motor SimpleMathOpMotor { get; set; }
    public Motor FibbonaciMotor { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        SimpleMathOpMotor = new Motor(RCaronRunner.Parse(SimpleMathOp));
        FibbonaciMotor = new Motor(RCaronRunner.Parse(FibbonaciCode));
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
    public void FibbonaciFull()
    {
        RCaronRunner.Run(FibbonaciCode);
    }

    [Benchmark]
    public void FibbonaciParsed()
    {
        FibbonaciMotor.Run();
    }
}