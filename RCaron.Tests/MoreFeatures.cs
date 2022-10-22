using System.Collections;
using System.Text;

namespace RCaron.Tests;

public class MoreFeatures
{
    [Fact]
    public void ExtensionMethods()
    {
        var m = RCaronRunner.Run(@"open_ext 'System.Linq';
$array = @(0, 1, 2, 3, 4, 5);
$g = $array.ToList();
$i5 = $g[5];");
        m.AssertVariableEquals("i5", (long)5);
        m.AssertVariableIsType<List<object>>("g");
    }

    [Fact]
    public void FunctionPlainCall()
    {
        var definition = @"func Funny($Arg, $OptArg = 1){
    globalset('global', $Arg + $OptArg);
    return $Arg + $OptArg;
}";
        var m = RCaronRunner.Run(@$"
{definition}

$h1 = Funny 2 -OptArg 2;
$h2 = Funny 2;");
        m.AssertVariableEquals("h1", (long)4);
        m.AssertVariableEquals("h2", (long)3);
        Assert.Equal(0, m.BlockStack.Count);

        m.RunWithCode($@"{definition}
Funny 2 -OptArg 3;");
        m.AssertVariableEquals("global", (long)5);
        Assert.Equal(0, m.BlockStack.Count);

        ExtraAssert.ThrowsCode(() => RCaronRunner.Run($@"{definition}
$h = InvalidName 2;"), RCaronExceptionCode.MethodNotFound);

        ExtraAssert.ThrowsCode(() => RCaronRunner.Run($@"{definition}
$h = Funny 2 -InvalidOptArg 2;"), RCaronExceptionCode.NamedArgumentNotFound);

        ExtraAssert.ThrowsCode(() => RCaronRunner.Run($@"{definition}
$h = Funny;"), RCaronExceptionCode.ArgumentsLeftUnassigned);

        ExtraAssert.ThrowsCode(() => RCaronRunner.Run($@"{definition}
$h = Funny -OptArg 3;"), RCaronExceptionCode.ArgumentsLeftUnassigned);

        ExtraAssert.ThrowsCode(() => RCaronRunner.Run($@"{definition}
$h = Funny 2 3 4;"), RCaronExceptionCode.LeftOverPositionalArgument);

        ExtraAssert.ThrowsCode(() => RCaronRunner.Run($@"{definition}
$h = Funny -OptArg 3 4;"), RCaronExceptionCode.PositionalArgumentAfterNamedArgument);
    }

    [Fact]
    public void FunctionCallLikeCall()
    {
        var definition = @"func Funny($Arg, $OptArg = 1){
    globalset('global', $Arg + $OptArg);
    return $Arg + $OptArg;
}";
        var m = RCaronRunner.Run(@$"
{definition}

$h1 = Funny(2, OptArg: 2);
$h2 = Funny(2);");
        m.AssertVariableEquals("h1", (long)4);
        m.AssertVariableEquals("h2", (long)3);
        Assert.Equal(0, m.BlockStack.Count);

        m.RunWithCode($@"{definition}
Funny(2, OptArg: 3);");
        m.AssertVariableEquals("global", (long)5);
        Assert.Equal(0, m.BlockStack.Count);

        ExtraAssert.ThrowsCode(() => RCaronRunner.Run($@"{definition}
$h = InvalidName(2);"), RCaronExceptionCode.MethodNotFound);

        ExtraAssert.ThrowsCode(() => RCaronRunner.Run($@"{definition}
$h = Funny(2, InvalidOptArg: 2);"), RCaronExceptionCode.NamedArgumentNotFound);

        ExtraAssert.ThrowsCode(() => RCaronRunner.Run($@"{definition}
$h = Funny();"), RCaronExceptionCode.ArgumentsLeftUnassigned);

        ExtraAssert.ThrowsCode(() => RCaronRunner.Run($@"{definition}
$h = Funny(OptArg: 3);"), RCaronExceptionCode.ArgumentsLeftUnassigned);

        ExtraAssert.ThrowsCode(() => RCaronRunner.Run($@"{definition}
$h = Funny(2, 4, 5);"), RCaronExceptionCode.LeftOverPositionalArgument);

        ExtraAssert.ThrowsCode(() => RCaronRunner.Run($@"{definition}
$h = Funny(OptArg: 3, 4);"), RCaronExceptionCode.PositionalArgumentAfterNamedArgument);
    }

    [Fact]
    public void FunctionNoParameters()
    {
        RCaronRunner.Run(@"func NoParameters(){}");
    }

    [Fact]
    public void ModuleMethodCall()
    {
        var m = RCaronRunner.Run(@$"
$h1 = ForUnitTests 2 -b 2;
$h2 = ForUnitTests 2;");
        m.AssertVariableEquals("h1", (long)4);
        m.AssertVariableEquals("h2", (long)3);
        Assert.Equal(0, m.BlockStack.Count);

        m.RunWithCode(@"ForUnitTests 2 -b 3;");
        m.AssertVariableEquals("global", (long)5);
        Assert.Equal(0, m.BlockStack.Count);

        ExtraAssert.ThrowsCode(() => RCaronRunner.Run("$h = ForUnitTests 2 -InvalidOptArg 2;"),
            RCaronExceptionCode.NamedArgumentNotFound);

        ExtraAssert.ThrowsCode(() => RCaronRunner.Run(@"
$h = ForUnitTests;"), RCaronExceptionCode.ArgumentsLeftUnassigned);

        ExtraAssert.ThrowsCode(() => RCaronRunner.Run(@"
$h = ForUnitTests -b 3;"), RCaronExceptionCode.ArgumentsLeftUnassigned);

        ExtraAssert.ThrowsCode(() => RCaronRunner.Run(@"
$h = ForUnitTests 2 -b 3 4;"), RCaronExceptionCode.LeftOverPositionalArgument);
    }

    [Fact]
    public void RangeOperator()
    {
        var m = RCaronRunner.Run(@"$count = 0;
$last = 0;
foreach($num in 0..10){
    $last = $num;
    $count++;
}");
        m.AssertVariableEquals("count", 10L);
        m.AssertVariableEquals("last", 9L);
    }

    [Fact]
    public void ConstructorNew()
    {
        var m = RCaronRunner.Run(@"$h = #System.Text.StringBuilder:New(int32(20));");
        var str = (StringBuilder)m.GlobalScope.GetVariable("h");
        Assert.Equal(20, str.Capacity);
    }

    [Fact]
    public void AllowShebang()
    {
        var m = RCaronRunner.Run(@"#! me when the
$h = 1;");
        m.AssertVariableEquals("h", 1L);
    }

    [Fact]
    public void SwitchStatement()
    {
        var m = RCaronRunner.Run(@"
$ls = #System.Collections.ArrayList:New();
for ($h = 0; $h < 4; $h++) {
    switch($h) {
        0 { $ls.Add('zero');  }
        1 { $ls.Add('one'); }
        default { $ls.Add('default ' + $h); }
    }
}");
        var ls = (ArrayList)m.GlobalScope.Variables!["ls"]!;
        Assert.Equal("zero", ls[0]);
        Assert.Equal("one", ls[1]);
        Assert.Equal("default 2", ls[2]);
        Assert.Equal("default 3", ls[3]);
        Assert.Equal(4, ls.Count);
    }

    [Fact]
    public void ElseIfStatement()
    {
        var m = RCaronRunner.Run(@"
$h = 0;
if ($true) { $h = 1; }
else if ($true) { $h = 2; }
");
        m.AssertVariableEquals("h", 1L);
    }

    [Fact]
    public void ElseStatement()
    {
        var m = RCaronRunner.Run(@"
$h = 0;
if ($false) { $h = 1; }
else { $h = 2; }
");
        m.AssertVariableEquals("h", 2L);
    }

    [Fact]
    public void ArrayIndexerDoesntGetConfused()
    {
        var m = RCaronRunner.Run(@"$arr = @(0); $h = 0; if(0 == $arr[0]){$h = 1;}");
        m.AssertVariableEquals("h", 1L);
    }

    [Fact]
    public void EqualityGroupWorks()
    {
        var ctx = RCaronRunner.Parse(@"$h = 0 + 1 == 1;");
        var l = (TokenLine)ctx.Lines[0];
        Assert.IsType<ComparisonValuePosToken>(l.Tokens[^1]);
        ctx = RCaronRunner.Parse("if(0 + 1 == 1){}");
        l = (TokenLine)ctx.Lines[0];
        Assert.IsType<ComparisonValuePosToken>(((CallLikePosToken)l.Tokens[^1]).Arguments[0][0]);
    }

    [Fact]
    public void OperationOrderWithBooleanOps()
    {
        var m = RCaronRunner.Run("$h = 3 == 3 && 4 == 4;");
        m.AssertVariableEquals("h", true);
        m = RCaronRunner.Run("$h = 1 + 2 == 2 + 1 && 3 + 1 == 2 + 1 + 1;");
        m.AssertVariableEquals("h", true);
    }

    [Fact]
    public void BooleanAnd()
    {
        var m = RCaronRunner.Run(@"
$tt = $true && $true;
$tf = $true && $false;
$ft = $false && $true;
$ff = $false && $false;
");
        m.AssertVariableEquals("tt", true);
        m.AssertVariableEquals("tf", false);
        m.AssertVariableEquals("ft", false);
        m.AssertVariableEquals("ff", false);
    }

    [Fact]
    public void BooleanOr()
    {
        var m = RCaronRunner.Run(@"
$tt = $true || $true;
$tf = $true || $false;
$ft = $false || $true;
$ff = $false || $false;
");
        m.AssertVariableEquals("tt", true);
        m.AssertVariableEquals("tf", true);
        m.AssertVariableEquals("ft", true);
        m.AssertVariableEquals("ff", false);
    }

    [Fact]
    public void GetLineNumber()
    {
        var m = RCaronRunner.Run(@"$l1 = 1;
$l2 = 2;
$l3 = 3;
dbg_exit;
$l5 = 5;", new MotorOptions() { EnableDebugging = true });
        Assert.Equal(5, m.Lines.Count);
        Assert.Equal(4, m.GetLineNumber());
        m = RCaronRunner.Run(@"dbg_exit;", new MotorOptions() { EnableDebugging = true });
        Assert.Equal(1, m.Lines.Count);
        Assert.Equal(1, m.GetLineNumber());
        // when exception it thrown it returns the previous line number
        m = new Motor(RCaronRunner.Parse(@"$l1 = 1;
$l2 = 2;
$l3 = 3;
dbg_throw;
$l5 = 5;"), new MotorOptions() { EnableDebugging = true });
        var exc = Assert.Throws<Exception>(() =>
        {
            m.Run();
        });
        Assert.Equal("dbg_throw", exc.Message);
        Assert.Equal(5, m.Lines.Count);
        Assert.Equal(4, m.GetLineNumber());
    }

    [Fact]
    public void ComparisonInParentheses()
    {
        var m = RCaronRunner.Run("$h = (1 == 1);");
        m.AssertVariableEquals("h", true);
    }

    [Fact]
    public void LogicalInParentheses()
    {
        var m = RCaronRunner.Run("$h = ($true && $true);");
        m.AssertVariableEquals("h", true);
    }
}