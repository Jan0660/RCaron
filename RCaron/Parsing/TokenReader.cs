using System.Globalization;
using Log73;

namespace RCaron.Parsing;

// todo(perf): could be a struct so that it is kept on the stack?
public class TokenReader
{
    int _position;
    string _code;
    public bool ReturnIgnored { get; set; }
    public static readonly PosToken IgnorePosToken = new(TokenType.Ignore, (0, 0));
    public IParsingErrorHandler ErrorHandler { get; }

    public TokenReader(string code, IParsingErrorHandler errorHandler, bool returnIgnored = false)
    {
        this._code = code;
        this._position = 0;
        ReturnIgnored = returnIgnored;
        ErrorHandler = errorHandler;
    }

    public PosToken? Read()
    {
        if (_position >= _code.Length)
        {
            return null;
        }

        var txt = _code.AsSpan();

        void Skip(int count)
        {
            _position += count;
        }

        var initialPosition = _position;

        // variable
        if (txt[_position] == '$')
        {
            Skip(1);
            var index = CollectAlphaNumericAndSome(txt[_position..]);
            if (index == 0)
                ErrorHandler.Handle(ParsingException.LonelyVariableStart(GetLocation(initialPosition, 1)));
            _position += index;
            return new VariableToken((initialPosition, _position),
                _code.Substring(initialPosition + 1, _position - initialPosition - 1));
            // return new ValuePosToken(TokenType.VariableIdentifier, (initialPosition, position));
        }
        // shebang
        else if (txt.Length - _position > 1 && txt[_position] == '#' && txt[_position + 1] == '!')
        {
            _position += 2;
            // collect until line ending
            while (_position != txt.Length && txt[_position] != '\n')
                _position++;
            if (!ReturnIgnored)
                return IgnorePosToken;
            return new PosToken(TokenType.Ignore, (initialPosition, _position));
        }
        // string
        else if (txt[_position] == '\'')
        {
            Skip(1);
            var (index, str) = CollectString(txt[_position..]);
            if (index == 0)
                return null;
            _position += index;
            return new ConstToken(TokenType.String, (initialPosition, _position), str);
        }
        // whitespace
        else if (char.IsWhiteSpace(txt[_position]))
        {
            _position++;
            while (_position < txt.Length && char.IsWhiteSpace(txt[_position]))
                ++_position;
            if (!ReturnIgnored)
                return IgnorePosToken;
            return new PosToken(TokenType.Whitespace, (initialPosition, _position));
        }
        // comma
        else if (txt[_position] == ',')
        {
            _position++;
            return new PosToken(TokenType.Comma, (initialPosition, _position));
        }
        // line ending - semicolon
        else if (txt[_position] == ';')
        {
            _position++;
            return new PosToken(TokenType.LineEnding, (initialPosition, _position));
        }
        else if (txt[_position] == '{')
        {
            _position++;
            return new BlockPosToken(TokenType.BlockStart, (initialPosition, _position));
        }
        else if (txt[_position] == '}')
        {
            _position++;
            return new BlockPosToken(TokenType.BlockEnd, (initialPosition, _position));
        }
        else if (txt[_position] == '(')
        {
            _position++;
            return new BlockPosToken(TokenType.SimpleBlockStart, (initialPosition, _position));
        }
        else if (txt[_position] == ')')
        {
            _position++;
            return new BlockPosToken(TokenType.SimpleBlockEnd, (initialPosition, _position));
        }
        // hex, decimal or integer number
        else if (char.IsDigit(txt[_position]))
        {
            var isHex = txt.Length - _position > 1 && txt[_position] == '0' && txt[_position + 1] is 'x' or 'X';
            var tokenType = isHex ? TokenType.HexNumber : TokenType.Number;
            if (isHex)
                _position += 2;
            var numberStart = _position;
            var isDecimal = false;
            var hasSpacing = false;
            int index;
            if (isHex)
            {
                (index, hasSpacing) = CollectHexNumber(txt[_position..]);
                if (index == 0)
                    ErrorHandler.Handle(
                        ParsingException.InvalidHexNumber(GetLocation(initialPosition, _position - initialPosition)));
            }
            else
                (index, isDecimal, hasSpacing) = CollectAnyNumber(txt[_position..]);

            if (isDecimal)
                tokenType = TokenType.DecimalNumber;
            _position += index;
            var type = GetNumberSuffixType(txt[_position..], isHex, isDecimal, out var suffixLength);
            _position += suffixLength;
            object value;
            if (hasSpacing)
            {
                // create span without the underscores
                var span = txt[numberStart..(numberStart + index)];
                Span<char> spanWithoutUnderscores = stackalloc char[span.Length];
                var resultSpanIndex = 0;
                for (var i = 0; i < span.Length; i++)
                {
                    if (span[i] == '_')
                        continue;
                    spanWithoutUnderscores[resultSpanIndex] = span[i];
                    resultSpanIndex++;
                }

                if (isHex && resultSpanIndex == 0)
                    ErrorHandler.Handle(
                        ParsingException.InvalidHexNumber(GetLocation(initialPosition, _position - initialPosition)));

                value = ParseNumber(spanWithoutUnderscores[..resultSpanIndex], type, isHex);
            }
            else
                value = ParseNumber(txt[numberStart..(numberStart + index)], type, isHex);

            return new ConstToken(tokenType, (initialPosition, _position), value);
        }
        // single line comment
        else if (txt.Length - _position > 1 && txt[_position] == '/' && txt[_position + 1] == '/')
        {
            _position += 2;
            while (_position < txt.Length && txt[_position] != '\n')
                _position++;
            if (!ReturnIgnored)
                return IgnorePosToken;
            return new PosToken(TokenType.Comment, (initialPosition, _position));
        }
        // // multiline line comment
        else if (txt.Length - _position > 1 && txt[_position] == '/' && txt[_position + 1] == '#')
        {
            _position += 2;
            while (txt[_position] != '#' && txt[_position + 1] != '/')
                _position++;
            _position += 3;
            if (!ReturnIgnored)
                return IgnorePosToken;
            return new PosToken(TokenType.Comment, (initialPosition, _position));
        }
        // extern thing
        else if (txt[_position] == '#')
        {
            _position++;
            _position += CollectAlphaNumericAndSomeAndDot(txt[_position..]);
            return new ExternThingToken((initialPosition, _position),
                _code.Substring(initialPosition + 1, _position - initialPosition - 1));
        }
        // array literal start
        else if (txt.Length - _position > 1 && txt[_position] == '@' && txt[_position + 1] == '(')
        {
            _position++;
            return new PosToken(TokenType.ArrayLiteralStart, (initialPosition, _position));
        }
        // range operator
        // it is here, instead of in CollectOperation, because it would conflict with TokenType.Dot
        else if (txt.Length - _position > 1 && txt[_position] == '.' && txt[_position + 1] == '.'
                 && (txt.Length - _position == 2 || txt[_position + 2] != '.'))
        {
            _position += 2;
            return new PosToken(TokenType.Range, (initialPosition, _position));
        }
        // dot
        else if (txt[_position] == '.')
        {
            _position++;
            return new PosToken(TokenType.Dot, (initialPosition, _position));
        }
        // normal array indexer start
        else if (txt[_position] == '[')
        {
            _position++;
            return new BlockPosToken(TokenType.IndexerStart, (initialPosition, _position));
        }
        // normal array indexer end
        else if (txt[_position] == ']')
        {
            _position++;
            return new BlockPosToken(TokenType.IndexerEnd, (initialPosition, _position));
        }
        // colon
        else if (txt[_position] == ':')
        {
            _position++;
            return new PosToken(TokenType.Colon, (initialPosition, _position));
        }
        // paths
        else if ((txt.Length - _position > 2 && char.IsLetter(txt[_position]) &&
                  txt[_position + 1] == ':' &&
                  (txt[_position + 2] == '/' ||
                   txt[_position + 2] == '\\')))
        {
            var (index, path, _) = CollectPathOrKeyword(txt[_position..], allowFirstDoubleDot: true);
            if (index == 0)
                return null;
            _position += index;
            return new ConstToken(TokenType.Path, (initialPosition, _position), path);
        }
        // executable keyword
        else if (txt[_position] == '@')
        {
            _position++;
            var (index, str, isPath) = CollectPathOrKeyword(txt[_position..]);
            if (index == 0)
                throw new("Invalid token at position " + _position);
            _position += index;
            return isPath
                ? new ConstToken(TokenType.Path, (initialPosition, _position), str, true)
                : new KeywordToken((initialPosition, _position), str, true);
        }
        // operation
        else
        {
            var (index, tokenType, op) = CollectOperation(txt[_position..]);
            // collect a keyword e.g. "println"
            if (index == 0 || (txt.Length - _position - index > 0 &&
                               ((op == OperationEnum.Divide && txt[_position + index] != ' ') ||
                                (op == OperationEnum.Multiply && txt[_position + index] != ' '))))
            {
                (index, var str, var isPath) = CollectPathOrKeyword(txt[_position..]);
                if (index == 0)
                    throw new("Invalid token at position " + _position);
                _position += index;
                return isPath
                    ? new ConstToken(TokenType.Path, (initialPosition, _position), str)
                    : new KeywordToken((initialPosition, _position), str);
            }

            _position += index;
            // match "value" operations
            if (tokenType == TokenType.Operator || tokenType == TokenType.MathOperator)
                return new ValueOperationValuePosToken(tokenType, (initialPosition, _position), op);

            return new OperationPosToken(tokenType, (initialPosition, _position), op);
        }
    }

