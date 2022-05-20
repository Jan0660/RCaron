namespace RCaron.Tests;

public class BasicFeatures
{
    [Fact]
    public void ParenthesisMath()
    {
        var m = RCaronRunner.Run("$h = ((3 * 3) + 2) * 2;");
        m.VariableEquals("h", (long)22);
    }
    [Fact]
    public void IfStatement()
    {
        var m = RCaronRunner.Run(@"$h = 2; if ($h == 2){ $h = 1; }");
        m.VariableEquals("h", (long)1);
    }
}