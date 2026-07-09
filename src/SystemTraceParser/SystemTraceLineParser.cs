using System.Globalization;
using System.Text.RegularExpressions;

namespace LogMonitoring.SystemTraceParser;

public static class SystemTraceLineParser
{
    private static readonly Regex SlashDelimitedKeyValuePattern = new(@"\s*/(?=\s*[A-Za-z][A-Za-z0-9_]*\s*=)", RegexOptions.Compiled);

    public static SystemTraceEvent Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new ArgumentException("SystemTrace line cannot be empty.", nameof(line));
        }

        var columns = line.Split('|').Select(column => column.Trim()).ToArray();
        if (columns.Length < 9)
        {
            throw new FormatException($"SystemTrace line has {columns.Length} columns; expected at least 9.");
        }

        var traceEvent = new SystemTraceEvent
        {
            Timestamp = DateTimeOffset.Parse(columns[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            LogType = columns[1],
            Severity = ParseInt(columns[2], "severity"),
            Module = columns[3],
            Group = columns[4],
            SourceNamespace = columns[5],
            SourceSenderType = columns[6],
            SourceMethodName = columns[7],
            UserName = EmptyToNull(columns[8])
        };

        foreach (var segment in columns.Skip(9))
        {
            ParseSegment(traceEvent, segment);
        }

        return traceEvent;
    }

    private static void ParseSegment(SystemTraceEvent traceEvent, string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return;
        }

        var trimmed = segment.Trim();
        if (trimmed.Contains('/') &&
            !StartsWithKey(trimmed, "Message") &&
            !StartsWithKey(trimmed, "Data"))
        {
            foreach (var part in SlashDelimitedKeyValuePattern.Split(trimmed))
            {
                if (!string.IsNullOrWhiteSpace(part))
                {
                    ParseKeyValue(traceEvent, part.Trim());
                }
            }

            return;
        }

        ParseKeyValue(traceEvent, trimmed);
    }

    private static void ParseKeyValue(SystemTraceEvent traceEvent, string segment)
    {
        var separatorIndex = FindSeparator(segment);
        if (separatorIndex < 0)
        {
            return;
        }

        var key = segment[..separatorIndex].Trim();
        var value = segment[(separatorIndex + 1)..].Trim();

        switch (key.ToUpperInvariant())
        {
            case "SESSIONID":
                traceEvent.SessionId = EmptyToNull(value);
                break;
            case "CLIENTREQUESTTRANID":
                traceEvent.ClientRequestTranId = EmptyToNull(value);
                break;
            case "OPERATIONID":
                traceEvent.OperationId = EmptyToNull(value);
                break;
            case "THREADID":
                traceEvent.ThreadId = ParseNullableInt(value);
                break;
            case "TOTALELAPSED":
                traceEvent.TotalElapsedSeconds = ParseNullableDouble(value);
                break;
            case "LASTLOGELAPSED":
                traceEvent.LastLogElapsedSeconds = ParseNullableDouble(value);
                break;
            case "MESSAGE":
                traceEvent.Message = EmptyToNull(value);
                break;
            case "DATA":
                traceEvent.Data = EmptyToNull(value);
                break;
            default:
                if (!string.IsNullOrWhiteSpace(key))
                {
                    traceEvent.TraceInfo[key] = value;
                }
                break;
        }
    }

    private static int FindSeparator(string segment)
    {
        var equalsIndex = segment.IndexOf('=');
        var colonIndex = segment.IndexOf(':');

        return (equalsIndex, colonIndex) switch
        {
            (>= 0, >= 0) => Math.Min(equalsIndex, colonIndex),
            (>= 0, _) => equalsIndex,
            (_, >= 0) => colonIndex,
            _ => -1
        };
    }

    private static bool StartsWithKey(string value, string key)
    {
        return value.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith(key + " =", StringComparison.OrdinalIgnoreCase) ||
               value.Equals(key, StringComparison.OrdinalIgnoreCase);
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int ParseInt(string value, string fieldName)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            throw new FormatException($"Invalid {fieldName} value '{value}'.");
        }

        return result;
    }

    private static int? ParseNullableInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static double? ParseNullableDouble(string value)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }
}
