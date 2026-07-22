using System.Text;
using Google.Protobuf;

namespace LogMonitoring.OtlpProto;

/// <summary>
/// Parses the current OTLP JSON Kafka format while retaining compatibility
/// with Protobuf records written before the wire-format migration.
/// </summary>
public static class OtlpPayloadParser
{
    public static T ParseJsonOrProtobuf<T>(MessageParser<T> parser, byte[] data)
        where T : IMessage<T>
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(data);

        return IsJson(data)
            ? parser.ParseJson(Encoding.UTF8.GetString(data))
            : parser.ParseFrom(data);
    }

    private static bool IsJson(ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            if (value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            {
                continue;
            }

            return value == (byte)'{' || value == (byte)'[';
        }

        return false;
    }
}
