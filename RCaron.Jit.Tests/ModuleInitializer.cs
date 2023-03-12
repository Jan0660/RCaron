using System.Runtime.CompilerServices;

namespace RCaron.Jit.Tests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        // throw new();
        Hook.EmptyMethod();
    }
}