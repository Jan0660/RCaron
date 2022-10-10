﻿using System.Collections;
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
    public void FunctionCall()
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
$h = Funny 2 -OptArg 3 4;"), RCaronExceptionCode.LeftOverPositionalArgument);
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
for ($h = 0, $h < 4, $h++) {
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
}