using RCaron.Classes;

namespace RCaron.Tests;

public class ClassTests
{
    [Fact]
    public void PropertyGet()
    {
        var m = TestRunner.Run(@"
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
        var m = TestRunner.Run(@"
class Funny {
    $prop = 1 + 2;
}
$f = #Funny:New();
$f.prop = 5;
");
        var f = m.AssertVariableIsType<ClassInstance>("f");
        Assert.Equal(f.PropertyValues![0], 5L);
    }
    [Fact]
    public void PropertyWithoutInitializer()
    {
        var m = TestRunner.Run(@"
class Funny {
    $prop;
}

$h = #Funny:New().prop;
");
        m.AssertVariableEquals("h", (object?)null);
    }

    [Fact]
    public void Function()
    {
        var m = TestRunner.Run(@"
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
        var m = TestRunner.Run(@"
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
        var m = TestRunner.Run(@"
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
    public void Function_WithArgs()
    {
        var m = TestRunner.Run(@"
class Funny {
    func Function($must, $opt = 2){ return $must + $opt; }
}
$f = #Funny:New();
$h = $f.Function(1);
$h2 = $f.Function(1, opt: 1);
$h6 = $f.Function(opt: 1, must: 5);
");
        m.AssertVariableEquals("h", 3L);
        m.AssertVariableEquals("h2", 2L);
        m.AssertVariableEquals("h6", 6L);
    }

    [Fact]
    public void FunctionInFunction()
    {
        var m = TestRunner.Run(@"class Funny {
func Function() { return Second(); }

func Second() { return 3; }
}

$h = #Funny:New().Function();");
        m.AssertVariableEquals("h", 3L);
    }

    [Fact]
    public void ClassFunctionNotFound()
    {
        ExtraAssert.ThrowsCode(() =>
        {
            TestRunner.Run(@"
class Funny {
}
$h = #Funny:New().Function();");
        }, RCaronExceptionCode.ClassFunctionNotFound);
    }

    [Fact]
    public void ClassPropertyNotFound()
    {
        ExtraAssert.ThrowsCode(() =>
        {
            TestRunner.Run(@"
class Funny {
}
$h = #Funny:New().noprop;");
        }, RCaronExceptionCode.ClassPropertyNotFound);
        ExtraAssert.ThrowsCode(() =>
        {
            TestRunner.Run(@"
class Funny {
}
#Funny:New().noprop = 0;");
        }, RCaronExceptionCode.ClassPropertyNotFound);
    }

    [Fact]
    public void ClassTypeOf()
    {
        var m = TestRunner.Run(@"
class Funny { }
$h = #Funny:new().GetType();
");
        m.AssertVariableEquals("h", typeof(ClassInstance));
    }

    public class StaticTests
    {
        [Fact]
        public void StaticPropertyGet()
        {
            var m = TestRunner.Run(@"
class Funny {
    static $prop = 1;
}
$h = #Funny:prop;");
            m.AssertVariableEquals("h", 1L);
        }

        [Fact]
        public void StaticPropertySet()
        {
            var m = TestRunner.Run(@"
class Funny {
    static $prop = 1;
}
#Funny:prop = 5;
$h = #Funny:prop;");
            m.AssertVariableEquals("h", 5L);
        }

        [Fact]
        public void StaticFunction()
        {
            var m = TestRunner.Run(@"
class Funny {
    static func Function() { return 3; }
}
$h = #Funny:Function();");
            m.AssertVariableEquals("h", 3L);
        }

        [Fact]
        public void StaticFunctionPropertyGet()
        {
            var m = TestRunner.Run(@"
class Funny {
    static $staticProp = 1;
    static func Function() { return $staticProp; }
}
$h = #Funny:Function();");
            m.AssertVariableEquals("h", 1L);
        }
        
        [Fact]
        public void StaticFunctionPropertySet()
        {
            var m = TestRunner.Run(@"
class Funny {
    static $staticProp = 1;
    static func Function() { $staticProp = 5; }
}
#Funny:Function();
$h = #Funny:staticProp;");
            m.AssertVariableEquals("h", 5L);
        }
        
        [Fact]
        public void FunctionStaticPropertyGet()
        {
            var m = TestRunner.Run(@"
class Funny {
    static $staticProp = 1;
    func Function() { return $staticProp; }
}
$h = #Funny:New().Function();");
            m.AssertVariableEquals("h", 1L);
        }
        
        [Fact]
        public void FunctionStaticPropertySet()
        {
            var m = TestRunner.Run(@"
class Funny {
    static $staticProp = 1;
    func Function() { $staticProp = 5; }
}
$f = #Funny:New();
$f.Function();
$h = #Funny:staticProp;");
            m.AssertVariableEquals("h", 5L);
        }
    }
}