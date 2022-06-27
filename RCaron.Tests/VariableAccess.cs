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
            ["nested"] = new Dictionary<string, string> { ["woo"] = "WOOO" }
        });
        m.Run();
        m.AssertVariableEquals("zero", "0");
        m.AssertVariableEquals("nested", "WOOO");
    }

    [Fact]
    public void MethodOnVariable()
    {
        var m = new Motor(RCaronRunner.Parse(@"$h = $array.0.ToString();
$array.0.ToString();"));
        m.GlobalScope.SetVariable("array", new[] { 0, 1, 2, 3, 4, 5 });
        m.Run();
        m.AssertVariableEquals("h", "0");
        ExtraAssert.ThrowsCode(() => m.RunWithCode("$h = $array.0.NonExistentMethod();"),
            RCaronExceptionCode.MethodNoSuitableMatch);
    }

    [Fact]
    public void MethodOnDirect()
    {
        var m = RCaronRunner.Run(@"$h = 1.ToString();
$arrayLength = @(0, 1, 2, 3).Length;");
        m.AssertVariableEquals("h", "1");
        m.AssertVariableEquals("arrayLength", 4);
    }

    [Fact]
    public void NormalArrayAccess()
    {
        var m = RCaronRunner.Run(@"$arr = @(0, 1, 2, 3);
$h1 = $arr[1];
$h = 2;
$h2 = $arr[$h];");
        m.AssertVariableEquals("h1", (long)1);
        m.AssertVariableEquals("h2", (long)2);
    }

    public class NormalArrayAccessOnDotThingDummy
    {
        public object[] Array { get; set; }
    }

    [Fact]
    public void NormalArrayAccessOnDotThing()
    {
        var m = new Motor(RCaronRunner.Parse("$h1 = $obj.Array[1];"));
        m.GlobalScope.SetVariable("obj", new NormalArrayAccessOnDotThingDummy()
        {
            Array = new object[] { 0L, 1L, 2L, 3L, 4L, 5L }
        });
        m.Run();
        m.AssertVariableEquals("h1", (long)1);
    }

    [Fact]
    public void AssignerAssignments()
    {
        var m = RCaronRunner.Run(@"$arr = @(1, 2, 3, 4, 5);
$arr[1] = 22;
$h = $arr[1];");
        m.AssertVariableEquals("h", (long)22);
        m = RCaronRunner.Run(@"#RCaron.Tests.StaticDummy:Field = 1;
#RCaron.Tests.StaticDummy:Property = 1;");
        Assert.Equal(1, StaticDummy.Field);
        Assert.Equal(1, StaticDummy.Property);
    }

    [Fact]
    public void StaticGet()
    {
        StaticDummy.Field = 2;
        StaticDummy.Property = 2;
        var m = RCaronRunner.Run(@"open 'RCaron.Tests';
$field = #StaticDummy:Field;
$property = #StaticDummy:Property;
$nested = #StaticDummy:InstanceDummy.Property;");
        m.AssertVariableEquals("field", (long)2);
        m.AssertVariableEquals("property", (long)2);
        m.AssertVariableEquals("nested", 3L);
    }
}

public static class StaticDummy
{
    public static long Field;
    public static long Property { get; set; }
    public static readonly InstanceDummy InstanceDummy = new();
}

public class InstanceDummy
{
    public long Property => 3;
}