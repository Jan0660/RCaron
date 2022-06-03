using BenchmarkDotNet.Attributes;

namespace RCaron.Benchmarks;

[MemoryDiagnoser()]
[Config(typeof(StringBenchmarks.Config))]
public class TypeResolverBenchmarks
{
    [Benchmark()]
    public void FullName()
    {
        TypeResolver.FindType("System.Console");
    }
    [Benchmark()]
    public void FullNameWeirdInternal()
    {
        TypeResolver.FindType("Internal.Console");
    }

    public List<string> VarShortNameAndUsed = new() { "System" };
    [Benchmark()]
    public void ShortNameAndUsed()
    {
        TypeResolver.FindType("Console", VarShortNameAndUsed);
    }
}