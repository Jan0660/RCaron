# RCaron

A .NET shell and scripting language.
It is currently unusable as a shell, a little usable as a scripting language. You can also call it ř or Ř.

Documentation for the language is available at [rcaron.jan0660.dev](https://rcaron.jan0660.dev).

A simple number guessing game currently looks like this:

```rcaron
// we "open" a .NET namespace with open
open 'System'
// to use a .NET type we start it's name with a '#' and then access it's members with ':'
// from there we access the members of a variable, property or whatever with '.'
// variables don't have to be declared
$number = #Random:Shared.Next(1, 10000)
print 'Guess a number between 1 and 10000'
// 'loop' is a loop that can be exited with 'break'
loop {
    #Console:Write('Your guess: ')
    $guess = #Int32:Parse(#Console:ReadLine())
    // 'print' is a built-in function that prints arguments to the console with a space between them
    print 'You guessed:' $guess
    // operators look normal
    if ($guess < $number) {
        print 'Too low'
    }
    else if ($guess > $number) {
        print 'Too high'
    }
    else {
        print 'You guessed it!'
        break
    }
}
print 'congrats'
```

## Getting started

See [the documentation site](https://rcaron.jan0660.dev/getting-started).

## Getting help

Try to find if anything on [rcaron.jan0660.dev](https://rcaron.jan0660.dev) helps you.

You can [start a new GitHub discussion](https://github.com/Jan0660/RCaron/discussions/new?category=q-a).

## Structure of this repository

This repository contains the following projects:

- `RCaron`: The language itself
- `RCaron.Shell`: The RCaron shell
- `RCaron.LibrarySourceGenerator`: A source generator for creating libraries
- `RCaron.LibrarySourceGenerator.Attributes`: Attributes for the source generator
- `RCaron.AutoCompletion`: Auto completion that powers the language server and the shell
- `RCaron.LanguageServer`: A Language Server Protocol implementation
- `RCaron.Tests`: Unit tests
- `RCaron.FunLibrary`: Experimental stuff
- `RCaron.Benchmarks`: Just a basic benchmark for checking between language versions
- `RCaron.Jit`: An expression tree compiler for the language, allowing for faster execution at the cost of a slower "dry" run
- `RCaron.Jit.Tests`: Unit tests for the JIT (uses `RCaron.Tests`)
- `RCaron.Testing`: Just some testing stuff
- `Rcaron.Cli`: A basic command line interface, this is not the main RCaron experience
