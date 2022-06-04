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
        m.Variables["obj"] = new PropertyDummyObject();
        m.Run();
        m.VariableEquals("h", "VALUE");
    }

    public class FieldDummyObject
    {
        public string Value = "VALUE";
    }
    [Fact]
    public void VariableFieldAccess()
    {
        var m = new Motor(RCaronRunner.Parse("$h = $obj.value;"));
        m.Variables["obj"] = new FieldDummyObject();
        m.Run();
        m.VariableEquals("h", "VALUE");
    }

    [Fact]
    public void VariableArrayIndexAccess()
    {
        var m = new Motor(RCaronRunner.Parse("$i0 = $array.0; $i2 = $array.2;"));
        m.Variables["array"] = new[] { 0, 1, 2, 3, 4, 5 };
        m.Run();
        m.VariableEquals("i0", 0);
        m.VariableEquals("i2", 2);
    }

    [Fact]
    public void VariableDictionaryIndexAccess()
    {
        var m = new Motor(RCaronRunner.Parse("$zero = $dict.zero; $nested = $dict.nested.woo;"));
        m.Variables["dict"] = new Dictionary<string, object>
        {
            ["zero"] = "0",
            ["one"] = "1",
            ["two"] = "2",
            ["nested"] = new Dictionary<string, string>{["woo"] = "WOOO"}
        };
        m.Run();
        m.VariableEquals("zero", "0");
        m.VariableEquals("nested", "WOOO");
    }

    [Fact]
    public void MethodOnVariable()
    {
        var m = new Motor(RCaronRunner.Parse(@"$h = $array.0.ToString();"));
        m.Variables["array"] = new[] { 0, 1, 2, 3, 4, 5 };
        m.Run();
        m.VariableEquals("h", "0");
    }
}