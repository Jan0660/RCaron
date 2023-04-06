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
        var m = TestRunner.Run("$h = $obj.value;", variables: new()
        {
            ["obj"] = new PropertyDummyObject()
        });
        m.AssertVariableEquals("h", "VALUE");
        ExtraAssert.ThrowsCode(() => TestRunner.Run("$h = $obj.not;", variables: new()
        {
            ["obj"] = new PropertyDummyObject()
        }), RCaronExceptionCode.CannotResolveInDotThing);
    }

    public class FieldDummyObject
    {
        public string Value = "VALUE";
    }

    [Fact]
    public void VariableFieldAccess()
    {
        var m = TestRunner.Run("$h = $obj.value;", variables: new()
        {
            ["obj"] = new FieldDummyObject()
        });
        m.AssertVariableEquals("h", "VALUE");
        ExtraAssert.ThrowsCode(() => TestRunner.Run("$h = $obj.nonExistentField;", variables: new()
        {
            ["obj"] = new FieldDummyObject()
        }), RCaronExceptionCode.CannotResolveInDotThing);
    }

    [Fact]
    public void VariableArrayIndexAccess()
    {
        var m = TestRunner.Run("$i0 = $array[0]; $i2 = $array[2];", variables: new()
        {
            ["array"] = new[] { 0, 1, 2, 3, 4, 5 }
        });
        m.AssertVariableEquals("i0", 0);
        m.AssertVariableEquals("i2", 2);
        ExtraAssert.Throws<IndexOutOfRangeException>(() =>
        {
            TestRunner.Run("$i10 = $array[10];", variables: new()
            {
                ["array"] = new[] { 0, 1, 2, 3, 4, 5 }
            });
        });
    }

    [Fact]
    public void VariableDictionaryIndexAccess()
    {
        var m = TestRunner.Run("$zero = $dict.zero; $nested = $dict.nested.woo;", variables: new()
        {
            ["dict"] = new Dictionary<string, object>
            {
                ["zero"] = "0",
                ["one"] = "1",
                ["two"] = "2",
                ["nested"] = new Dictionary<string, string> { ["woo"] = "WOOO" }
            }
        });
        m.AssertVariableEquals("zero", "0");
        m.AssertVariableEquals("nested", "WOOO");
    }

    [Fact]
    public void MethodOnVariable()
    {
        var m = TestRunner.Run(@"$h = $array[0].ToString();
$array[0].ToString();", variables: new()
        {
            ["array"] = new[] { 0, 1, 2, 3, 4, 5 }
        });
        m.AssertVariableEquals("h", "0");
        ExtraAssert.ThrowsCode(() => TestRunner.Run(@"$h = $array[0].NonExistentMethod();", variables: m.GlobalScope.Variables),
            RCaronExceptionCode.MethodNoSuitableMatch);
    }

    [Fact]
    public void MethodOnDirect()
    {
        var m = TestRunner.Run(@"$h = 1.ToString();
$arrayLength = @(0, 1, 2, 3).Length;");
        m.AssertVariableEquals("h", "1");
        m.AssertVariableEquals("arrayLength", 4);
    }

    [Fact]
    public void NormalArrayAccess()
    {
        var m = TestRunner.Run(@"$arr = @(0, 1, 2, 3);
$h1 = $arr[1];
$h = 2;
$h2 = $arr[$h];");
        m.AssertVariableEquals("h1", (long)1);
        m.AssertVariableEquals("h2", (long)2);
    }

    [Fact]
    public void NoIndexerImplementation()
    {
        ExtraAssert.ThrowsCode(() => TestRunner.Run(@"$arr = 1; $h = $arr[1];"),
            RCaronExceptionCode.NoSuitableIndexerImplementation);
    }

    private class CustomIndexerImplementationClass : IIndexerImplementation
    {
        public bool Do(Motor _, object? indexerValue, ref object? value, ref Type? type)
        {
            if (value is long a && indexerValue is long b)
            {
                value = a * b;
                type = typeof(long);
                return true;
            }

            return false;
        }
    }

    [Fact]
    public void CustomIndexerImplementation()
    {
        var m = TestRunner.Run(@"$v = 2; $h = $v[3];", indexers: new() { new CustomIndexerImplementationClass() });
        m.AssertVariableEquals("h", 6L);
    }

    private class CustomPropertyAccessorClass : IPropertyAccessor
    {
        public bool Do(Motor _, string propertyName, ref object? value, ref Type? type)
        {
            if (value is string a)
            {
                value = a + '.' + propertyName;
                type = typeof(string);
                return true;
            }

            return false;
        }
    }

    [Fact]
    public void CustomPropertyAccessor()
    {
        var m = TestRunner.Run(@"$v = 'funny'; $h = $v.hello;", propertyAccessors: new()
        {
            new CustomPropertyAccessorClass()
        });
        m.AssertVariableEquals("h", "funny.hello");
    }

    public class NormalIndexerOnDotThingDummy
    {
        public object[]? Array { get; set; }
    }

    [Fact]
    public void NormalArrayAccessOnDotThing()
    {
        var m = TestRunner.Run("$h1 = $obj.Array[1];", variables: new()
        {
            ["obj"] = new NormalIndexerOnDotThingDummy()
            {
                Array = new object[] { 0L, 1L, 2L, 3L, 4L, 5L }
            }
        });
        m.AssertVariableEquals("h1", (long)1);
    }

    [Fact]
    public void AssignerAssignments()
    {
        var m = TestRunner.Run(@"$arr = @(1, 2, 3, 4, 5);
$arr[1] = 22;
$h = $arr[1];");
        m.AssertVariableEquals("h", (long)22);
        TestRunner.Run(@"#RCaron.Tests.StaticDummy:Field = 1;
#RCaron.Tests.StaticDummy:Property = 1;");
        Assert.Equal(1, StaticDummy.Field);
        Assert.Equal(1, StaticDummy.Property);
    }

    [Fact]
    public void StaticGet()
    {
        StaticDummy.Field = 2;
        StaticDummy.Property = 2;
        var m = TestRunner.Run(@"open 'RCaron.Tests';
$field = #StaticDummy:Field;
$property = #StaticDummy:Property;
$nested = #StaticDummy:InstanceDummy.Property;");
        m.AssertVariableEquals("field", (long)2);
        m.AssertVariableEquals("property", (long)2);
        m.AssertVariableEquals("nested", 3L);
    }

    [Fact]
    public void DoesntGetMixedWithMathStuff()
    {
        var m = TestRunner.Run("$arr = @(1); $h = 1 + $arr[0]; $str = '2' + $h.ToString();");
        m.AssertVariableEquals("h", (long)2);
        m.AssertVariableEquals("str", "22");
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