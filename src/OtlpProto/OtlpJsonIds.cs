using Google.Protobuf;

namespace LogMonitoring.OtlpProto;

/// <summary>
/// Decodes OTLP trace and span identifiers into lowercase hex.
/// </summary>
/// <remarks>
/// OTLP/JSON deviates from the canonical Protobuf JSON mapping: it encodes <c>trace_id</c>
/// and <c>span_id</c> as lowercase hex strings, whereas Protobuf JSON encodes every
/// <c>bytes</c> field as base64. Google.Protobuf's <c>JsonParser</c> follows the Protobuf
/// spec, so it base64-decodes the hex string instead of hex-decoding it.
///
/// That failure is silent and total: hex characters are all valid base64, and the ID
/// lengths (32 and 16 characters) are multiples of 4, so the decode always succeeds and
/// yields 24 or 12 bytes of garbage rather than the intended 16 or 8.
///
/// The mangling is reversible. Base64 round-trips, so re-encoding those bytes reproduces
/// the original hex string exactly. Length is what distinguishes the two cases: a
/// correctly decoded ID has its expected byte length, a mangled one does not.
/// </remarks>
public static class OtlpJsonIds
{
    /// <summary>Byte length of a decoded W3C trace ID.</summary>
    public const int TraceIdBytes = 16;

    /// <summary>Byte length of a decoded W3C span ID.</summary>
    public const int SpanIdBytes = 8;

    /// <summary>
    /// Returns <paramref name="id"/> as lowercase hex, repairing the base64 misparse
    /// described above. Returns null when the ID is absent.
    /// </summary>
    /// <param name="id">The identifier as decoded by the Protobuf JSON parser.</param>
    /// <param name="expectedBytes">
    /// <see cref="TraceIdBytes"/> or <see cref="SpanIdBytes"/> — the length the ID has
    /// when it was decoded correctly.
    /// </param>
    public static string? ToHex(ByteString id, int expectedBytes)
    {
        if (id.IsEmpty)
        {
            return null;
        }

        byte[] bytes = id.ToByteArray();

        if (bytes.Length == expectedBytes)
        {
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        // Mangled by the base64 misparse: recover the hex string the sender actually wrote.
        string recovered = Convert.ToBase64String(bytes);

        return IsLowercaseHex(recovered, expectedBytes * 2)
            ? recovered
            // Neither a valid ID nor a recoverable hex string. Preserve the bytes rather
            // than dropping the field, so a malformed ID stays visible during triage.
            : Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool IsLowercaseHex(string value, int expectedLength)
    {
        if (value.Length != expectedLength)
        {
            return false;
        }

        foreach (char c in value)
        {
            bool isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }
}
