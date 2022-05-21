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

    [Fact]
    public void KeywordPlainCall()
    {
        var m = RCaronRunner.Run(@"dbg_assert_is_one 2 - 1;", new MotorOptions()
        {
            EnableDebugging = true,
        });
        m.VariableEquals("$$assertResult", true);
    }

    [Fact]
    public void GateDebug()
    {
        var p = RCaronRunner.Parse("dbg_println 'h';");
        new Motor(p, new MotorOptions { EnableDebugging = true }).Run();
        Assert.Throws<RCaronException>(() => new Motor(p).Run());
    }

    [Fact]
    public void GateDumb()
    {
        var p = RCaronRunner.Parse("goto_line 0;");
        new Motor(p, new MotorOptions { EnableDumb = true }).Run();
        Assert.Throws<RCaronException>(() => new Motor(p).Run());
    }

    [Fact]
    public void LoopAndBreak()
    {
        var m = RCaronRunner.Run(@"$h = 0; loop {
$h = $h + 1;
if ($h > 9) { break; }
}");
        m.VariableEquals("h", (long)10);
    }

    [Fact]
    public void InvalidLine()
    {
        var exp = Assert.Throws<RCaronException>(() => RCaronRunner.Parse("$h println 'huh';"));
        Assert.Equal(RCaronExceptionTime.Parsetime, exp.Time);
    }

    [Fact]
    public void EnableLogRun()
    {
        RCaronRunner.GlobalLog = (RCaronRunnerLog)0b111111;
        RCaronRunner.Run("println 'woo';");
        RCaronRunner.GlobalLog = 0;
    }
}