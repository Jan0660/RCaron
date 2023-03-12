using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace RCaron.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[Config(typeof(Config))]
public class StringBenchmarks
{
    public class Config : ManualConfig
    {
        public Config()
        {
            AddJob(Job.MediumRun.DontEnforcePowerPlan());
            // AddJob(Job.MediumRun.WithPowerPlan(new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61")));
            // AddJob(Job.MediumRun.WithPowerPlan(PowerPlan.UltimatePerformance));
            // AddJob(Job.MediumRun.WithPowerPlan(PowerPlan.UserPowerPlan));
            // AddJob(Job.MediumRun.WithPowerPlan(PowerPlan.HighPerformance));
            // AddJob(Job.MediumRun.WithPowerPlan(PowerPlan.Balanced));
            // AddJob(Job.MediumRun.WithPowerPlan(PowerPlan.PowerSaver));
        }
    }
    public Motor Motor;

    [GlobalSetup]
    public void Setup()
    {
        Motor = new Motor(RCaronRunner.Parse(@"$h = 'a fine string';"));
    }

    [Benchmark]
    public void Run()
        => Motor.Run();
}