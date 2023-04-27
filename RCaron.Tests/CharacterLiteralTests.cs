using RCaron.Parsing;

namespace RCaron.Tests;

// we inherit from Tests in SingleQuote and DoubleQuote so that we can test both
// CharacterLiteralTests exist just so SingleQuote and DoubleQuote are in a nice group :)
public class CharacterLiteralTests
{
    public abstract class Tests
    {
        public char Quote { get; }
        private readonly char _oppositeQuote;

        protected Tests(char quote, char oppositeQuote)
        {
            Quote = quote;
            _oppositeQuote = oppositeQuote;
        }

        [Theory]
        [InlineData("a", 'a')]
        [InlineData("\\u0159", '\u0159')]
        [InlineData("\\r", '\r')]
        [InlineData("\\\"", '"')]
        [InlineData("\\\\", '\\')]
        public void CharacterLiteral(string input, char expected)
        {
            var m = TestRunner.Run($@"$h = @{Quote}{input}{Quote}");
            var type = m.AssertVariableIsType<char>("h");
            Assert.Equal(expected, type);
        }

        [Theory]
        [InlineData("aa")]
        [InlineData("")]
        public void InvalidCharacterLiteral(string input)
        {
            ExtraAssert.ThrowsParsingCode(() => RCaronParser.Parse($"$h = @{Quote}{input}{Quote}"),
                RCaronExceptionCode.InvalidCharacterLiteral);
        }

        [Theory]
        [InlineData("\\u123", RCaronExceptionCode.TooShortUnicodeEscape)]
        [InlineData("\\U12345678", RCaronExceptionCode.InvalidEscape)]
        public void InvalidEscapesInCharacterLiteral(string input, RCaronExceptionCode code)
        {
            ExtraAssert.ThrowsParsingCode(() => RCaronParser.Parse($"$h = @{Quote}{input}{Quote}"), code);
        }

        [Fact]
        public void OppositeQuote()
        {
            var m = TestRunner.Run($@"$h = @{Quote}{_oppositeQuote}{Quote}");
            var type = m.AssertVariableIsType<char>("h");
            Assert.Equal(_oppositeQuote, type);
        }

        [Fact]
        public void UnterminatedCharacterLiteral()
        {
            ExtraAssert.ThrowsParsingCode(() => RCaronParser.Parse($"$h = @{Quote}aaaa"),
                RCaronExceptionCode.UnterminatedCharacterLiteral);
        }
    }

    public class SingleQuote : Tests
    {
        public SingleQuote() : base('\'', '"')
        {
        }
    }

    [Collection("Character Literal")]
    public class DoubleQuote : Tests
    {
        public DoubleQuote() : base('"', '\'')
        {
        }
    }
}