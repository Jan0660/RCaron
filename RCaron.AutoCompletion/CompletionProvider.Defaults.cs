namespace RCaron.AutoCompletion;

public partial class CompletionProvider
{
    public static readonly CompletionThing[] BuiltInFunctions =
    {
        new()
        {
            Word = "print",
            Kind = CompletionItemKind.Method,
            Documentation = "Prints all of the arguments to the console, with a space separating all of them.",
            Detail = "(method) print(params $args)"
        },
        new()
        {
            Word = "println",
            Kind = CompletionItemKind.Method,
            Documentation = "Prints all of the arguments to the console, with a newline separating all of them.",
        },
        new()
        {
            Word = "string",
            Kind = CompletionItemKind.Method,
            Detail = "(method) string($value)",
            Documentation = "Converts the given value to a string using `ToString()`."
        },
        new()
        {
            Word = "float",
            Kind = CompletionItemKind.Method,
            Detail = "(method) float($value)",
            Documentation = "Converts the given value to a float(`System.Single`)."
        },
        new()
        {
            Word = "int32",
            Kind = CompletionItemKind.Method,
            Detail = "(method) int32($value)",
            Documentation = "Converts the given value to an int(`System.Int32`)."
        },
        new()
        {
            Word = "int64",
            Kind = CompletionItemKind.Method,
            Detail = "(method) int64($value)",
            Documentation = "Converts the given value to a long(`System.Int64`)."
        },
        new()
        {
            Word = "throw",
            Kind = CompletionItemKind.Method,
            Detail = "(method) throw($exception)",
            Documentation = "Throws the given exception."
        },
        new()
        {
            Word = "range",
            Kind = CompletionItemKind.Method,
            Detail = "(method) range($start, $end)",
            Documentation =
                "Creates a range from `$start` to `$end`. Note both `$start` and `$end` must be of type long(`System.Int64`)."
        },
    };

    public static readonly CompletionThing[] Keywords =
    {
        new()
        {
            Word = "if",
            Kind = CompletionItemKind.Keyword,
            Detail = "if($condition) {...}",
        },
        new()
        {
            Word = "else",
            Kind = CompletionItemKind.Keyword,
            Detail = @"// must have an if or else if statement before
else {...}",
        },
        new()
        {
            Word = "func",
            Kind = CompletionItemKind.Keyword,
            Detail = "func Name($param1, $param2) {...}",
        },
        new()
        {
            Word = "return",
            Kind = CompletionItemKind.Keyword,
            Detail = @"// can be either
return;
return $value;",
        },
        new()
        {
            Word = "for",
            Kind = CompletionItemKind.Keyword,
            Detail = "for ($i = 0; $i < 10; $i++) {...}",
        },
        new()
        {
            Word = "qfor",
            Kind = CompletionItemKind.Keyword,
            Detail = "qfor ($i = 0; $i < 10; $i++) {...}",
        },
        new()
        {
            Word = "foreach",
            Kind = CompletionItemKind.Keyword,
            Detail = "foreach ($i in $enumerable) {...}",
        },
        new()
        {
            Word = "while",
            Kind = CompletionItemKind.Keyword,
            Detail = "while ($condition) {...}",
        },
        new()
        {
            Word = "dowhile",
            Kind = CompletionItemKind.Keyword,
            Detail = "dowhile ($condition) {...}",
        },
        new()
        {
            Word = "loop",
            Kind = CompletionItemKind.Keyword,
            Detail = "loop {...}",
        },
        new()
        {
            Word = "break",
            Kind = CompletionItemKind.Keyword,
            Detail = "break;",
            Documentation = "Terminates a loop."
        },
        new()
        {
            Word = "continue",
            Kind = CompletionItemKind.Keyword,
            Detail = "continue;",
            Documentation = "Skips the current iteration of a loop."
        },
        new()
        {
            Word = "switch",
            Kind = CompletionItemKind.Keyword,
            Detail = "switch ($value) {...}",
            Documentation = @"Compares `$value` to each case and executes the first matching case.
Example:
```rcaron
switch ('fun') {
    'fun' { print 'fun'; }
    default { print 'default'; }
}
// Output: fun
```"
        },
        new()
        {
            Word = "class",
            Kind = CompletionItemKind.Keyword,
            Detail = "class Name {...}",
        },
        new()
        {
            Word = "try",
            Kind = CompletionItemKind.Keyword,
            Detail = "try {...}",
        },
        new()
        {
            Word = "catch",
            Kind = CompletionItemKind.Keyword,
            Detail = @"// must be preceded by a try statement
catch {...}",
            Documentation = "Catches an exception and executes the code in the catch block, with the exception available in the `$exception` variable."
        },
        new()
        {
            Word = "finally",
            Kind = CompletionItemKind.Keyword,
            Detail = @"// must be preceded by a try or catch statement
finally {...}",
        }
    };
}