using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using RCaron.Benchmarks.Benchmarks;

{
    var benchmark = new RCaronBenchmarks();
    benchmark.GlobalSetup();
    for(var i = 0; i < 100_000_000; i++)
    {
        benchmark.FibbonaciParsed();
    }
}

switch (args[0])
{
    case "checkAllWork":
    {
        var types = typeof(Program).Assembly.GetTypes().Where(t => t.IsClass && t.Name.Contains("Benchmarks"));
        try
        {
            foreach (var type in types)
            {
                var methods = type.GetMethods().Where(m => m.GetCustomAttribute(typeof(BenchmarkAttribute)) != null);
                foreach(var method in methods)
                {
                    var instance = Activator.CreateInstance(type);
                    var setup = type.GetMethods().FirstOrDefault(m => m.GetCustomAttribute(typeof(GlobalSetupAttribute)) != null);
                    setup?.Invoke(instance, null);
                    method.Invoke(instance, Array.Empty<object>());
                }
            }
        }
        catch(Exception e)
        {
            Console.WriteLine(e);
            return 1;
        }
        break;
    }
    case "rcaron":
        BenchmarkRunner.Run<RCaronBenchmarks>();
        break;
    default:
        return 420;
}

return 0;
