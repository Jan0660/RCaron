﻿namespace RCaron.Tests;

public class MoreFeatures
{
    [Fact]
    public void ExtensionMethods()
    {
        var m = RCaronRunner.Run(@"open_ext 'System.Linq';
$array = @(0, 1, 2, 3, 4, 5);
$g = $array.ToList();
$i5 = $g.5;");
        m.VariableEquals("i5", (long)5);
        Assert.IsType<List<object>>(m.Variables["g"]);
    }
}