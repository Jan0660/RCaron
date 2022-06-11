namespace RCaron.Tests;

public class ScopeTests
{
    [Fact]
    public void Bordered()
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
}