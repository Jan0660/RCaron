// See https://aka.ms/new-console-template for more information

using System.Linq.Expressions;
using System.Reflection;
using RCaron;
using Microsoft.Scripting;
using RCaron.Jit;

Console.WriteLine("Hello, World!");

var parsed = RCaronRunner.Parse(@"$h = 0 + 1;
print $h $h;
print 'a string';
print 'a string'.Length;");

var block = Compiler.CompileToBlock(parsed);
var lambda = Expression.Lambda(block.blockExpression).Compile();
// Microsoft.Scripting.Interpreter
// var intt =new
lambda.DynamicInvoke();