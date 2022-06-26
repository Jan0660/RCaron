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
$i5 = $g.5;");
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
        Assert.Empty(m.BlockStack);

        m.RunWithCode($@"{definition}
Funny 2 -OptArg 3;");
        m.AssertVariableEquals("global", (long)5);
        Assert.Empty(m.BlockStack);

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
    public void ModuleMethodCall()
    {
        var m = RCaronRunner.Run(@$"
$h1 = ForUnitTests 2 -b 2;
$h2 = ForUnitTests 2;");
        m.AssertVariableEquals("h1", (long)4);
        m.AssertVariableEquals("h2", (long)3);
        Assert.Empty(m.BlockStack);

        m.RunWithCode(@"ForUnitTests 2 -b 3;");
        m.AssertVariableEquals("global", (long)5);
        Assert.Empty(m.BlockStack);

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
        var m = RCaronRunner.Run(@"$h = #System.Text.StringBuilder.New(int32(20));");
        var str = (StringBuilder)m.GlobalScope.GetVariable("h");
        Assert.Equal(20, str.Capacity);
    }
}