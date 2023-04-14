using System.Collections;
using System.Text;
using RCaron.LibrarySourceGenerator;
using RCaron.Parsing;
using Xunit.Sdk;

namespace RCaron.Tests;

[Module("stuff")]
public partial class ForUnitTestsModule : IRCaronModule
{
    [Method("NoRequiredParameters")]
    public object NoRequiredParameters(Motor _, long optArg = 0)
    {
        return optArg;
    }
}

public class MoreFeatures
{
    [Fact]
    public void ExtensionMethods()
    {
        var m = TestRunner.Run(@"open_ext 'System.Linq';
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
        var m = TestRunner.Run(@$"
{definition}

$h1 = @Funny 2 -OptArg 2;
$h2 = @Funny 2;");
        m.AssertVariableEquals("h1", (long)4);
        m.AssertVariableEquals("h2", (long)3);
        Assert.Equal(0, m.BlockStack.Count);

        m.RunWithCode($@"{definition}
@Funny 2 -OptArg 3;");
        m.AssertVariableEquals("global", (long)5);
        Assert.Equal(0, m.BlockStack.Count);

        ExtraAssert.ThrowsCode(() => TestRunner.Run($@"{definition}
$h = @InvalidName 2;"), RCaronExceptionCode.MethodNotFound);

        ExtraAssert.ThrowsCode(() => TestRunner.Run($@"{definition}
$h = @Funny 2 -InvalidOptArg 2;"), RCaronExceptionCode.NamedArgumentNotFound);

        ExtraAssert.ThrowsCode(() => TestRunner.Run($@"{definition}
$h = @Funny;"), RCaronExceptionCode.ArgumentsLeftUnassigned);

        ExtraAssert.ThrowsCode(() => TestRunner.Run($@"{definition}
$h = @Funny -OptArg 3;"), RCaronExceptionCode.ArgumentsLeftUnassigned);

        ExtraAssert.ThrowsCode(() => TestRunner.Run($@"{definition}
$h = @Funny 2 3 4;"), RCaronExceptionCode.LeftOverPositionalArgument);

        ExtraAssert.ThrowsCode(() => TestRunner.Run($@"{definition}
$h = @Funny -OptArg 3 4;"), RCaronExceptionCode.PositionalArgumentAfterNamedArgument);
    }

    [Fact]
    public void FunctionCallLikeCall()
    {
        var definition = @"func Funny($Arg, $OptArg = 1){
    globalset('global', $Arg + $OptArg);
    return $Arg + $OptArg;
}";
        var m = TestRunner.Run(@$"
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

        ExtraAssert.ThrowsCode(() => TestRunner.Run($@"{definition}
$h = InvalidName(2);"), RCaronExceptionCode.MethodNotFound);

        ExtraAssert.ThrowsCode(() => TestRunner.Run($@"{definition}
$h = Funny(2, InvalidOptArg: 2);"), RCaronExceptionCode.NamedArgumentNotFound);

        ExtraAssert.ThrowsCode(() => TestRunner.Run($@"{definition}
$h = Funny();"), RCaronExceptionCode.ArgumentsLeftUnassigned);

        ExtraAssert.ThrowsCode(() => TestRunner.Run($@"{definition}
$h = Funny(OptArg: 3);"), RCaronExceptionCode.ArgumentsLeftUnassigned);

        ExtraAssert.ThrowsCode(() => TestRunner.Run($@"{definition}
$h = Funny(2, 4, 5);"), RCaronExceptionCode.LeftOverPositionalArgument);

        ExtraAssert.ThrowsCode(() => TestRunner.Run($@"{definition}
$h = Funny(OptArg: 3, 4);"), RCaronExceptionCode.PositionalArgumentAfterNamedArgument);
    }

    [Fact]
    public void FunctionNoParameters()
    {
        TestRunner.Run(@"func NoParameters(){}");
    }

    [Fact]
    public void ModuleMethodCall()
    {
        var m = TestRunner.Run(@$"
$h1 = @ForUnitTests 2 -b 2;
$h2 = @ForUnitTests 2;");
        m.AssertVariableEquals("h1", (long)4);
        m.AssertVariableEquals("h2", (long)3);
        Assert.Equal(0, m.BlockStack.Count);

        m.RunWithCode(@"@ForUnitTests 2 -b 3;");
        m.AssertVariableEquals("global", (long)5);
        Assert.Equal(0, m.BlockStack.Count);

        ExtraAssert.ThrowsCode(() => TestRunner.Run("$h = @ForUnitTests 2 -InvalidOptArg 2;"),
            RCaronExceptionCode.NamedArgumentNotFound);

        ExtraAssert.ThrowsCode(() => TestRunner.Run(@"
$h = @ForUnitTests;"), RCaronExceptionCode.ArgumentsLeftUnassigned);

        ExtraAssert.ThrowsCode(() => TestRunner.Run(@"
$h = @ForUnitTests -b 3;"), RCaronExceptionCode.ArgumentsLeftUnassigned);

        ExtraAssert.ThrowsCode(() => TestRunner.Run(@"
$h = @ForUnitTests 2 -b 3 4;"), RCaronExceptionCode.PositionalArgumentAfterNamedArgument);

        ExtraAssert.ThrowsCode(() => TestRunner.Run(@"
$h = @ForUnitTests 2 3 4;"), RCaronExceptionCode.LeftOverPositionalArgument);
    }

    [Theory]
    [InlineData(@"$h = @NoRequiredParameters;", 0L)]
    [InlineData(@"$h = @NoRequiredParameters 3;", 3L)]
    public void ModuleNoRequiredParameters(string code, long expected)
    {
        var m = TestRunner.Run(code, modules: new()
        {
            new ForUnitTestsModule(),
        });
        m.AssertVariableEquals("h", expected);
    }

    [Fact]
    public void RangeOperator()
    {
        var m = TestRunner.Run(@"$count = 0;
$last = 0;
foreach($num in range(0, 10)){
    $last = $num;
    $count++;
}");
        m.AssertVariableEquals("count", 10L);
        m.AssertVariableEquals("last", 9L);
    }

    [Fact]
    public void ConstructorNew()
    {
        var m = TestRunner.Run(@"$h = #System.Text.StringBuilder:New(int32(20));");
        var str = (StringBuilder)m.GlobalScope.GetVariable("h")!;
        Assert.Equal(20, str.Capacity);
    }

    [Fact]
    public void AllowShebang()
    {
        var m = TestRunner.Run(@"#! me when the
$h = 1;");
        m.AssertVariableEquals("h", 1L);
    }

    [Fact]
    public void SwitchStatement()
    {
        var m = TestRunner.Run(@"
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
        var m = TestRunner.Run(@"
$h = 0;
if ($true) { $h = 1; }
else if ($true) { $h = 2; }
");
        m.AssertVariableEquals("h", 1L);
    }

    [Fact]
    public void ElseStatement()
    {
        var m = TestRunner.Run(@"
$h = 0;
if ($false) { $h = 1; }
else { $h = 2; }
");
        m.AssertVariableEquals("h", 2L);
    }

    [Fact]
    public void ArrayIndexerDoesntGetConfused()
    {
        var m = TestRunner.Run(@"$arr = @(0); $h = 0; if(0 == $arr[0]){$h = 1;}");
        m.AssertVariableEquals("h", 1L);
    }

    [Fact]
    public void EqualityGroupWorks()
    {
        var ctx = RCaronParser.Parse(@"$h = 0 + 1 == 1;");
        var l = (TokenLine)ctx.FileScope.Lines[0];
        Assert.IsType<ComparisonValuePosToken>(l.Tokens[^1]);
        ctx = RCaronParser.Parse("if(0 + 1 == 1){}");
        l = (TokenLine)ctx.FileScope.Lines[0];
        Assert.IsType<ComparisonValuePosToken>(((CallLikePosToken)l.Tokens[0]).Arguments[0][0]);
    }

    [Theory]
    [InlineData("3 == 3 && 4 == 4")]
    [InlineData("3 == 3 && 4 == 4 && $true")]
    [InlineData("3 + 0 == 3 && 4 == 4")]
    [InlineData("1 + 2 == 2 + 1 && 3 + 1 == 2 + 1 + 1")]
    public void OperationOrderWithBooleanOps(string code)
    {
        var m = TestRunner.Run($"$h = {code};");
        m.AssertVariableEquals("h", true);
    }

    [Fact]
    public void BooleanAnd()
    {
        var m = TestRunner.Run(@"
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
        var m = TestRunner.Run(@"
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

    [Fact
#if RCARONJIT
            (Skip = "JIT does not plan to support this")
#endif
    ]
    public void GetLineNumber()
    {
        var m = TestRunner.Run(@"$l1 = 1;
$l2 = 2;
$l3 = 3;
dbg_exit;
$l5 = 5;", motorOptions: new MotorOptions { EnableDebugging = true });
        Assert.Equal(5, m.Lines.Count);
        Assert.Equal(4, m.GetLineNumber());
        m = TestRunner.Run(@"dbg_exit;", motorOptions: new MotorOptions { EnableDebugging = true });
        Assert.Equal(1, m.Lines.Count);
        Assert.Equal(1, m.GetLineNumber());
    }

    [Fact
#if RCARONJIT
            (Skip = "JIT does not plan to support this")
#endif
    ]
    public void GetLineNumber_Throws()
    {
        // when exception it thrown it returns the previous line number
        var prepared = TestRunner.Prepare(@"$l1 = 1;
$l2 = 2;
$l3 = 3;
dbg_throw;
$l5 = 5;", motorOptions: new MotorOptions() { EnableDebugging = true });
        var exc = ExtraAssert.Throws<Exception>(() => { TestRunner.RunPrepared(prepared); });
        Assert.Equal("dbg_throw", exc.Message);
        var m = prepared.motor;
        Assert.Equal(5, m.Lines.Count);
        Assert.Equal(4, m.GetLineNumber());
    }

    [Fact]
    public void ComparisonInParentheses()
    {
        var m = TestRunner.Run("$h = (1 == 1);");
        m.AssertVariableEquals("h", true);
    }

    [Fact]
    public void LogicalInParentheses()
    {
        var m = TestRunner.Run("$h = ($true && $true);");
        m.AssertVariableEquals("h", true);
    }

    [Fact]
    public void Throw()
    {
        var exc = ExtraAssert.Throws<Exception>(() => { TestRunner.Run("throw(#System.Exception:new('funny'));"); });
        Assert.Equal("funny", exc.Message);
        exc = ExtraAssert.Throws<Exception>(() => { TestRunner.Run("throw #System.Exception:new('funny');"); });
        Assert.Equal("funny", exc.Message);
    }

    [Fact]
    public void TryAndCatchBlock()
    {
        var m = TestRunner.Run(@"
$h = 0;
$exc = $null;
try {
    throw(#System.Exception:new('funny'));
    $h = $h + 1;
}
catch {
    $h = $h + 2;
    $exc = $exception;
}
$h = $h + 1;");
        m.AssertVariableEquals("h", 3L);
        m.AssertVariableIsType<Exception>("exc");
        Assert.Equal(0, m.BlockStack.Count);
    }

    [Fact]
    public void TryAndFinallyBlock()
    {
        var prepared = TestRunner.Prepare(@"
$h = 0;
try {
    throw(#System.Exception:new('funny'));
    $h = $h + 1;
}
finally {
    $h = $h + 2;
}
$h = $h + 1;");
        ExtraAssert.Throws<Exception>(() => TestRunner.RunPrepared(prepared));
        var m = prepared.motor;
        m.AssertVariableEquals("h", 2L);
        Assert.Equal(0, m.BlockStack.Count);
    }

    [Fact]
    public void TryAndCatchAndFinallyBlock()
    {
        var m = TestRunner.Run(@"
$h = 0;
try {
    throw(#System.Exception:new('funny'));
    $h = $h + 1;
}
catch {
    $h = $h + 4;
}
finally {
    $h = $h + 2;
}
$h = $h + 1;");
        m.AssertVariableEquals("h", 7L);
        Assert.Equal(0, m.BlockStack.Count);
    }

    [Fact]
    public void LetVariable()
    {
        var m = TestRunner.Run(@"
let $h = 1;
$h = $h + 1;");
        m.AssertVariableEquals("h", 2L);
        ExtraAssert.ThrowsCode(() =>
        {
            TestRunner.Run(@"
let $h = 1;
$h = 1.2;");
        }, RCaronExceptionCode.LetVariableTypeMismatch);
        ExtraAssert.ThrowsCode(() =>
        {
            TestRunner.Run(@"
let $h = 1;
$h = $null;");
        }, RCaronExceptionCode.LetVariableTypeMismatch);
    }

    [Fact]
    public void DotNetCallNumericConversion_OnConstructor()
    {
        // uses Timespan(int, int, int) constructor
        var m = TestRunner.Run("$h = #System.TimeSpan:new(1, 2, 3);");
        var timeSpan = m.AssertVariableIsType<TimeSpan>("h");
        Assert.Equal(1, timeSpan.Hours);
        Assert.Equal(2, timeSpan.Minutes);
        Assert.Equal(3, timeSpan.Seconds);
    }

    [Fact]
    public void DotNetCallNumericConversion()
    {
        var m = TestRunner.Run("$h = #System.TimeSpan:FromMilliseconds(10);");
        var timeSpan = m.AssertVariableIsType<TimeSpan>("h");
        Assert.Equal(10, timeSpan.TotalMilliseconds);
    }

    public static IEnumerable<object[]> NumbersDecimal = new[]
    {
        new object[]
        {
            "123m", 123M,
        },
    };

    [Theory]
    [InlineData("1", 1L)]
    [InlineData("123", 123L)]
    [InlineData("1.23", 1.23D)]
    [InlineData("0x12", 0x12UL)]
    [InlineData("0xDEADBEEF", 0xDEADBEEFUL)]
    [InlineData("0xDEADBEEFiu", 0xDEADBEEF)]
    [InlineData("0xDEADBEEFul", 0xDEADBEEFUL)]
    [InlineData("123i", 123)]
    [InlineData("123iu", 123u)]
    [InlineData("123l", 123L)]
    [InlineData("123u", 123UL)]
    [InlineData("123f", 123F)]
    [InlineData("123d", 123D)]
    [MemberData(nameof(NumbersDecimal))]
    // with underscore spacing
    [InlineData("1_2_3", 123L)]
    [InlineData("1_2_3.4_5_6", 123.456D)]
    [InlineData("1_2_3___.4_5_6", 123.456D)]
    [InlineData("0xDEAD_BEEF", 0xDEADBEEFUL)]
    [InlineData("0xDEAD_BEEFiu", 0xDEADBEEF)]
    [InlineData("0xDEAD_BEEF_________ul", 0xDEADBEEFUL)]
    [InlineData("0x________DEAD_BEEF_________", 0xDEADBEEFUL)]
    [InlineData("1_2_3_____", 123L)]
    public void Numbers(string input, object expected)
    {
        var m = TestRunner.Run($"$h = {input}");
        m.AssertVariableEquals("h", expected);
    }

    [Theory]
    [InlineData("0x11M")]
    [InlineData("1.1u")]
    [InlineData("1fu")]
    [InlineData("1.1fu")]
    public void InvalidNumberSuffix(string input)
    {
        ExtraAssert.ThrowsParsingCode(() => TestRunner.Run($"$h = {input};"), RCaronExceptionCode.InvalidNumberSuffix);
    }

    [Theory]
    [InlineData("0x")]
    [InlineData("0x____")]
    public void InvalidHexNumber(string code)
    {
        ExtraAssert.ThrowsParsingCode(() => TestRunner.Run($"$h = {code};"), RCaronExceptionCode.InvalidHexNumber);
    }
    
    [Theory]
    [InlineData("for")]
    [InlineData("qfor")]
    public void ForAndQForNoInitializer(string which)
    {
        var m = TestRunner.Run($$"""$i = 0; {{which}}(; $i < 3; $i = $i + 1) {  }""");
        m.AssertVariableEquals("i", 3L);
    }
    
    [Theory]
    [InlineData("for")]
    [InlineData("qfor")]
    public void ForAndQForNoIterator(string which)
    {
        var m = TestRunner.Run($$"""{{which}}($i = 0; $i < 3;) { $i = $i + 1; }""");
        m.AssertVariableEquals("i", 3L);
    }
    
    [Theory]
    [InlineData("byte", typeof(byte))]
    [InlineData("sbyte", typeof(sbyte))]
    [InlineData("char", typeof(char))]
    [InlineData("short", typeof(short))]
    [InlineData("ushort", typeof(ushort))]
    [InlineData("int", typeof(int))]
    [InlineData("uint", typeof(uint))]
    [InlineData("long", typeof(long))]
    [InlineData("ulong", typeof(ulong))]
    [InlineData("float", typeof(float))]
    [InlineData("double", typeof(double))]
    [InlineData("decimal", typeof(decimal))]
    [InlineData("bool", typeof(bool))]
    [InlineData("string", typeof(string))]
    [InlineData("object", typeof(object))]
    public void CSharpTypeNames(string input, Type expected)
    {
        var m = TestRunner.Run($"$h = #{input};");
        var type = m.AssertVariableIsType<RCaronType>("h");
        Assert.Equal(expected, type.Type);
        
        
        m = TestRunner.Run($"$h = #{input.ToUpperInvariant()};");
        type = m.AssertVariableIsType<RCaronType>("h");
        Assert.Equal(expected, type.Type);
    }
}