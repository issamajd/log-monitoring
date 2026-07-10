# SystemTrace Secret Redaction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redact access secrets at the SystemTrace parser boundary while preserving SQL/customer diagnostics and replacing the ignored real-log test dependency with a tracked sanitized fixture.

**Architecture:** First make the existing parser test portable. Then add one internal `SensitiveDataRedactor` for direct secret fields, embedded secret assignments, and authorization values. `SystemTraceLineParser` invokes it after extracting each key/value pair and before assigning any event property; the public parser API and event model stay unchanged.

**Tech Stack:** .NET 8, C#, `System.Text.RegularExpressions`, existing console-based parser test harness, Git.

## Global Constraints

- Use `[REDACTED]` as the replacement marker.
- Preserve SQL text, SQL parameter/customer values, server and database names, user names, timings, and row counts.
- Mask password, pwd, token, access token, refresh token, id token, API key, client secret, and authorization fields case-insensitively while ignoring spaces, hyphens, and underscores in field names.
- Apply redaction before assigning `Message`, `Data`, or `TraceInfo` values.
- Redaction must not make an otherwise parseable line fail.
- Add no package dependencies and do not change the public parser API or event model.
- SQL field extraction and the Windows file-tail service remain out of scope.

---

## File Structure

- Create `src/SystemTraceParser/SensitiveDataRedactor.cs`: secret classification and text redaction.
- Modify `src/SystemTraceParser/SystemTraceLineParser.cs`: invoke redaction before storage.
- Modify `tests/UnitTests/SystemTraceParserTests/Program.cs`: portable-fixture and redaction tests.
- Create `tests/UnitTests/SystemTraceParserTests/Fixtures/system-trace-sample.txt`: sanitized input.
- Modify `tests/UnitTests/SystemTraceParserTests/SystemTraceParserTests.csproj`: copy the fixture to output.

---

### Task 1: Establish A Portable Parser Fixture

**Files:**
- Create: `tests/UnitTests/SystemTraceParserTests/Fixtures/system-trace-sample.txt`
- Modify: `tests/UnitTests/SystemTraceParserTests/SystemTraceParserTests.csproj:11-13`
- Modify: `tests/UnitTests/SystemTraceParserTests/Program.cs:12,72-113`

**Interfaces:**
- Consumes: `SystemTraceLineParser.Parse(string line)`.
- Produces: `AppContext.BaseDirectory/Fixtures/system-trace-sample.txt` at build time.

- [ ] **Step 1: Replace the ignored-fixture test with a failing portable test**

Replace the old test-list entry with:

```csharp
("Parse tracked sanitized SystemTrace fixture", ParseTrackedSanitizedSystemTraceFixture)
```

Replace `ParseRealSystemTraceFixturePreservingSlashContainingValues` with:

```csharp
private static void ParseTrackedSanitizedSystemTraceFixture()
{
    var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "system-trace-sample.txt");
    if (!File.Exists(fixturePath))
    {
        throw new FileNotFoundException("Tracked SystemTrace fixture was not copied to test output.", fixturePath);
    }

    var events = File.ReadLines(fixturePath)
        .Where(line => !string.IsNullOrWhiteSpace(line))
        .Select(SystemTraceLineParser.Parse)
        .ToList();

    AssertEqual(2, events.Count, "Parsed event count");
    var sqlEvent = events.Single(item =>
        item.TraceInfo.TryGetValue("SQLID", out var sqlId) && sqlId == "sample-sql-1");

    AssertEqual("SELECT * FROM Customer WHERE CustomerID = 42", sqlEvent.Message, "SQL message");
    AssertEqual("CustomerID=42;Name='Majdi'", sqlEvent.Data, "SQL parameters");
    AssertEqual(".004851", sqlEvent.TraceInfo["ExecutionTime"], "ExecutionTime");
    AssertEqual("8", sqlEvent.TraceInfo["RowsAffected"], "RowsAffected");
    AssertEqual("Table Table : 8 rows / 11 columns", sqlEvent.TraceInfo["DataResult"], "DataResult");
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
dotnet run --project tests\UnitTests\SystemTraceParserTests\SystemTraceParserTests.csproj --no-restore
```

