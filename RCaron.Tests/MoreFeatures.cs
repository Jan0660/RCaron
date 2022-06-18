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
    public void FunctionArguments()
    {
        var definition = @"func Funny($Arg, $OptArg = 1){
    return $Arg + $OptArg;
}";
        var m = RCaronRunner.Run(@$"
{definition}

$h1 = Funny 2 -OptArg 2;
$h2 = Funny 2;
Funny 2 -OptArg 1;");
        m.AssertVariableEquals("h1", (long)4);
        m.AssertVariableEquals("h2", (long)3);
        
        ExtraAssert.ThrowsCode(() => RCaronRunner.Run($@"{definition}
$h = InvalidName 2;"), RCaronExceptionCode.FunctionNotFound);
        
        ExtraAssert.ThrowsCode(() => RCaronRunner.Run($@"{definition}
$h = Funny 2 -InvalidOptArg 2;"), RCaronExceptionCode.NamedFunctionArgumentNotFound);
        
        ExtraAssert.ThrowsCode(() => RCaronRunner.Run($@"{definition}
$h = Funny;"), RCaronExceptionCode.FunctionArgumentsLeftUnassigned);
        
        ExtraAssert.ThrowsCode(() => RCaronRunner.Run($@"{definition}
$h = Funny -OptArg 3;"), RCaronExceptionCode.FunctionArgumentsLeftUnassigned);
        
        ExtraAssert.ThrowsCode(() => RCaronRunner.Run($@"{definition}
$h = Funny 2 -OptArg 3 4;"), RCaronExceptionCode.LeftOverFunctionPositionalArgument);
    }
}