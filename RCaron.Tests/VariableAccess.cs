namespace RCaron.Tests;

public class VariableAccess
{
    public class PropertyDummyObject
    {
        public string Value => "VALUE";
    }
    [Fact]
    public void VariablePropertyAccess()
    {
        var m = new Motor(RCaronRunner.Parse("$h = $obj.value;"));
        m.GlobalScope.SetVariable("obj", new PropertyDummyObject());
        m.Run();
        m.AssertVariableEquals("h", "VALUE");
        ExtraAssert.ThrowsCode(() => m.RunWithCode("$h = $obj.not;"), RCaronExceptionCode.CannotResolveInDotThing);
    }

    public class FieldDummyObject
    {
        public string Value = "VALUE";
    }
    [Fact]
    public void VariableFieldAccess()
    {
        var m = new Motor(RCaronRunner.Parse("$h = $obj.value;"));
        m.GlobalScope.SetVariable("obj", new FieldDummyObject());
        m.Run();
        m.AssertVariableEquals("h", "VALUE");
        ExtraAssert.ThrowsCode(() => m.RunWithCode("$h = $obj.not;"), RCaronExceptionCode.CannotResolveInDotThing);
    }

    [Fact]
    public void VariableArrayIndexAccess()
    {
        var m = new Motor(RCaronRunner.Parse("$i0 = $array.0; $i2 = $array.2;"));
        m.GlobalScope.SetVariable("array", new[] { 0, 1, 2, 3, 4, 5 });
        m.Run();
        m.AssertVariableEquals("i0", 0);
        m.AssertVariableEquals("i2", 2);
        // todo(error clarity): throw a RCaronException when index is out of bounds
        Assert.Throws<IndexOutOfRangeException>(() => m.RunWithCode("$i10 = $array.10;"));
    }

    [Fact]
    public void VariableDictionaryIndexAccess()
    {
        var m = new Motor(RCaronRunner.Parse("$zero = $dict.zero; $nested = $dict.nested.woo;"));
        m.GlobalScope.SetVariable("dict", new Dictionary<string, object>
        {
            ["zero"] = "0",
            ["one"] = "1",
            ["two"] = "2",
            ["nested"] = new Dictionary<string, string>{["woo"] = "WOOO"}
        });
        m.Run();
        m.AssertVariableEquals("zero", "0");
        m.AssertVariableEquals("nested", "WOOO");
    }

    [Fact]
    public void MethodOnVariable()
    {
        var m = new Motor(RCaronRunner.Parse(@"$h = $array.0.ToString();"));
        m.GlobalScope.SetVariable("array", new[] { 0, 1, 2, 3, 4, 5 });
        m.Run();
        m.AssertVariableEquals("h", "0");
        ExtraAssert.ThrowsCode(() => m.RunWithCode("$h = $array.0.NonExistentMethod();"), RCaronExceptionCode.MethodNoSuitableMatch);
    }

    [Fact]
    public void MethodOnDirect()
    {
        var m = RCaronRunner.Run(@"$h = 1.ToString();
$arrayLength = @(0, 1, 2, 3).Length;");
        m.AssertVariableEquals("h", "1");
        m.AssertVariableEquals("arrayLength", 4);
    }
}