Expected: exit `1` with `Tracked SystemTrace fixture was not copied to test output.`

- [ ] **Step 3: Add the sanitized fixture**

Create `Fixtures/system-trace-sample.txt` with exactly:

```text
2026-07-08T14:31:22.1234567+04:00 | Performance | 7 | DataExecutor | QueryFinished | engine.dal.dataexecutor | DataExecutor | ExecuteDataSet | support.user | SessionID = 11111111-1111-1111-1111-111111111111 | Message: SELECT * FROM Customer WHERE CustomerID = 42 | Data: CustomerID=42;Name='Majdi' | SQLID = sample-sql-1 /ExecutionTime = .004851 /RowsAffected = 8 /DataResult = Table Table : 8 rows / 11 columns /ConnectionString = Server=db01;Database=SupportDb;User Id=support;Password=fixture-secret
2026-07-08T14:31:23.0000000+04:00 | General | 5 | ApiClient | Request | engine.api | ApiClient | Send | support.user | Message: Calling API with Authorization: Bearer fixture-token | Data: Status=200 | ApiKey = fixture-api-key
```

- [ ] **Step 4: Copy the fixture to test output**

Add after the project-reference item group in `SystemTraceParserTests.csproj`:

```xml
<ItemGroup>
  <None Include="Fixtures\system-trace-sample.txt" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

- [ ] **Step 5: Verify GREEN and absence of the old dependency**

Run the parser test command. Expected: the representative and tracked-fixture tests print `PASS` and exit `0`.

Run:

```powershell
rg -n "SystemTrace_2026051914\.log|tests.*ComponentTests.*InstrumentationTests" tests/UnitTests/SystemTraceParserTests
```

Expected: no matches; `rg` exits `1`.

- [ ] **Step 6: Commit**

```powershell
git add tests/UnitTests/SystemTraceParserTests/Program.cs tests/UnitTests/SystemTraceParserTests/SystemTraceParserTests.csproj tests/UnitTests/SystemTraceParserTests/Fixtures/system-trace-sample.txt
git commit -m "test: add portable SystemTrace fixture"
```

---

### Task 2: Redact Direct Secret Fields

**Files:**
- Create: `src/SystemTraceParser/SensitiveDataRedactor.cs`
- Modify: `src/SystemTraceParser/SystemTraceLineParser.cs:70-113`
- Test: `tests/UnitTests/SystemTraceParserTests/Program.cs`

**Interfaces:**
- Consumes: parsed `string key` and `string value`.
- Produces: `internal static string SensitiveDataRedactor.Redact(string key, string value)`.

- [ ] **Step 1: Register and write the failing test**

Add before the portable-fixture entry:

```csharp
("Redact direct access-secret fields", RedactDirectAccessSecretFields),
```

Add before the portable-fixture method:

```csharp
private static void RedactDirectAccessSecretFields()
{
    const string line =
        "2026-07-08T14:31:22.1234567+04:00 | Security | 5 | ApiClient | Request | " +
        "engine.api | ApiClient | Send | support.user | " +
        "ApiKey = fixture-api-key /AccessToken = fixture-access-token /" +
        "Refresh_Token = fixture-refresh-token /ClientSecret = fixture-client-secret /" +
        "Password = fixture-password /Authorization = Basic fixture-authorization /SQLID = sql-17";

    var result = SystemTraceLineParser.Parse(line);

    AssertEqual("[REDACTED]", result.TraceInfo["ApiKey"], "ApiKey");
    AssertEqual("[REDACTED]", result.TraceInfo["AccessToken"], "AccessToken");
    AssertEqual("[REDACTED]", result.TraceInfo["Refresh_Token"], "RefreshToken");
    AssertEqual("[REDACTED]", result.TraceInfo["ClientSecret"], "ClientSecret");
    AssertEqual("[REDACTED]", result.TraceInfo["Password"], "Password");
    AssertEqual("[REDACTED]", result.TraceInfo["Authorization"], "Authorization");
    AssertEqual("sql-17", result.TraceInfo["SQLID"], "Non-secret field");
}
```

- [ ] **Step 2: Verify RED**

Run the parser test command. Expected: exit `1`; `ApiKey` remains `fixture-api-key`.

- [ ] **Step 3: Add the minimal direct-field redactor**

Create `SensitiveDataRedactor.cs`:

```csharp
namespace LogMonitoring.SystemTraceParser;

