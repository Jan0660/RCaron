using System.Runtime.CompilerServices;

namespace RCaron.Tests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        // throw new();
        RCaron.Jit.Hook.EmptyMethod();
    }
}