using RCaron.Classes;

namespace RCaron.Tests;

public class ClassTests
{
    [Fact]
    public void PropertyGet()
    {
        var m = RCaronRunner.Run(@"
class Funny {
    $prop = 1 + 2;
}

$h = #Funny:New().prop;
");
        m.AssertVariableEquals("h", 3L);
    }

    [Fact]
    public void PropertySet()
    {
        var m = RCaronRunner.Run(@"
class Funny {
    $prop = 1 + 2;
}
$f = #Funny:New();
$f.prop = 5;
");
        Assert.Equal(((ClassInstance)m.GlobalScope.Variables!["f"]!).PropertyValues![0], 5L);
    }
    [Fact]
    public void PropertyWithoutInitializer()
    {
        var m = RCaronRunner.Run(@"
class Funny {
    $prop;
}

$h = #Funny:New().prop;
");
        m.AssertVariableEquals("h", (object)null);
    }

    [Fact]
    public void Function()
    {
        var m = RCaronRunner.Run(@"
class Funny {
    func Function(){ return 3; }
}
$h = #Funny:New().Function();
");
        m.AssertVariableEquals("h", 3L);
    }

    [Fact]
    public void FunctionPropertyGet()
    {
        var m = RCaronRunner.Run(@"
class Funny {
    $prop = 1 + 2;
    func Function(){ return $prop; }
}
$h = #Funny:New().Function();
");
        m.AssertVariableEquals("h", 3L);
    }

    [Fact]
    public void FunctionPropertySet()
    {
        var m = RCaronRunner.Run(@"
class Funny {
    $prop = 1 + 2;
    func Function(){ $prop = 5; }
}
$f = #Funny:New();
$f.Function();
$h = $f.prop;
");
        m.AssertVariableEquals("h", 5L);
    }

    [Fact]
    public void FunctionWithParameter()
    {
        var m = RCaronRunner.Run(@"
class Funny {
    $prop = 2;
    func Function($a){ return $a + $prop; }
}
$h = #Funny:New().Function(4);
");
        m.AssertVariableEquals("h", 6L);
    }
}