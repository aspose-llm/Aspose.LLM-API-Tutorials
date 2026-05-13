using System.Text.RegularExpressions;
using Aspose.LLM;
using Aspose.LLM.Abstractions.Parameters.Presets;
using Aspose.LLM.Tutorials.Common;
using static SqlPrompt;

// -----------------------------------------------------------------------------
// SQL from Natural Language — translate a plain-English (or Russian) question
// into a SQL query against a known schema. The model writes the SQL, prints
// it inside a fenced code block; we extract the fence and pretty-print to
// stdout. Optionally an Assumptions block follows.
//
// Hard rule: this tutorial NEVER executes the generated SQL. The user reads
// the query, decides it's right, and runs it themselves against their DB.
// Treating the model as a "co-pilot, not a driver" sidesteps catastrophic
// failure modes (DROP TABLE on a misunderstanding).
//
// CLI:
//   dotnet run -- "How much revenue per region in Q1 2026?"
//   dotnet run -- --schema schema.sql --dialect mysql --explain "..."
//   dotnet run                  # runs a small canned demo if no question
// -----------------------------------------------------------------------------

// ---------- CLI parsing -----------------------------------------------------
string? schemaPath = null;
string dialect = "postgresql";
string? question = null;
bool explain = false;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--schema" when i + 1 < args.Length:  schemaPath = args[++i]; break;
        case "--dialect" when i + 1 < args.Length: dialect = args[++i].ToLowerInvariant(); break;
        case "--explain":                          explain = true; break;
        case "--q" when i + 1 < args.Length:       question = args[++i]; break;
        default: question ??= args[i]; break;
    }
}

if (!ValidDialects.Contains(dialect))
{
    ConsoleHelpers.Fail($"Unknown dialect '{dialect}'. Use: {string.Join(", ", ValidDialects)}.");
    return 1;
}

schemaPath ??= Path.Combine(AppContext.BaseDirectory, "assets", "sample-schema.sql");
if (!File.Exists(schemaPath)) { ConsoleHelpers.Fail($"Schema file not found: {schemaPath}"); return 1; }
var schema = await File.ReadAllTextAsync(schemaPath);

// ---------- Setup -----------------------------------------------------------
ConsoleHelpers.Banner("Aspose.LLM — SQL from Natural Language");
TutorialEnvironment.ApplyLicense();
ConsoleHelpers.Info($"Schema:  {schemaPath} ({schema.Length:N0} chars)");
ConsoleHelpers.Info($"Dialect: {dialect}");
Console.WriteLine();

var preset = new Qwen25Preset();
preset.EngineParameters.ModelCachePath = TutorialEnvironment.ResolveModelCachePath();
preset.EngineParameters.EnableDebugLogging = false;
preset.ContextParameters.ContextSize = 16384;
preset.SamplerParameters.Temperature = 0.1f;
preset.SamplerParameters.Seed = 42;
preset.SamplerParameters.RepetitionPenalty = 1.15f;
preset.ChatParameters.SystemPrompt = SystemPrompt;
preset.ChatParameters.MaxTokens = 800;

using var api = new AsposeLLMApi(preset);

// If no question was passed, run a small canned demo so `dotnet run` always
// produces meaningful output.
var questions = question is not null
    ? new[] { question }
    : new[]
    {
        "How much revenue did each region book in Q1 2026? Order by revenue descending.",
        "List the five accounts that churned most recently — name and churn date.",
        "Count active enterprise accounts by region."
    };

foreach (var q in questions)
{
    ConsoleHelpers.Section($"Question: {q}");

    var userMessage =
        $"SQL dialect: {dialect}\n" +
        (explain ? "Include an Assumptions block after the code fence.\n" : "") +
        $"\nSchema:\n```sql\n{schema}\n```\n\n" +
        $"Question: {q}";

    // One AsposeLLMApi instance, fresh chat session per question so prior
    // queries don't accidentally bias the next one.
    await api.StartNewChatAsync();
    var reply = await api.SendMessageAsync(userMessage);

    var (sql, assumptions) = ExtractSqlAndAssumptions(reply);

    Console.WriteLine();
    if (sql is null)
    {
        ConsoleHelpers.Warn("[no SQL fence found in reply — printing raw]");
        Console.WriteLine(reply);
    }
    else
    {
        Console.WriteLine("```sql");
        Console.WriteLine(sql);
        Console.WriteLine("```");
        if (explain && !string.IsNullOrWhiteSpace(assumptions))
        {
            Console.WriteLine();
            Console.WriteLine("Assumptions:");
            Console.WriteLine(assumptions);
        }
    }
    Console.WriteLine();
}

ConsoleHelpers.Info(
    "Always READ the SQL before running it. The tutorial never executes\n" +
    "anything against a real database; that step is yours.");
return 0;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static (string? Sql, string? Assumptions) ExtractSqlAndAssumptions(string reply)
{
    // Match fenced ```sql ... ``` block. Tolerate ```SQL or just ``` (no lang).
    var fenceMatch = Regex.Match(reply,
        @"```(?:sql|SQL)?\s*\n(?<sql>.*?)\n```",
        RegexOptions.Singleline);
    if (!fenceMatch.Success) return (null, null);

    var sql = fenceMatch.Groups["sql"].Value.TrimEnd();

    // Anything after the closing fence: look for "Assumptions:" / "Notes:".
    var afterFence = reply.Substring(fenceMatch.Index + fenceMatch.Length);
    var asMatch = Regex.Match(afterFence,
        @"(?:Assumptions|Notes|Caveats)\s*:?\s*\n(?<body>.*?)(?:\n\n|$)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase);
    var assumptions = asMatch.Success ? asMatch.Groups["body"].Value.Trim() : null;

    return (sql, assumptions);
}

internal static class SqlPrompt
{
    public static readonly HashSet<string> ValidDialects = new()
    {
        "postgresql", "mysql", "sqlite", "tsql", "snowflake", "bigquery"
    };

    public const string SystemPrompt = """
        You are a SQL translator. The user supplies:
          1. A SQL dialect ("postgresql", "mysql", "sqlite", "tsql",
             "snowflake", or "bigquery").
          2. A schema (CREATE TABLE statements).
          3. A question in plain language (any human language).

        You return ONE SQL query wrapped in a ```sql code fence, optionally
        followed by an "Assumptions:" block when interpretation was needed.

        Rules:
        1. Use ONLY tables and columns that exist in the schema. Never
           invent.
        2. If the question is ambiguous, make the most reasonable
           assumption and state it in an Assumptions block. Do NOT ask
           clarifying questions back — produce a query.
        3. Use the requested dialect's idioms (PG `::type` casts, TSQL
           `[brackets]`, BigQuery STRUCT, etc.). Default to PostgreSQL
           idioms if the dialect line is missing.
        4. Prefer explicit JOINs over comma-style joins. Use explicit
           column lists; avoid `SELECT *` in production-shaped output.
        5. Quote identifiers only when necessary (reserved words, mixed
           case in PG, etc.).
        6. If the user's question requires DELETE / DROP / TRUNCATE /
           UPDATE / INSERT and they did NOT explicitly ask for a
           destructive query, REFUSE: write one sentence saying so,
           after the (omitted) code fence, and propose a SELECT
           equivalent that lets them inspect first.
        7. If the question can't be answered with the given schema, say
           so in one sentence, no code fence.

        Output format:

            ```sql
            <one statement, semicolon-terminated>
            ```

            Assumptions:
            - <bullet>
            - <bullet>

        The Assumptions block is optional and only appears when the user
        passes "Include an Assumptions block" in their message.
        """;
}