internal static class SensitiveDataRedactor
{
    internal const string RedactedValue = "[REDACTED]";

    private static readonly HashSet<string> SensitiveNames = new(StringComparer.Ordinal)
    {
        "password", "pwd", "token", "accesstoken", "refreshtoken",
        "idtoken", "apikey", "clientsecret", "authorization"
    };

    internal static string Redact(string key, string value)
    {
        return SensitiveNames.Contains(NormalizeKey(key)) ? RedactedValue : value;
    }

    private static string NormalizeKey(string key)
    {
        return string.Concat(key.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    }
}
```

In `SystemTraceLineParser.ParseKeyValue`, replace the value declaration with:

```csharp
var key = segment[..separatorIndex].Trim();
var rawValue = segment[(separatorIndex + 1)..].Trim();
var value = SensitiveDataRedactor.Redact(key, rawValue);
```

- [ ] **Step 4: Verify GREEN**

Run the parser test command. Expected: all three registered tests print `PASS` and exit `0`.

- [ ] **Step 5: Commit**

```powershell
git add src/SystemTraceParser/SensitiveDataRedactor.cs src/SystemTraceParser/SystemTraceLineParser.cs tests/UnitTests/SystemTraceParserTests/Program.cs
git commit -m "feat: redact direct SystemTrace secrets"
```

---

### Task 3: Redact Embedded Secrets Without Hiding Diagnostics

**Files:**
- Modify: `src/SystemTraceParser/SensitiveDataRedactor.cs`
- Test: `tests/UnitTests/SystemTraceParserTests/Program.cs`

**Interfaces:**
- Consumes and extends `SensitiveDataRedactor.Redact(string key, string value)` from Task 2.
- Produces selective embedded-assignment, authorization, and bearer-token masking.

- [ ] **Step 1: Register and write the failing selective-redaction test**

Add before the portable-fixture entry:

```csharp
("Redact embedded secrets without changing diagnostics", RedactEmbeddedSecretsWithoutChangingDiagnostics),
```

Add before the portable-fixture method:

```csharp
private static void RedactEmbeddedSecretsWithoutChangingDiagnostics()
{
    const string line =
        "2026-07-08T14:31:22.1234567+04:00 | Performance | 7 | DataExecutor | QueryFinished | " +
        "engine.dal.dataexecutor | DataExecutor | ExecuteDataSet | support.user | " +
        "Message: Calling endpoint with Authorization: Bearer ey.fixture.token | " +
        "Data: Authorization=Basic fixture-basic;CustomerID=42;Name='Majdi' | " +
        "ConnectionString = Server=db01;Database=SupportDb;User Id=support;Password=fixture-secret /" +
        "SqlStatement = SELECT * FROM Customer WHERE Name = 'Majdi' AND CustomerID = 42";

    var result = SystemTraceLineParser.Parse(line);

    AssertEqual("Calling endpoint with Authorization: Bearer [REDACTED]", result.Message, "Bearer token");
    AssertEqual("Authorization=Basic [REDACTED];CustomerID=42;Name='Majdi'", result.Data, "Authorization and customer data");
    AssertEqual(
        "Server=db01;Database=SupportDb;User Id=support;Password=[REDACTED]",
        result.TraceInfo["ConnectionString"],
        "ConnectionString");
    AssertEqual(
        "SELECT * FROM Customer WHERE Name = 'Majdi' AND CustomerID = 42",
        result.TraceInfo["SqlStatement"],
        "SqlStatement");
}
```

Also add these assertions to the end of `ParseTrackedSanitizedSystemTraceFixture`:

```csharp
AssertEqual(
    "Server=db01;Database=SupportDb;User Id=support;Password=[REDACTED]",
    sqlEvent.TraceInfo["ConnectionString"],
    "Fixture ConnectionString");

var apiEvent = events.Single(item => item.Module == "ApiClient");
AssertEqual("Calling API with Authorization: Bearer [REDACTED]", apiEvent.Message, "Fixture bearer token");
AssertEqual("[REDACTED]", apiEvent.TraceInfo["ApiKey"], "Fixture API key");
```

- [ ] **Step 2: Verify RED**

Run the parser test command. Expected: exit `1` because embedded authorization or the connection-string password remains visible.

- [ ] **Step 3: Extend the redactor**

Add `using System.Text.RegularExpressions;` and these fields:

```csharp
private static readonly Regex AuthorizationPattern = new(
    @"(?<prefix>\bauthorization\s*[:=]\s*(?:[A-Za-z][A-Za-z0-9._~-]*\s+)?)(?<secret>[^\s;,|]+)",
    RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

private static readonly Regex EmbeddedSecretAssignmentPattern = new(
    @"(?<prefix>\b(?:password|pwd|token|access[\s_-]*token|refresh[\s_-]*token|id[\s_-]*token|api[\s_-]*key|client[\s_-]*secret)\s*=\s*)(?<secret>""(?:[^""]|"""")*""|'(?:[^']|'')*'|[^;\s|,]+)",
    RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

private static readonly Regex BearerTokenPattern = new(
    @"(?<prefix>\bBearer\s+)(?<secret>[A-Za-z0-9._~+/=-]+)",
    RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
```

Replace `Redact` and add `ReplaceSecret`:

```csharp
internal static string Redact(string key, string value)
{
    if (SensitiveNames.Contains(NormalizeKey(key)))
    {
        return RedactedValue;
    }

    var redacted = ReplaceSecret(AuthorizationPattern, value);
    redacted = ReplaceSecret(EmbeddedSecretAssignmentPattern, redacted);
    return ReplaceSecret(BearerTokenPattern, redacted);
}

private static string ReplaceSecret(Regex pattern, string value)
{
    return pattern.Replace(value, match => match.Groups["prefix"].Value + RedactedValue);
}
```

- [ ] **Step 4: Verify GREEN**

Run the parser test command. Expected: all four tests print `PASS`; SQL/customer/server/database/user assertions stay unchanged.

- [ ] **Step 5: Commit**

```powershell
git add src/SystemTraceParser/SensitiveDataRedactor.cs tests/UnitTests/SystemTraceParserTests/Program.cs
git commit -m "feat: redact embedded SystemTrace secrets"
```

---

### Task 4: Final Verification

**Files:**
- Verify only; no source changes expected.

**Interfaces:**
- Consumes: Tasks 1-3.
- Produces: verification evidence for this parser-hardening increment.

- [ ] **Step 1: Verify from a clean parser-test build**

```powershell
dotnet clean tests\UnitTests\SystemTraceParserTests\SystemTraceParserTests.csproj
dotnet run --project tests\UnitTests\SystemTraceParserTests\SystemTraceParserTests.csproj --no-restore
```

Expected: clean succeeds; four tests print `PASS` and exit `0`.

- [ ] **Step 2: Build the solution**

```powershell
dotnet build LogMonitoring.sln --no-restore
```

Expected: `Build succeeded.` with zero errors. The existing `NU1902` warning for `OpenTelemetry.Api` 1.7.0 in `InstrumentationTests` may remain because dependency remediation is outside this increment.

- [ ] **Step 3: Check repository consistency**

```powershell
git diff --check
git status --short
```

Expected: `git diff --check` exits `0`; status has no uncommitted implementation files.

- [ ] **Step 4: Record the next milestone item**

Report that access-secret masking and portable parser tests are complete. Next: extract SQL operation, table names, normalized duration/rows fields, and danger flags before implementing the Windows file-tail service.
