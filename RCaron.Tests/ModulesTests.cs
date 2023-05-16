using RCaron.BaseLibrary;

namespace RCaron.Tests;

public class ModulesTests
{
    [Fact]
    public void GetModuleType()
    {
        var m = TestRunner.Run("$m = @Get-ModuleType 'Reflection';");
        var t = m.GetVar("m") as Type;
        Assert.NotNull(t);
        Assert.True(t.IsAssignableFrom(typeof(ReflectionModule)));
    }
}