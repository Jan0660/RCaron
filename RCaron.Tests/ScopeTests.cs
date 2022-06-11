namespace RCaron.Tests;

public class ScopeTests
{
    [Fact]
    public void Set()
    {
        var m = RCaronRunner.Run(@"
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
        Assert.Throws<RCaronException>(() => RCaronRunner.Run(@"
func Woo{
    $g = $h;
}
$h = 1;
Woo;
"));
    }
}