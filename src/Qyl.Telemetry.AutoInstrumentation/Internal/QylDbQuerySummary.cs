using System.Data;
using System.Text;

namespace Qyl.Telemetry.AutoInstrumentation.Internal;

/// <summary>
/// Derives <c>db.operation.name</c> and <c>db.query.summary</c> from a command: every recognised
/// operation keyword, each followed by the schema object it targets, in query order and bounded to
/// a few pairs. Only identifiers are copied — literals, parameters, and comments are skipped — so
/// the result is a grouping key, never a value.
/// </summary>
internal static class QylDbQuerySummary
{
    private const string Call = "CALL";
    private const int MaxOperations = 4;

    public static (string? Operation, string? Summary) Describe(CommandType commandType, string? commandText)
    {
        if (commandType is not CommandType.StoredProcedure)
            return Describe(commandText);

        var position = 0;
        var procedure = commandText is not null && TryReadName(commandText, ref position, out var name, out _) ? name : null;
        return (Call, procedure is null ? Call : Call + " " + procedure);
    }

    public static (string? Operation, string? Summary) Describe(string? commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
            return (null, null);

        string? operation = null;
        var summary = new StringBuilder();
        var operations = 0;
        var targetExpected = false;
        var position = 0;

        while (operations < MaxOperations && TryReadName(commandText, ref position, out var name, out var quoted))
        {
            if (targetExpected)
            {
                targetExpected = false;
                if (quoted || IsIdentifier(name))
                {
                    summary.Append(' ').Append(name);
                    continue;
                }
            }

            if (quoted)
                continue;

            var keyword = name.ToUpperInvariant();
            if (IsOperation(keyword))
            {
                operation ??= keyword;
                operations++;
                if (summary.Length > 0)
                    summary.Append(' ');
                summary.Append(keyword);
                targetExpected = keyword is "UPDATE" or Call or "EXEC" or "EXECUTE";
                continue;
            }

            targetExpected = keyword is "FROM" or "INTO" or "JOIN" or "TABLE" or "VIEW" or "INDEX" or "PROCEDURE" or "FUNCTION";
        }

        return operation is null ? (null, null) : (operation, summary.ToString());
    }

    private static bool IsOperation(string keyword)
        => keyword is "SELECT" or "INSERT" or "UPDATE" or "DELETE" or "MERGE" or "CREATE" or "ALTER" or "DROP" or "TRUNCATE" or Call or "EXEC" or "EXECUTE";

    private static bool IsIdentifier(string name)
        => name.Length > 0 && (char.IsLetter(name[0]) || name[0] is '_' or '#' or '@' or '$');

    private static bool IsIdentifierCharacter(char character)
        => char.IsLetterOrDigit(character) || character is '_' or '#' or '@' or '$';

    // A name is one or more dot-joined segments; a segment is a plain word or a delimited identifier.
    private static bool TryReadName(string text, ref int position, out string name, out bool quoted)
    {
        name = string.Empty;
        quoted = false;

        while (position < text.Length)
        {
            var current = text[position];
            if (char.IsWhiteSpace(current))
            {
                position++;
                continue;
            }

            if (current is '\'')
            {
                SkipLiteral(text, ref position);
                continue;
            }

            if (current is '-' && position + 1 < text.Length && text[position + 1] is '-')
            {
                while (position < text.Length && text[position] is not '\n')
                    position++;
                continue;
            }

            if (current is '/' && position + 1 < text.Length && text[position + 1] is '*')
            {
                var end = text.IndexOf("*/", position + 2, StringComparison.Ordinal);
                position = end < 0 ? text.Length : end + 2;
                continue;
            }

            if (!IsSegmentStart(current))
            {
                position++;
                continue;
            }

            var builder = new StringBuilder();
            while (ReadSegment(text, ref position, builder, ref quoted) && position < text.Length && text[position] is '.')
            {
                builder.Append('.');
                position++;
            }

            if (builder.Length is 0)
                continue;

            name = builder.ToString();
            return true;
        }

        return false;
    }

    private static bool IsSegmentStart(char character)
        => IsIdentifierCharacter(character) || character is '[' or '"' or '`';

    private static bool ReadSegment(string text, ref int position, StringBuilder builder, ref bool quoted)
    {
        if (position >= text.Length)
            return false;

        var current = text[position];
        if (current is '[' or '"' or '`')
        {
            var close = current is '[' ? ']' : current;
            var start = position + 1;
            var end = text.IndexOf(close, start);
            if (end < 0)
                end = text.Length;
            builder.Append(text, start, end - start);
            position = Math.Min(end + 1, text.Length);
            quoted = true;
            return true;
        }

        if (!IsIdentifierCharacter(current))
            return false;

        var wordStart = position;
        while (position < text.Length && IsIdentifierCharacter(text[position]))
            position++;
        builder.Append(text, wordStart, position - wordStart);
        return true;
    }

    private static void SkipLiteral(string text, ref int position)
    {
        position++;
        while (position < text.Length)
        {
            if (text[position] is '\'')
            {
                position++;
                if (position < text.Length && text[position] is '\'')
                {
                    position++;
                    continue;
                }

                return;
            }

            position++;
        }
    }
}
