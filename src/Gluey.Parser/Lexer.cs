namespace Gluey.Parser;

/// <summary>
/// Token types for the .gflow DSL
/// </summary>
public enum TokenType
{
    // Keywords
    Flow,
    From,
    Route,
    OnError,
    Schema,
    Migrate,
    Filter,
    Transform,
    Enrich,
    Description,

    // Literals
    String,
    Number,
    True,
    False,
    Null,

    // Identifiers
    Identifier,

    // Symbols
    LeftBrace,      // {
    RightBrace,     // }
    LeftBracket,    // [
    RightBracket,   // ]
    LeftParen,      // (
    RightParen,     // )
    Pipe,           // |
    Colon,          // :
    Arrow,          // ->
    Comma,          // ,
    Dot,            // .
    Question,       // ?

    // Operators
    And,            // &&
    Or,             // ||
    Equal,          // ==
    NotEqual,       // !=
    GreaterThan,    // >
    LessThan,       // <
    GreaterOrEqual, // >=
    LessOrEqual,    // <=
    Plus,           // +
    Minus,          // -
    Star,           // *
    Slash,          // /
    Ampersand,      // &
    Caret,          // ^
    Bang,           // !

    // Special
    Eof,
    Error
}

/// <summary>
/// Represents a token from the lexer with position tracking for error reporting
/// </summary>
public sealed record Token(
    TokenType Type,
    string Value,
    int Line,
    int Column
)
{
    public override string ToString() => $"{Type}({Value}) at {Line}:{Column}";
}

/// <summary>
/// Lexer for the .gflow DSL that tokenizes input into tokens for the parser
/// </summary>
public sealed class Lexer
{
    private readonly string _source;
    private int _position;
    private int _line;
    private int _column;

