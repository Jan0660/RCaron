namespace RCaron.Tests;

public class ScopeTests
{
    [Fact]
    public void Set()
    {
        var m = TestRunner.Run(@"
func Woo{
    $h = 2;
}
$h = 1;
Woo;
");
        m.AssertVariableEquals("h", (long)1);
    }
    [Fact]
    public void Get()
    {
        ExtraAssert.ThrowsCode(() => TestRunner.Run(@"
func Woo{
    $g = $h;
}
$h = 1;
Woo;
"), RCaronExceptionCode.VariableNotFound);
    }
}