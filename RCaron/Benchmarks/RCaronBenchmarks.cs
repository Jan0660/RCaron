using BenchmarkDotNet.Attributes;

namespace RCaron.Benchmarks;

[MemoryDiagnoser]
public class RCaronBenchmarks
{
    [Benchmark]
    public void SimpleMathOpFull()
    {
        RCaronRunner.Run("$h = 2 * (2 + 3);");
    }
}