    private (int index, bool hasSpacing) CollectHexNumber(ReadOnlySpan<char> span)
    {
        var index = 0;
        var hasSpacing = false;
        while (index < span.Length && ((span[index] >= '0' && span[index] <= '9') ||
                                       (span[index] >= 'a' && span[index] <= 'f') ||
                                       (span[index] >= 'A' && span[index] <= 'F') || span[index] == '_'))
        {
            if (span[index] == '_')
                hasSpacing = true;
            index++;
        }

        return (index, hasSpacing);
    }

    public int CollectExecutableKeyword(in ReadOnlySpan<char> span)
    {
        var index = 0;
        while (index < span.Length && ((span[index] >= '0' && span[index] <= '9') ||
                                       (span[index] >= 'a' && span[index] <= 'z') ||
                                       (span[index] >= 'A' && span[index] <= 'Z') || span[index] == '_' ||
                                       span[index] == '-'))
            index++;
        return index;
    }

    public (int index, string path, bool isPath) CollectPathOrKeyword(in ReadOnlySpan<char> span,
        bool allowFirstDoubleDot = false)
    {
        var index = 0;
        var isPath = false;
        Span<char> resultSpan = stackalloc char[span.Length];
        var path = new SpanStringBuilder(ref resultSpan);
        while (index < span.Length && span[index] != ' ' && span[index] != '\n' && span[index] != '\r' &&
               span[index] != ',' &&
               span[index] != '(' && span[index] != '{' && span[index] != '[' &&
               span[index] != ')' && span[index] != ']' && span[index] != '}' &&
               span[index] != ';' && span[index] != ':' && span[index] != '.')
        {
            if (!((span[index] >= '0' && span[index] <= '9') || (span[index] >= 'a' && span[index] <= 'z') ||
                  (span[index] >= 'A' && span[index] <= 'Z') || span[index] == '_' || span[index] == '-'))
                isPath = true;
            if (span[index] == '`')
                index++;
            path.Append(span[index]);
            index++;
            if (allowFirstDoubleDot && index < span.Length && span[index] == ':')
                path.Append(span[index++]);
        }

        return (index, path.ToString(), isPath);
    }