    private static readonly Dictionary<string, TokenType> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["flow"] = TokenType.Flow,
        ["from"] = TokenType.From,
        ["route"] = TokenType.Route,
        ["on_error"] = TokenType.OnError,
        ["schema"] = TokenType.Schema,
        ["migrate"] = TokenType.Migrate,
        ["filter"] = TokenType.Filter,
        ["transform"] = TokenType.Transform,
        ["enrich"] = TokenType.Enrich,
        ["description"] = TokenType.Description,
        ["true"] = TokenType.True,
        ["false"] = TokenType.False,
        ["null"] = TokenType.Null,
    };

    public Lexer(string source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _position = 0;
        _line = 1;
        _column = 1;
    }

    /// <summary>
    /// Tokenizes the entire source and returns all tokens
    /// </summary>
    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();

        while (true)
        {
            var token = NextToken();
            tokens.Add(token);

            if (token.Type == TokenType.Eof || token.Type == TokenType.Error)
                break;
        }

        return tokens;
    }

    /// <summary>
    /// Returns the next token from the source
    /// </summary>
    public Token NextToken()
    {
        SkipWhitespaceAndComments();

        if (IsAtEnd())
            return MakeToken(TokenType.Eof, "");

        int startLine = _line;
        int startColumn = _column;

        char c = Advance();

        // Single character tokens
        switch (c)
        {
            case '{': return MakeToken(TokenType.LeftBrace, "{", startLine, startColumn);
            case '}': return MakeToken(TokenType.RightBrace, "}", startLine, startColumn);
            case '[': return MakeToken(TokenType.LeftBracket, "[", startLine, startColumn);
            case ']': return MakeToken(TokenType.RightBracket, "]", startLine, startColumn);
            case '(': return MakeToken(TokenType.LeftParen, "(", startLine, startColumn);
            case ')': return MakeToken(TokenType.RightParen, ")", startLine, startColumn);
            case '|':
                if (Match('|'))
                    return MakeToken(TokenType.Or, "||", startLine, startColumn);
                return MakeToken(TokenType.Pipe, "|", startLine, startColumn);
            case ':': return MakeToken(TokenType.Colon, ":", startLine, startColumn);
            case ',': return MakeToken(TokenType.Comma, ",", startLine, startColumn);
            case '.': return MakeToken(TokenType.Dot, ".", startLine, startColumn);
            case '?': return MakeToken(TokenType.Question, "?", startLine, startColumn);
            case '+': return MakeToken(TokenType.Plus, "+", startLine, startColumn);
            case '*': return MakeToken(TokenType.Star, "*", startLine, startColumn);
            case '/': return MakeToken(TokenType.Slash, "/", startLine, startColumn);
            case '^': return MakeToken(TokenType.Caret, "^", startLine, startColumn);

            case '-':
                if (Match('>'))
                    return MakeToken(TokenType.Arrow, "->", startLine, startColumn);
                return MakeToken(TokenType.Minus, "-", startLine, startColumn);

            case '&':
                if (Match('&'))
                    return MakeToken(TokenType.And, "&&", startLine, startColumn);
                return MakeToken(TokenType.Ampersand, "&", startLine, startColumn);

            case '=':
                if (Match('='))
                    return MakeToken(TokenType.Equal, "==", startLine, startColumn);
                return MakeToken(TokenType.Error, $"Unexpected character '=' at {startLine}:{startColumn}. Did you mean '=='?", startLine, startColumn);

            case '!':
                if (Match('='))
                    return MakeToken(TokenType.NotEqual, "!=", startLine, startColumn);
                return MakeToken(TokenType.Bang, "!", startLine, startColumn);

            case '>':
                if (Match('='))
                    return MakeToken(TokenType.GreaterOrEqual, ">=", startLine, startColumn);
                return MakeToken(TokenType.GreaterThan, ">", startLine, startColumn);

            case '<':
                if (Match('='))
                    return MakeToken(TokenType.LessOrEqual, "<=", startLine, startColumn);
                return MakeToken(TokenType.LessThan, "<", startLine, startColumn);

            case '"':
            case '\'':
                return ScanString(c, startLine, startColumn);
        }

        // Numbers
        if (char.IsDigit(c))
            return ScanNumber(c, startLine, startColumn);

        // Identifiers and keywords
        if (IsIdentifierStart(c))
            return ScanIdentifier(c, startLine, startColumn);

        return MakeToken(TokenType.Error, $"Unexpected character '{c}' at {startLine}:{startColumn}", startLine, startColumn);
    }

    private void SkipWhitespaceAndComments()
    {
        while (!IsAtEnd())
        {
            char c = Peek();

            switch (c)
            {
                case ' ':
                case '\t':
                case '\r':
                    Advance();
                    break;

                case '\n':
                    _line++;
                    _column = 0; // Will be incremented to 1 by Advance()
                    Advance();
                    break;

                case '/':
                    if (PeekNext() == '/')
                    {
                        // Single-line comment
                        while (!IsAtEnd() && Peek() != '\n')
                            Advance();
                    }
                    else if (PeekNext() == '*')
                    {
                        // Multi-line comment
                        Advance(); // consume /
                        Advance(); // consume *

                        while (!IsAtEnd())
                        {
                            if (Peek() == '*' && PeekNext() == '/')
                            {
                                Advance(); // consume *
                                Advance(); // consume /
                                break;
                            }

                            if (Peek() == '\n')
                            {
                                _line++;
                                _column = 0;
                            }

                            Advance();
                        }
                    }
                    else
                    {
                        return;
                    }
                    break;

                default:
                    return;
            }
        }
    }

    private Token ScanString(char quote, int startLine, int startColumn)
    {
        var sb = new System.Text.StringBuilder();

        while (!IsAtEnd() && Peek() != quote)
        {
            char c = Peek();

            if (c == '\n')
            {
                return MakeToken(TokenType.Error, $"Unterminated string at {startLine}:{startColumn}", startLine, startColumn);
            }

            if (c == '\\' && !IsAtEnd())
            {
                Advance(); // consume backslash

                if (IsAtEnd())
                {
                    return MakeToken(TokenType.Error, $"Unterminated escape sequence in string at {startLine}:{startColumn}", startLine, startColumn);
                }

                char escaped = Advance();
                switch (escaped)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case '\'': sb.Append('\''); break;
                    default:
                        sb.Append('\\');
                        sb.Append(escaped);
                        break;
                }
            }
            else
            {
                sb.Append(Advance());
            }
        }

        if (IsAtEnd())
        {
            return MakeToken(TokenType.Error, $"Unterminated string at {startLine}:{startColumn}", startLine, startColumn);
        }

        Advance(); // consume closing quote

        return MakeToken(TokenType.String, sb.ToString(), startLine, startColumn);
    }

    private Token ScanNumber(char first, int startLine, int startColumn)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(first);

        // Check for hex notation 0x
        if (first == '0' && (Peek() == 'x' || Peek() == 'X'))
        {
            sb.Append(Advance()); // consume 'x' or 'X'

            while (!IsAtEnd() && IsHexDigit(Peek()))
            {
                sb.Append(Advance());
            }

            return MakeToken(TokenType.Number, sb.ToString(), startLine, startColumn);
        }

        // Integer part
        while (!IsAtEnd() && char.IsDigit(Peek()))
        {
            sb.Append(Advance());
        }

        // Decimal part
        if (!IsAtEnd() && Peek() == '.' && char.IsDigit(PeekNext()))
        {
            sb.Append(Advance()); // consume '.'

            while (!IsAtEnd() && char.IsDigit(Peek()))
            {
                sb.Append(Advance());
            }
        }

        // Exponent part (e.g., 1e10, 2.5e-3)
        if (!IsAtEnd() && (Peek() == 'e' || Peek() == 'E'))
        {
            sb.Append(Advance()); // consume 'e' or 'E'

            if (!IsAtEnd() && (Peek() == '+' || Peek() == '-'))
            {
                sb.Append(Advance());
            }

            while (!IsAtEnd() && char.IsDigit(Peek()))
            {
                sb.Append(Advance());
            }
        }

        return MakeToken(TokenType.Number, sb.ToString(), startLine, startColumn);
    }

    private Token ScanIdentifier(char first, int startLine, int startColumn)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(first);

        while (!IsAtEnd() && IsIdentifierPart(Peek()))
        {
            sb.Append(Advance());
        }

        string value = sb.ToString();

        // Check if it's a keyword
        if (Keywords.TryGetValue(value, out var keywordType))
        {
            return MakeToken(keywordType, value, startLine, startColumn);
        }

        return MakeToken(TokenType.Identifier, value, startLine, startColumn);
    }

    private bool IsIdentifierStart(char c)
    {
        return char.IsLetter(c) || c == '_';
    }

    private bool IsIdentifierPart(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    private bool IsHexDigit(char c)
    {
        return char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }

    private bool IsAtEnd()
    {
        return _position >= _source.Length;
    }

    private char Peek()
    {
        if (IsAtEnd()) return '\0';
        return _source[_position];
    }

    private char PeekNext()
    {
        if (_position + 1 >= _source.Length) return '\0';
        return _source[_position + 1];
    }

    private char Advance()
    {
        char c = _source[_position];
        _position++;
        _column++;
        return c;
    }

    private bool Match(char expected)
    {
        if (IsAtEnd()) return false;
        if (_source[_position] != expected) return false;

        _position++;
        _column++;
        return true;
    }

    private Token MakeToken(TokenType type, string value)
    {
        return new Token(type, value, _line, _column);
    }

    private Token MakeToken(TokenType type, string value, int line, int column)
    {
        return new Token(type, value, line, column);
    }
}
