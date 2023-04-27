using System.Collections;

namespace RCaron.Tests;

public class PipelineTests
{
    public class RCaronModulePipelineTests
    {
        [Fact
#if RCARONJIT
                (Skip = "JIT does not support pipelines")
#endif
        ]
        public void ObjectToPipeline()
        {
            var m = TestRunner.Run(@"1 | MeasurePipelineCount | SetVarFromPipeline 'h';");
            m.AssertVariableEquals("h", 1L);
        }

        [Fact
#if RCARONJIT
                (Skip = "JIT does not support pipelines")
#endif
        ]
        public void ArrayToPipeline()
        {
            var m = TestRunner.Run(@"@(1, 2, 3) | MeasurePipelineCount | SetVarFromPipeline 'h';");
            m.AssertVariableEquals("h", 3L);
        }

        [Fact
#if RCARONJIT
                (Skip = "JIT does not support pipelines")
#endif
        ]
        public void ObjectToObject()
        {
            var m = TestRunner.Run(@"3 | MeasurePipelineCount | SetVarFromPipelineObject 'h';");
            m.AssertVariableEquals("h", 1L);
        }

        [Fact
#if RCARONJIT
                (Skip = "JIT does not support pipelines")
#endif
        ]
        public void EnumeratorToPipeline()
        {
            var m = TestRunner.Run(@"EnumeratorRange 0i 5i | MeasurePipelineCount | SetVarFromPipelineObject 'h';");
            m.AssertVariableEquals("h", 5L);
        }

        [Fact
#if RCARONJIT
                (Skip = "JIT does not support pipelines")
#endif
        ]
        public void EnumerableToPipeline()
        {
            var m = TestRunner.Run(@"EnumerableRange 0i 5i | MeasurePipelineCount | SetVarFromPipelineObject 'h';");
            m.AssertVariableEquals("h", 5L);
        }

        [Fact
#if RCARONJIT
                (Skip = "JIT does not support pipelines")
#endif
        ]
        public void PipelineToEnumerator()
        {
            var m = TestRunner.Run(@"EnumeratorRange 0i 5i | MeasureEnumeratorCount | SetVarFromPipelineObject 'h';");
            m.AssertVariableEquals("h", 5L);
        }

        [Fact
#if RCARONJIT
                (Skip = "JIT does not support pipelines")
#endif
        ]
        public void EnumeratorToObjectToEnumerator()
        {
            var m = TestRunner.Run(@"EnumeratorRange 0i 5i | Increment | MeasureEnumeratorCount | SetVarFromPipelineObject 'h';");
            m.AssertVariableEquals("h", 5L);
        }

        [Fact
#if RCARONJIT
                (Skip = "JIT does not support pipelines")
#endif
        ]
        public void InvokeNormally()
        {
            var m = TestRunner.Run(@"$h = @Increment 4i;");
            m.AssertVariableEquals("h", 5);
        }
        [Fact
#if RCARONJIT
                (Skip = "JIT does not support pipelines")
#endif
        ]
        public void ObjectToEnumerator()
        {
            var m = TestRunner.Run(@"1i | MeasureEnumeratorCount | SetVarFromPipelineObject 'h';");
            m.AssertVariableEquals("h", 1L);
        }
        [Fact
#if RCARONJIT
                (Skip = "JIT does not support pipelines")
#endif
        ]
        public void ObjectToObjectToEnumerator()
        {
            var m = TestRunner.Run(@"1i | Increment | MeasureEnumeratorCount | SetVarFromPipelineObject 'h';");
            m.AssertVariableEquals("h", 1L);
        }
    }

    public class FunctionPipelineTests
    {
        [Fact
#if RCARONJIT
                (Skip = "JIT does not support pipelines")
#endif
        ]
        public void Hell()
        {
            var arrayList = new ArrayList();
            var m = TestRunner.Run("""
func Hell($pipeline = $fromPipeline, $arr = $null) {
    foreach($item in @Enumerate $pipeline) {
        $arr.Add($item);
    }
}

@(1, 2, 3) | Hell -arr $arr;
""", variables: new()
            {
                ["arr"] = arrayList,
            });
            Assert.Equal(3, arrayList.Count);
            Assert.Equal(1L, arrayList[0]);
            Assert.Equal(2L, arrayList[1]);
            Assert.Equal(3L, arrayList[2]);
        }
    }

    [Fact
#if RCARONJIT
                (Skip = "JIT does not support pipelines")
#endif
    ]
    public void CannotGetEnumeratorTwice()
    {
        var p = new SingleObjectPipeline(null);
        p.GetEnumerator();
        Assert.Throws<InvalidOperationException>(() => p.GetEnumerator());
    }
}