    public (int index, bool isDecimal, bool hasSpacing) CollectAnyNumber(in ReadOnlySpan<char> span)
    {
        var index = 0;
        var isDecimal = false;
        var hasSpacing = false;
        while (index < span.Length && (char.IsDigit(span[index]) || span[index] == '.' || span[index] == '_'))
        {
            if (span[index] == '.' && char.IsDigit(span[index + 1]))
                isDecimal = true;
            else if (span[index] == '.')
                return (index, isDecimal, hasSpacing);
            else if (span[index] == '_')
                hasSpacing = true;
            index++;
        }

        return (index, isDecimal, hasSpacing);
    }

    public int CollectAlphaNumeric(in ReadOnlySpan<char> span)
    {
        var index = 0;
        while (index < span.Length && ((span[index] >= '0' && span[index] <= '9') ||
                                       (span[index] >= 'a' && span[index] <= 'z') ||
                                       (span[index] >= 'A' && span[index] <= 'Z')))
            index++;
        return index;
    }

    public int CollectAlphaNumericAndSome(in ReadOnlySpan<char> span)
    {
        var index = 0;
        while (index < span.Length && ((span[index] >= '0' && span[index] <= '9') ||
                                       (span[index] >= 'a' && span[index] <= 'z') ||
                                       (span[index] >= 'A' && span[index] <= 'Z') || span[index] == '_'))
            index++;
        return index;
    }

    public int CollectAlphaNumericAndSomeAndDot(in ReadOnlySpan<char> span)
    {
        var index = 0;
        while (index < span.Length && ((span[index] >= '0' && span[index] <= '9') ||
                                       (span[index] >= 'a' && span[index] <= 'z') ||
                                       (span[index] >= 'A' && span[index] <= 'Z') ||
                                       (span[index] == '_' || span[index] == '.')))
            index++;
        return index;
    }

