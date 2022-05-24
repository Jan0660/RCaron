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
        m = RCaronRunner.Run(@"dbg_sum_three 1 1 + 2 - 3 1;", new MotorOptions()
        {
            EnableDebugging = true,
        });
        m.VariableEquals("$$assertResult", (long)2);
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
    public void LoopLoopAndBreak()
    {
        var m = RCaronRunner.Run(@"$h = 0; loop {
$h = $h + 1;
if ($h > 9) { break; }
}");
        m.VariableEquals("h", (long)10);
    }

    [Fact]
    public void WhileLoop()
    {
        var m = RCaronRunner.Run(@"$h = 10;
while ($h > 0) {
    $h = $h - 1;
}");
        m.VariableEquals("h", (long)0);
    }

    [Fact]
    public void DoWhileLoop()
    {
        var m = RCaronRunner.Run(@"$h = 1;
dowhile ($h > 1) {
    $h = $h - 1;
}");
        m.VariableEquals("h", (long)0);
    }

    [Fact]
    public void InvalidLine()
    {
        var exp = Assert.Throws<RCaronException>(() => RCaronRunner.Parse(@"$h = 0;
$h println 'huh';"));
        Assert.Equal(RCaronExceptionTime.Parsetime, exp.Time);
    }

    [Fact]
    public void EnableLogRun()
    {
        RCaronRunner.GlobalLog = (RCaronRunnerLog)0b111111;
        RCaronRunner.Run("println 'woo';");
        RCaronRunner.GlobalLog = 0;
    }

    [Fact]
    public void Strings()
    {
        var m = RCaronRunner.Run("$h = 'when the string is escaped == \\'kool!!!\\'';");
        m.VariableEquals("h", "when the string is escaped == 'kool!!!'");
    }

    [Fact]
    public void Numbers()
    {
        var m = RCaronRunner.Run(@"$long = 123; $decimal = 123.123;");
        m.VariableEquals("long", (long)123);
        m.VariableEquals("decimal", 123.123M);
    }

    [Fact]
    public void Functions()
    {
        var m = RCaronRunner.Run(@"func sus{
    $h = 1;
}
sus;");
        m.VariableEquals("h", (long)1);
    }

    [Fact]
    public void ToStringKeywordCall()
    {
        var m = RCaronRunner.Run(@"$h = 0; $g = string($h);");
        m.VariableEquals("g", "0");
    }

    [Fact]
    public void KeywordCall()
    {
        var m = RCaronRunner.Run(@"$h = sum(sum(1 + 2, 2 * 2 - 4), 1 + 3 + sum(1 + 1, 2 - 1 - 1));");
        m.VariableEquals("h", (long)9);
        // todo: some better test like this
        m = RCaronRunner.Run("printfunny(1, 2, 3, 4, 5 + 1);");
    }

    [Fact]
    public void ForLoop()
    {
        var m = RCaronRunner.Run(@"for($h = 0, $h < 10, $h = $h + 1){$l = $h;}");
        m.VariableEquals("l", (long)9);
        m.VariableEquals("h", (long)10);
    }
}