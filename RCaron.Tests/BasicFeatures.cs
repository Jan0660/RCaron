using RCaron.Parsing;

namespace RCaron.Tests;

public class BasicFeatures
{
    [Fact]
    public void ParenthesisMath()
    {
        var m = TestRunner.Run("$h = ((3 * 3) + 2) * 2 / 2;");
        m.AssertVariableEquals("h", (long)11);
    }

    [Theory]
    [InlineData("3 * 3")]
    [InlineData("(3 * 3)")]
    [InlineData("((3 * 3))")]
    public void ParenthesisMathMore(string code)
    {
        var m = TestRunner.Run($"$h = {code};");
        m.AssertVariableEquals("h", (long)9);
    }

    [Fact]
    public void IfStatement()
    {
        var m = TestRunner.Run(@"$h = 2;
$h2 = 0;
if ($h == 2){ $h = $h - 1; }
if ($true){ $h2 = 1; }");
        m.AssertVariableEquals("h", (long)1);
        m.AssertVariableEquals("h2", (long)1);
    }

    [Fact]
    public void KeywordPlainCall()
    {
        var m = TestRunner.Run(@"dbg_assert_is_one (2 - 1);", motorOptions: new MotorOptions()
        {
            EnableDebugging = true,
        });
        m.AssertVariableEquals("$$assertResult", true);
        m = TestRunner.Run(@"dbg_sum_three 1 (1 + 2 - 3) 1;", motorOptions: new MotorOptions()
        {
            EnableDebugging = true,
        });
        m.AssertVariableEquals("$$assertResult", (long)2);
    }

    [Fact]
    public void GateDebug()
    {
        var code = "dbg_println 'h';";
        var p = TestRunner.Run(code, motorOptions: new MotorOptions { EnableDebugging = true });
        ExtraAssert.ThrowsCode(() => TestRunner.Run(code), RCaronExceptionCode.MethodNotFound);
    }

    [Fact]
    public void LoopLoopAndBreak()
    {
        var m = TestRunner.Run(@"$h = 0; loop {
$h = $h + 1;
if ($h > 9) { break; }
}
$reachedEnd = $true;");
        m.AssertVariableEquals("h", (long)10);
        m.AssertVariableEquals("reachedEnd", true);
    }

    [Fact]
    public void LoopLoopAndContinueAndBreak()
    {
        var m = TestRunner.Run(@"
$h = 0;
$b = 0;
loop {
    if ($h == 6) { break; }
    $h = $h + 1;
    if ($h == 6) { continue; }
    $b = $h;
}
$reachedEnd = $true;");
        m.AssertVariableEquals("h", (long)6);
        m.AssertVariableEquals("b", (long)5);
        m.AssertVariableEquals("reachedEnd", true);
    }

    [Fact]
    public void WhileLoop()
    {
        var m = TestRunner.Run(@"$h = 10;
while ($h > 0) {
    $h = $h - 1;
}");
        m.AssertVariableEquals("h", (long)0);
    }

    [Fact]
    public void DoWhileLoop()
    {
        var m = TestRunner.Run(@"$h = 1;
dowhile ($h > 1) {
    $h = $h - 1;
}");
        m.AssertVariableEquals("h", (long)0);
    }

    [Fact]
    public void InvalidLine()
    {
        ExtraAssert.ThrowsParsingCode(() => RCaronParser.Parse(@"$h = 0;
$h println 'huh';"), ExceptionCode.ParseInvalidLine);
    }

    [Fact]
    public void Strings()
    {
        var m = TestRunner.Run(@"$h = 'when the string is escaped == \'kool!!!\' \\';");
        m.AssertVariableEquals("h", @"when the string is escaped == 'kool!!!' \");
    }

    [Fact]
    public void StringInvalidCharacterEscape()
    {
        ExtraAssert.ThrowsParsingCode(() => TestRunner.Run(@"$h = '\z';"), ExceptionCode.InvalidEscape);
    }

    [Fact]
    public void StringUtf16Escape()
    {
        var m = TestRunner.Run(@"$h = 'a\u0159b';");
        m.AssertVariableEquals("h", "a\u0159b");
        ExtraAssert.ThrowsParsingCode(() => TestRunner.Run(@"$h = '\utttt';"), ExceptionCode.InvalidUnicodeEscape);
        ExtraAssert.ThrowsParsingCode(() => TestRunner.Run(@"$h = '\uaa';"), ExceptionCode.TooShortUnicodeEscape);
    }

    [Fact]
    public void StringUtf32Escape()
    {
        var m = TestRunner.Run(@"$h = 'a\U0001F47Db';");
        m.AssertVariableEquals("h", "a\U0001F47Db");
        ExtraAssert.ThrowsParsingCode(() => TestRunner.Run(@"$h = '\Utttttttt';"),
            ExceptionCode.InvalidUnicodeEscape);
        ExtraAssert.ThrowsParsingCode(() => TestRunner.Run(@"$h = '\Uaaaaaa';"), ExceptionCode.TooShortUnicodeEscape);
    }

    [Fact]
    public void Numbers()
    {
        var m = TestRunner.Run(@"$long = 123; $decimal = 123.123;");
        m.AssertVariableEquals("long", (long)123);
        m.AssertVariableEquals("decimal", 123.123D);
    }

    [Fact]
    public void Functions()
    {
        var m = TestRunner.Run(@"func sus(){
    for($r = 0; $r < 2; $r++){
        print(1);
    }
    globalset('h', globalget('h') + 1);
}
$h = 0;
for($i = 0; $i < 3; $i++){
    sus;
}");
        m.AssertVariableEquals("h", (long)3);
    }

    [Fact]
    public void FunctionReturnValue()
    {
        var m = TestRunner.Run(@"func v(){
    $g = 2;
    if ($true) {
        return $g;
    }
    return 'wrong way';
}
$h = v();
");
        m.AssertVariableEquals("h", 2L);
    }

    [Fact]
    public void FunctionReturnNoValue()
    {
        var m = TestRunner.Run(@"func v(){
    $g = 2;
    if ($true) {
        return;
    }
    return 'wrong way';
}
$h = v();
");
        m.AssertVariableEquals("h", RCaronInsideEnum.ReturnWithoutValue);
    }

    [Fact(Timeout = 20_000)]
    public void ReturnsCorrectly()
    {
        var bodies = new Dictionary<string, string>
        {
            ["if"] = @"if($true) { return 1; } return 0;",
            ["while"] = @"while($true) { return 1; } return 0;",
            ["just a code block"] = @"{ return 1; } return 0;",
            ["dowhile"] = @"dowhile($true) { return 1; } return 0;",
            ["foreach"] = @"foreach($z in range(0, 10)) { return 1; } return 0;",
            ["loop"] = @"loop { return 1; } return 0;",
            ["for"] = @"for($z = 0; $z < 10; $z = $z + 1) { return 1; } return 0;",
            ["qfor"] = @"qfor($z = 0; $z < 10; $z = $z + 1) { return 1; } return 0;",
            ["switch"] = @"switch (1) { 1 { return 1; } } return 0;",
            ["else if"] = @"if($false) { return 0; } else if ($true) { return 1; }",
            ["else"] = @"if($false) { return 0; } else { return 1; }",
        };
        foreach (var test in bodies)
        {
            try
            {
                var m = TestRunner.Run(@"func v(){
    " + test.Value + @"
}
$h = v();
");
                m.AssertVariableEquals("h", (long)1);
            }
            catch (Exception exc)
            {
                throw new($"Thrown at test '{test.Key}'", exc);
            }
        }
    }

    [Fact]
    public void ToStringKeywordCall()
    {
        var m = TestRunner.Run(@"$h = 0; $g = string($h);");
        m.AssertVariableEquals("g", "0");
    }

    [Fact]
    public void ForLoop()
    {
        var m = TestRunner.Run(@"$l = 0; for($h = 0; $h < 10; $h = $h + 1){$l = $h;}");
        m.AssertVariableEquals("l", (long)9);
        m.AssertVariableEquals("h", (long)10);
    }

    [Fact]
    public void QuickFor()
    {
        var m = TestRunner.Run(@"$l = 0; qfor($h = 0; $h < 10; $h = $h + 1){$l = $h;}");
        m.AssertVariableEquals("l", (long)9);
        m.AssertVariableEquals("h", (long)10);
    }

    [Fact]
    public void UnaryOperations()
    {
        var m = TestRunner.Run(@"$h = 3;
$h++;
$h++;
$h--;");
        m.AssertVariableEquals("h", (long)4);
    }

    [Fact]
    public void MultilineComments()
    {
        var m = TestRunner.Run(@"/#when the comment is#/
$h = 1;
/##/ /# cool #/");
        m.AssertVariableEquals("h", (long)1);
    }

    [Fact]
    public void ExternalMethods()
    {
        var m = TestRunner.Run(@"$h1 = #System.MathF:Sqrt(float(9));
open 'System';
$h2 = #MathF:Sqrt(float(9));");
        m.AssertVariableEquals("h1", (float)3);
        m.AssertVariableEquals("h2", (float)3);
    }

    [Fact]
    public void SinglelineComments()
    {
        var m = TestRunner.Run(@"// w
$h = 1;
// WWWW");
        m.AssertVariableEquals("h", (long)1);
    }

    [Fact]
    public void Arrays()
    {
        var m = TestRunner.Run(@"$a = @(0, 1, 2, 3, 4, 5);
$i0 = $a[0];
$i5 = $a[5];");
        m.AssertVariableEquals("i0", (long)0);
        m.AssertVariableEquals("i5", (long)5);
    }

    [Fact]
    public void ForeachLoop()
    {
        var m = TestRunner.Run(@"$arr = @(0, 1, 2, 3, 4, 5);
foreach($item in $arr)
{
    $list.Add($item);
}
", variables: new()
        {
            ["list"] = new System.Collections.ArrayList()
        });
        var list = (System.Collections.ArrayList)m.GlobalScope.GetVariable("list")!;
        Assert.Equal(6, list.Count);
        Assert.Equal(2L, list[2]);
    }

    [Fact]
    public void Null()
    {
        var m = TestRunner.Run(@"$on_eq = 1 == $null;
$on_neq = 1 != $null;
$no_eq = $null == 1;
$no_neq = $null != 1;
$nn_eq = $null == $null;
$nn_neq = $null != $null;");
        m.AssertVariableEquals("on_eq", false);
        m.AssertVariableEquals("on_neq", true);
        m.AssertVariableEquals("no_eq", false);
        m.AssertVariableEquals("no_neq", true);
        m.AssertVariableEquals("nn_eq", true);
        m.AssertVariableEquals("nn_neq", false);
    }
}