    public (int endIndex, string value) CollectString(in ReadOnlySpan<char> span)
    {
        // get index of unescaped ending '
        var index = 0;
        while (span[index] != '\'')
        {
            if (span[index] == '\\')
                index++;
            index++;
        }

        index++;
        // assemble the string
        Span<char> g = stackalloc char[span.Length];
        var str = new SpanStringBuilder(ref g);
        for (var i = 0; i < index - 1; i++)
        {
            if (span[i] == '\\')
            {
                i++;
                // single quote
                if (span[i] == '\'')
                    str.Append('\'');
                // backslash
                else if (span[i] == '\\')
                    str.Append('\\');
                // null
                else if (span[i] == '0')
                    str.Append('\0');
                // alert
                else if (span[i] == 'a')
                    str.Append('\a');
                // backspace
                else if (span[i] == 'b')
                    str.Append('\b');
                // form feed
                else if (span[i] == 'f')
                    str.Append('\f');
                // new line
                else if (span[i] == 'n')
                    str.Append('\n');
                // carriage return
                else if (span[i] == 'r')
                    str.Append('\r');
                // horizontal tab
                else if (span[i] == 't')
                    str.Append('\t');
                // vertical tab
                else if (span[i] == 'v')
                    str.Append('\v');
                else if (span[i] == 'u' || span[i] == 'U')
                {
                    var length = span[i] == 'u' ? 4 : 8;
                    var escape = GetCharactersInStringForUnicodeEscape(span, i + 1, length);
                    if (escape.Length != length)
                    {
                        ErrorHandler.Handle(ParsingException.TooShortUnicodeEscape(escape, length,
                            GetLocation(span, i - 1, 2 + escape.Length)));
                        continue;
                    }

                    if (int.TryParse(escape, NumberStyles.HexNumber, null, out var code))
                    {
                        if (length == 4)
                            str.Append((char)code);
                        else
                            str.Append(char.ConvertFromUtf32(code));
                    }
                    else
                        ErrorHandler.Handle(
                            ParsingException.InvalidUnicodeEscape(escape, GetLocation(span, i - 1, 2 + length)));

                    i += length;
                }
                else
                    ErrorHandler.Handle(ParsingException.InvalidEscapeSequence(span[i], GetLocation(span, i - 1, 2)));

                continue;
            }

            str.Append(span[i]);
        }

        return (index, str.ToString());
    }

    public ReadOnlySpan<char> GetCharactersInStringForUnicodeEscape(in ReadOnlySpan<char> span, int start, int length)
    {
        var index = start;
        while (index < span.Length && (index - start) < length
                                   && !(span[index] == '\'' && span[index - 1] != '\\'))
        {
            index++;
        }

        return span[start..index];
    }

    public bool IsMatch(in ReadOnlySpan<char> span, ReadOnlySpan<char> toMatch)
    {
        for (var i = 0; i < toMatch.Length; i++)
        {
            if (span[i] != toMatch[i])
                return false;
        }

        return true;
    }

    public (int, TokenType tokenType, OperationEnum operationEnum) CollectOperation(in ReadOnlySpan<char> span)
    {
        // comparison
        if (IsMatch(in span, Operations.IsEqualOp))
            return (2, TokenType.ComparisonOperation, OperationEnum.IsEqual);
        if (IsMatch(in span, Operations.IsNotEqualOp))
            return (2, TokenType.ComparisonOperation, OperationEnum.IsNotEqual);
        if (IsMatch(in span, Operations.IsGreaterOrEqualOp))
            return (2, TokenType.ComparisonOperation, OperationEnum.IsGreaterOrEqual);
        if (IsMatch(in span, Operations.IsLessOrEqualOp))
            return (2, TokenType.ComparisonOperation, OperationEnum.IsLessOrEqual);
        if (IsMatch(in span, Operations.IsGreaterOp))
            return (1, TokenType.ComparisonOperation, OperationEnum.IsGreater);
        if (IsMatch(in span, Operations.IsLessOp))
            return (1, TokenType.ComparisonOperation, OperationEnum.IsLess);
        // logical
        if (IsMatch(in span, Operations.AndOp))
            return (2, TokenType.LogicalOperation, OperationEnum.And);
        if (IsMatch(in span, Operations.OrOp))
            return (2, TokenType.LogicalOperation, OperationEnum.Or);
        // other
        if (IsMatch(in span, Operations.AssignmentOp))
            return (1, TokenType.Operation, OperationEnum.Assignment);
        // unary
        if (IsMatch(in span, Operations.UnaryIncrementOp))
            return (2, TokenType.UnaryOperation, OperationEnum.UnaryIncrement);
        if (IsMatch(in span, Operations.UnaryDecrementOp))
            return (2, TokenType.UnaryOperation, OperationEnum.UnaryDecrement);
        // math
        if (IsMatch(in span, Operations.SumOp))
            return (1, TokenType.MathOperator, OperationEnum.Sum);
        if (IsMatch(in span, Operations.SubtractOp))
            return (1, TokenType.MathOperator, OperationEnum.Subtract);
        if (IsMatch(in span, Operations.MultiplyOp))
            return (1, TokenType.MathOperator, OperationEnum.Multiply);
        if (IsMatch(in span, Operations.DivideOp))
            return (1, TokenType.MathOperator, OperationEnum.Divide);
        if (IsMatch(in span, Operations.ModuloOp))
            return (1, TokenType.MathOperator, OperationEnum.Modulo);
        return (0, 0, 0);
    }

