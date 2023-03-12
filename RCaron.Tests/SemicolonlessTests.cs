namespace RCaron.Tests;

public class SemicolonlessTests
{
    [Fact]
    public void Semicolonless()
    {
        var m = RCaronRunner.Run(@"$h = 0");
        m.AssertVariableEquals("h", 0L);
        m = RCaronRunner.Run(@"$h = 0
$h = $h + 1");
        m.AssertVariableEquals("h", 1L);
    }

    [Fact]
    public void Block()
    {
        var m = RCaronRunner.Run(@"$h = 1
if ($h == 1) {$h = 2}");
        m.AssertVariableEquals("h", 2L);
    }

    [Fact]
    public void Var()
    {
        var m = RCaronRunner.Run(@"$h = 1
$h = $h");
        m.AssertVariableEquals("h", 1L);

        var p = RCaronRunner.Parse("print $h");
        Assert.Equal("$h", ((TokenLine)p.FileScope.Lines[0]).Tokens[1].ToString(p.FileScope.Raw));
    }

    [Fact]
    public void DotGroup1()
    {
        var m = RCaronRunner.Run(@"$h = #System.Random:Shared.Next()
$h = 1");
        m.AssertVariableIsType<long>("h");
    }
    [Fact]
    public void DotGroup2()
    {
        var m = RCaronRunner.Run(@"$h = #System.Random:Shared.Next()");
        m.AssertVariableIsType<int>("h");
    }

    [Fact]
    public void DotGroupCall1()
    {
        RCaronRunner.Run(@"#System.Random:Shared.Next(1, 2)");
    }

    [Fact]
    public void DotGroupCall2()
    {
        RCaronRunner.Run(@"#System.Random:Shared.Next(1, 2)
$h = 1");
    }
}