namespace RCaron.Tests;

public class BasicFeatures
{
    [Fact]
    public void ParenthesisMath()
    {
        var m = RCaronRunner.Run("$h = ((3 * 3) + 2) * 2;");
        m.AssertVariableEquals("h", (long)22);
    }

    [Fact]
    public void IfStatement()
    {
        var m = RCaronRunner.Run(@"$h = 2;
$h2 = 0;
if ($h == 2){ $h = 1; }
if ($true){ $h2 = 1; }");
        m.AssertVariableEquals("h", (long)1);
        m.AssertVariableEquals("h2", (long)1);
    }

    [Fact]
    public void KeywordPlainCall()
    {
        var m = RCaronRunner.Run(@"dbg_assert_is_one 2 - 1;", new MotorOptions()
        {
            EnableDebugging = true,
        });
        m.AssertVariableEquals("$$assertResult", true);
        m = RCaronRunner.Run(@"dbg_sum_three 1 1 + 2 - 3 1;", new MotorOptions()
        {
            EnableDebugging = true,
        });
        m.AssertVariableEquals("$$assertResult", (long)2);
    }

    [Fact]
    public void GateDebug()
    {
        var p = RCaronRunner.Parse("dbg_println 'h';");
        new Motor(p, new MotorOptions { EnableDebugging = true }).Run();
        ExtraAssert.ThrowsCode(() => new Motor(p).Run(), RCaronExceptionCode.MethodNotFound);
    }

    [Fact]
    public void GateDumb()
    {
        var p = RCaronRunner.Parse("goto_line 0;");
        new Motor(p, new MotorOptions { EnableDumb = true }).Run();
        ExtraAssert.ThrowsCode(() => new Motor(p).Run(), RCaronExceptionCode.MethodNotFound);
    }

    [Fact]
    public void LoopLoopAndBreak()
    {
        var m = RCaronRunner.Run(@"$h = 0; loop {
$h = $h + 1;
if ($h > 9) { break; }
}");
        m.AssertVariableEquals("h", (long)10);
    }

    [Fact]
    public void WhileLoop()
    {
        var m = RCaronRunner.Run(@"$h = 10;
while ($h > 0) {
    $h = $h - 1;
}");
        m.AssertVariableEquals("h", (long)0);
    }

    [Fact]
    public void DoWhileLoop()
    {
        var m = RCaronRunner.Run(@"$h = 1;
dowhile ($h > 1) {
    $h = $h - 1;
}");
        m.AssertVariableEquals("h", (long)0);
    }

    [Fact]
    public void InvalidLine()
    {
        ExtraAssert.ThrowsCode(() => RCaronRunner.Parse(@"$h = 0;
$h println 'huh';"), ExceptionCode.ParseInvalidLine);
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
        m.AssertVariableEquals("h", "when the string is escaped == 'kool!!!'");
    }

    [Fact]
    public void Numbers()
    {
        var m = RCaronRunner.Run(@"$long = 123; $decimal = 123.123;");
        m.AssertVariableEquals("long", (long)123);
        m.AssertVariableEquals("decimal", 123.123M);
    }

    [Fact]
    public void Functions()
    {
        var m = RCaronRunner.Run(@"func sus{
    for($r = 0, $r < 2, $r++){
        print(1);
    }
    globalset('h', globalget('h') + 1);
}
$h = 0;
for($i = 0, $i < 3, $i++){
    sus;
}");
        m.AssertVariableEquals("h", (long)3);
    }

    [Fact]
    public void FunctionReturnValue()
    {
        var m = RCaronRunner.Run(@"func v{
    $g = 2;
    return $g;
}
$h = v();
");
        m.AssertVariableEquals("h", (long)2);
    }

    [Fact]
    public void ToStringKeywordCall()
    {
        var m = RCaronRunner.Run(@"$h = 0; $g = string($h);");
        m.AssertVariableEquals("g", "0");
    }

    [Fact]
    public void KeywordCall()
    {
        var m = RCaronRunner.Run(@"$h = sum(sum(1 + 2, 2 * 2 - 4), 1 + 3 + sum(1 + 1, 2 - 1 - 1));");
        m.AssertVariableEquals("h", (long)9);
        // todo: some better test like this
        m = RCaronRunner.Run("printfunny(1, 2, 3, 4, 5 + 1);");
    }

    [Fact]
    public void ForLoop()
    {
        var m = RCaronRunner.Run(@"$l = 0; for($h = 0, $h < 10, $h = $h + 1){$l = $h;}");
        m.AssertVariableEquals("l", (long)9);
        m.AssertVariableEquals("h", (long)10);
    }

    [Fact]
    public void QuickFor()
    {
        var m = RCaronRunner.Run(@"$l = 0; qfor($h = 0, $h < 10, $h = $h + 1){$l = $h;}");
        m.AssertVariableEquals("l", (long)9);
        m.AssertVariableEquals("h", (long)10);
    }

    [Fact]
    public void UnaryOperations()
    {
        var m = RCaronRunner.Run(@"$h = 3;
$h++;
$h++;
$h--;");
        m.AssertVariableEquals("h", (long)4);
    }

    [Fact]
    public void MultilineComments()
    {
        var m = RCaronRunner.Run(@"/*when the comment is*/
$h = 1;
/**/ /* cool */");
        m.AssertVariableEquals("h", (long)1);
    }

    [Fact]
    public void ExternalMethods()
    {
        var m = RCaronRunner.Run(@"$h1 = #System.MathF:Sqrt(float(9));
open 'System';
$h2 = #MathF:Sqrt(float(9));");
        m.AssertVariableEquals("h1", (float)3);
        m.AssertVariableEquals("h2", (float)3);
    }

    [Fact]
    public void SinglelineComments()
    {
        var m = RCaronRunner.Run(@"// w
$h = 1;
// WWWW");
        m.AssertVariableEquals("h", (long)1);
    }

    [Fact]
    public void Arrays()
    {
        var m = RCaronRunner.Run(@"$a = @(0, 1, 2, 3, 4, 5);
$i0 = $a.0;
$i5 = $a.5;");
        m.AssertVariableEquals("i0", (long)0);
        m.AssertVariableEquals("i5", (long)5);
    }

    [Fact]
    public void CodeBlockTokenEvaluate()
    {
        var m = RCaronRunner.Run(@"$h = 1 + {return 2;};");
        m.AssertVariableEquals("h", (long)3);
    }

    [Fact]
    public void ForeachLoop()
    {
        var m = new Motor(RCaronRunner.Parse(@"$arr = @(0, 1, 2, 3, 4, 5);
foreach($item in $arr)
{
    $list.Add($item);
}
"));
        m.GlobalScope.SetVariable("list", new System.Collections.ArrayList());
        m.Run();
        var list = (System.Collections.ArrayList)m.GlobalScope.GetVariable("list");
        Assert.Equal(6, list.Count);
        Assert.Equal(2L, list[2]);
    }
}