    public TextSpan GetLocation(int startIndex, int length)
        => new(startIndex, length);

    public TextSpan GetLocation(ReadOnlySpan<char> subSpan, int startIndexInSubSpan, int length)
        => new(startIndexInSubSpan + (_code.Length - subSpan.Length), length);

    public NumberType GetNumberSuffixType(ReadOnlySpan<char> span, bool isHex, bool isDouble, out int suffixLength)
    {
        var isUnsigned = false;
        var isInteger = false;
        var isLong = !isDouble;
        var isFloat = false;
        var isDecimal = false;

        if (span.Length == 0)
        {
            suffixLength = 0;
            return isDouble ? NumberType.Double :
                isHex ? NumberType.UnsignedLong : NumberType.Long;
        }

        var i = 0;
        for (; i < 2 && i < span.Length; i++)
        {
            if (span[i] is 'u' or 'U')
                isUnsigned = true;
            else
            {
                // set the things that could be already set by default to false
                var defaults = (isDouble, isLong);
                isDouble = false;
                isLong = false;
                if (span[i] is 'i' or 'I')
                    isInteger = true;
                else if (span[i] is 'l' or 'L')
                    isLong = true;
                else if (span[i] is 'f' or 'F')
                    isFloat = true;
                else if (span[i] is 'd' or 'D')
                    isDouble = true;
                else if (span[i] is 'm' or 'M')
                    isDecimal = true;
                else
                {
                    // restore the defaults if there was no suffix
                    if (i is 0 or 1)
                        (isDouble, isLong) = defaults;
                    break;
                }
            }
        }

        suffixLength = i;

        if (isInteger)
            return isUnsigned ? NumberType.UnsignedInteger : NumberType.Integer;
        if (isLong)
            return isUnsigned ? NumberType.UnsignedLong : NumberType.Long;
        if (isFloat || isDouble || isDecimal)
        {
            if (isUnsigned)
                ErrorHandler.Handle(ParsingException.InvalidNumberSuffix(GetLocation(span, 0, i + 1),
                    unsignedOnFloatingPoint: true));
            if (isHex)
                ErrorHandler.Handle(ParsingException.InvalidNumberSuffix(GetLocation(span, 0, i + 1),
                    hexOnFloatingPoint: true));
        }

        if (isFloat)
            return NumberType.Float;
        if (isDouble)
            return NumberType.Double;
        if (isDecimal)
            return NumberType.Decimal;

        ErrorHandler.Handle(ParsingException.InvalidNumberSuffix(GetLocation(span, 0, i + 1)));
        return NumberType.Long;
    }

    public object ParseNumber(ReadOnlySpan<char> span, NumberType numberType, bool isHex)
        => numberType switch
        {
            NumberType.Integer => int.Parse(span, isHex ? NumberStyles.HexNumber : NumberStyles.Integer,
                CultureInfo.InvariantCulture),
            NumberType.UnsignedInteger => uint.Parse(span, isHex ? NumberStyles.HexNumber : NumberStyles.Integer,
                CultureInfo.InvariantCulture),
            NumberType.Long => long.Parse(span, isHex ? NumberStyles.HexNumber : NumberStyles.Integer,
                CultureInfo.InvariantCulture),
            NumberType.UnsignedLong => ulong.Parse(span, isHex ? NumberStyles.HexNumber : NumberStyles.Integer,
                CultureInfo.InvariantCulture),
            NumberType.Float => float.Parse(span, CultureInfo.InvariantCulture),
            NumberType.Double => double.Parse(span, CultureInfo.InvariantCulture),
            NumberType.Decimal => decimal.Parse(span, CultureInfo.InvariantCulture),
            _ => throw new ArgumentOutOfRangeException(nameof(numberType), numberType, null)
        };

    public enum NumberType
    {
        Integer,
        UnsignedInteger,
        Long,
        UnsignedLong,
        Float,
        Double,
        Decimal,
    }
}