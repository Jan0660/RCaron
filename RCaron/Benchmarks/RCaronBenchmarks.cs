using BenchmarkDotNet.Attributes;

namespace RCaron.Benchmarks;

[MemoryDiagnoser]
public class RCaronBenchmarks
{
    public const string SimpleMathOp = "$h = 2 * (2 + 3);";
    public Motor SimpleMathOpMotor { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        SimpleMathOpMotor = new Motor(RCaronRunner.Parse(SimpleMathOp));
    }
    [Benchmark]
    public void SimpleMathOpFull()
    {
        RCaronRunner.Run(SimpleMathOp);
    }

    [Benchmark()]
    public void SimpleMathOpParsed()
    {
        SimpleMathOpMotor.Run();
    }
}