using LogMonitoring.SystemTraceParser;

namespace LogMonitoring.Tests.SystemTraceParser;

internal static class Program
{
    private static int Main()
    {
        var tests = new List<(string Name, Action Test)>
        {
            ("Parse representative SystemTrace performance line", ParseRepresentativeSystemTracePerformanceLine),
            ("Parse real SystemTrace fixture preserving slash-containing values", ParseRealSystemTraceFixturePreservingSlashContainingValues)
        };

        foreach (var (name, test) in tests)
        {
            try
            {
                test();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL {name}");
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        return 0;
    }

    private static void ParseRepresentativeSystemTracePerformanceLine()
    {
        const string line =
            "2026-07-08T14:31:22.1234567+04:00 | Performance | 7 | DataExecutor | QueryFinished | " +
            "engine.dal.dataexecutor | DataExecutor | ExecuteDataSet | support.user | " +
            "SessionID = 11111111-1111-1111-1111-111111111111 | " +
            "ClientRequestTranID = client-tran-42 | OperationID= operation-99 | ThreadID= 12 | " +
            "TotalElapsed = 1.234567| LastLogElapsed = .123456| " +
            "Message: Finished query | Data: ParameterCount=2 | " +
            "SQLID = sql-17 /ExecutionTime = 1.25 /RowsAffected = 4 /" +
            "ConnectionString = Server=db01;Database=AppDb;User Id=app;Password=secret /" +
            "SqlStatement = SELECT * FROM Asset WHERE AssetID = @AssetID";

        var result = SystemTraceLineParser.Parse(line);

        AssertEqual(new DateTimeOffset(2026, 7, 8, 14, 31, 22, TimeSpan.FromHours(4)).AddTicks(1234567), result.Timestamp, "Timestamp");
        AssertEqual("Performance", result.LogType, "LogType");
        AssertEqual(7, result.Severity, "Severity");
        AssertEqual("DataExecutor", result.Module, "Module");
        AssertEqual("QueryFinished", result.Group, "Group");
        AssertEqual("engine.dal.dataexecutor", result.SourceNamespace, "SourceNamespace");
        AssertEqual("DataExecutor", result.SourceSenderType, "SourceSenderType");
        AssertEqual("ExecuteDataSet", result.SourceMethodName, "SourceMethodName");
        AssertEqual("support.user", result.UserName, "UserName");
        AssertEqual("11111111-1111-1111-1111-111111111111", result.SessionId, "SessionId");
        AssertEqual("client-tran-42", result.ClientRequestTranId, "ClientRequestTranId");
        AssertEqual("operation-99", result.OperationId, "OperationId");
        AssertEqual(12, result.ThreadId, "ThreadId");
        AssertEqual(1.234567, result.TotalElapsedSeconds, "TotalElapsedSeconds");
        AssertEqual(0.123456, result.LastLogElapsedSeconds, "LastLogElapsedSeconds");
        AssertEqual("Finished query", result.Message, "Message");
        AssertEqual("ParameterCount=2", result.Data, "Data");
        AssertEqual("sql-17", result.TraceInfo["SQLID"], "SQLID");
        AssertEqual("1.25", result.TraceInfo["ExecutionTime"], "ExecutionTime");
        AssertEqual("4", result.TraceInfo["RowsAffected"], "RowsAffected");
        AssertEqual("Server=db01;Database=AppDb;User Id=app;Password=secret", result.TraceInfo["ConnectionString"], "ConnectionString");
        AssertEqual("SELECT * FROM Asset WHERE AssetID = @AssetID", result.TraceInfo["SqlStatement"], "SqlStatement");
    }

    private static void ParseRealSystemTraceFixturePreservingSlashContainingValues()
    {
        var fixturePath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "tests",
            "ComponentTests",
            "InstrumentationTests",
            "SystemTrace_2026051914.log");

        if (!File.Exists(fixturePath))
        {
            throw new FileNotFoundException("Real SystemTrace fixture was not found.", fixturePath);
        }

        var events = File.ReadLines(fixturePath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(SystemTraceLineParser.Parse)
            .ToList();

        AssertEqual(957, events.Count, "Parsed event count");

        var sqlEvent = events.Single(item =>
            item.LogType == "Performance" &&
            item.Module == "DataExecutor" &&
            item.Group == "QueryFinished" &&
            item.TraceInfo.TryGetValue("SQLID", out var sqlId) &&
            sqlId == "235e2e10-180f-40fb-a986-53711cda1302");

        AssertEqual(new DateTimeOffset(2026, 5, 19, 14, 1, 35, TimeSpan.FromHours(4)).AddTicks(1804449), sqlEvent.Timestamp, "Real Timestamp");
        AssertEqual("b6ded718-412f-4d49-8108-ddb79b3f1e70", sqlEvent.SessionId, "Real SessionId");
        AssertEqual("76bc194e-ea86-4731-ae41-034824d35b75", sqlEvent.OperationId, "Real OperationId");
        AssertStartsWith("SELECT Counters.CACHESIZE", sqlEvent.Message, "Real SQL message prefix");
        AssertEqual("DataParameterCollection=()", sqlEvent.Data, "Real Data");
        AssertEqual("5d2d0a55-5641-4880-88c9-37c281322b52", sqlEvent.TraceInfo["TransactID"], "Real TransactID");
        AssertEqual(".004851", sqlEvent.TraceInfo["ExecutionTime"], "Real ExecutionTime");
        AssertEqual("8", sqlEvent.TraceInfo["RowsAffected"], "Real RowsAffected");
        AssertEqual("Table Table : 8 rows / 11 columns", sqlEvent.TraceInfo["DataResult"], "Real DataResult");
        AssertEqual(
            "workstation id=192.168.50.156\\DEVSQL2019;MultiSubnetFailover=False;packet size=4096;user id=data;data source=192.168.50.156\\DEVSQL2019;persist security info=True;initial catalog=S4ASSETS_BASE_DEV_MQ_V2;TrustServerCertificate=true;",
            sqlEvent.TraceInfo["ConnectionString"],
            "Real ConnectionString");
    }

    private static void AssertEqual<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'.");
        }
    }

    private static void AssertStartsWith(string expectedPrefix, string? actual, string name)
    {
        if (actual == null || !actual.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{name}: expected prefix '{expectedPrefix}', actual '{actual}'.");
        }
    }

    private static void AssertEqual(double expected, double actual, string name)
    {
        if (Math.Abs(expected - actual) > 0.000001)
        {
            throw new InvalidOperationException($"{name}: expected '{expected}', actual '{actual}'.");
        }
    }
}
