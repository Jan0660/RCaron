using RCaron.Classes;

namespace RCaron.Tests;

public class MultiFileTests
{
    [Fact]
    public void OpenFromString()
    {
        var m = RCaronRunner.Run(@"
$code = 'func Hello() { return 1; }
class Funny { func Hello() { return 2; } }';

Open-FromString($code);
$h = Hello();
$hell = #Funny:new().Hello();
");
        m.AssertVariableEquals("h", 1L);
        m.AssertVariableEquals("hell", 2L);
    }
    [Fact]
    public void OpenFromString_FunctionsSpecified()
    {
        var m = RCaronRunner.Run(@"
$code = 'func Hello() { return 1; }';

Open-FromString($code, functions: @('Hello'));
$h = Hello();
");
        m.AssertVariableEquals("h", 1L);
    }

    [Fact]
    public void OpenFromString_FunctionsSpecified_ImportNone()
    {
        ExtraAssert.ThrowsCode(() =>
        {
            RCaronRunner.Run(@"Open-FromString('func DoNot(){}', functions: @()); DoNot();");
        }, RCaronExceptionCode.MethodNotFound);
    }
    
    [Fact]
    public void OpenFromString_FunctionsSpecified_ImportNotFound()
    {
        ExtraAssert.ThrowsCode(() =>
        {
            RCaronRunner.Run(@"Open-FromString('', functions: @('Wont'));");
        }, RCaronExceptionCode.ImportNotFound);
    }
    [Fact]
    public void OpenFromString_ClassesSpecified()
    {
        var m = RCaronRunner.Run(@"
$code = 'class Funny { }';

Open-FromString($code, classes: @('Funny'));
$h = #Funny:new();
");
        m.AssertVariableIsType<ClassInstance>("h");
    }

    [Fact]
    public void OpenFromString_ClassesSpecified_ImportNone()
    {
        ExtraAssert.ThrowsCode(() =>
        {
            RCaronRunner.Run(@"Open-FromString('class DoNot{}', classes: @()); $h = #DoNot:new();");
        }, RCaronExceptionCode.TypeNotFound);
    }
    
    [Fact]
    public void OpenFromString_ClassesSpecified_ImportNotFound()
    {
        ExtraAssert.ThrowsCode(() =>
        {
            RCaronRunner.Run(@"Open-FromString('', functions: @('Wont'));");
        }, RCaronExceptionCode.ImportNotFound);
    }
}