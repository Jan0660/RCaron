using RCaron.Parsing;

namespace RCaron.Tests;

public class SemicolonlessTests
{
    [Fact]
    public void Semicolonless()
    {
        var m = TestRunner.Run(@"$h = 0");
        m.AssertVariableEquals("h", 0L);
        m = TestRunner.Run(@"$h = 0
$h = $h + 1");
        m.AssertVariableEquals("h", 1L);
    }

    [Fact]
    public void Block()
    {
        var m = TestRunner.Run(@"$h = 1
if ($h == 1) {$h = 2}");
        m.AssertVariableEquals("h", 2L);
    }

    [Fact]
    public void Var()
    {
        var m = TestRunner.Run(@"$h = 1
$h = $h");
        m.AssertVariableEquals("h", 1L);

        var p = RCaronParser.Parse("print $h");
        Assert.Equal("$h", ((TokenLine)p.FileScope.Lines[0]).Tokens[1].ToString(p.FileScope.Raw));
    }

    [Fact]
    public void DotGroup1()
    {
        var m = TestRunner.Run(@"$h = #System.Random:Shared.Next()
$h = 1");
        m.AssertVariableIsType<long>("h");
    }
    [Fact]
    public void DotGroup2()
    {
        var m = TestRunner.Run(@"$h = #System.Random:Shared.Next()");
        m.AssertVariableIsType<int>("h");
    }

    [Theory]
    [InlineData(@"#System.Random:Shared.Next(1, 2)")]
    [InlineData(@"#System.Random:Shared.Next(1, 2)
$h = 1")]
    public void DotGroupCall(string code)
    {
        TestRunner.Run(code);
    }
}