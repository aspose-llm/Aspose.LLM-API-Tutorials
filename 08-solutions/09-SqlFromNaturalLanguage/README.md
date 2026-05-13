# 09 — SQL from Natural Language

Translate a plain-English (or Russian) question against a known schema
into a SQL query for your dialect. The tool **never executes** the
generated SQL — you read it, decide it's right, run it yourself.

## What it demonstrates

- **Code-fence extraction.** The model produces SQL inside `\`\`\`sql ... \`\`\``;
  a regex pulls out the fence body. Optional `Assumptions:` block after
  the fence captures the model's reasoning.
- **Six-dialect support** — `postgresql` (default), `mysql`, `sqlite`,
  `tsql`, `snowflake`, `bigquery`. The system prompt teaches each
  dialect's idioms (PG `::type`, TSQL `[brackets]`, BigQuery STRUCT).
- **Refuse-on-destructive.** If the question implies DELETE / DROP /
  TRUNCATE / UPDATE without explicitly asking for one, the model
  refuses and proposes a SELECT instead.
- **Multi-question demo mode.** With no question on the command line,
  the tutorial runs three canned questions against the bundled schema
  for an out-of-the-box impression.

## Run it

```bash
cd 08-solutions/09-SqlFromNaturalLanguage
ASPOSE_LLM_LICENSE=/path/to/License.lic dotnet run

# Specific question, default dialect (postgresql):
dotnet run -- "How much revenue did each region book in Q1 2026?"

# Custom schema + MySQL dialect + Assumptions block:
dotnet run -- --schema my-schema.sql --dialect mysql --explain \
    "Top 10 paying customers last quarter"
```

With no `--schema` it uses the bundled
[`assets/sample-schema.sql`](assets/sample-schema.sql) — a small
SaaS-billing model (regions, accounts, users, invoices, subscriptions)
with FK constraints, NOT NULLs, and CHECK clauses to give the model
useful anchors.

## Sample output (illustrative)

```
── Question: How much revenue did each region book in Q1 2026? ──

```sql
SELECT
    r.name                              AS region,
    SUM(i.amount)::numeric(12, 2)       AS total_revenue
FROM invoices i
JOIN accounts a ON a.id = i.account_id
JOIN regions  r ON r.id = a.region_id
WHERE i.paid_at >= DATE '2026-01-01'
  AND i.paid_at <  DATE '2026-04-01'
  AND i.status  = 'paid'
GROUP BY r.name
ORDER BY total_revenue DESC;
```

Assumptions:
  - "Revenue" = sum of `invoices.amount` where status='paid'.
  - "Q1" covers January–March; inclusive of 1 Jan, exclusive of 1 Apr.
  - Region is derived from the account, not the invoice.
```

## Hard rule

This tool **NEVER** runs the SQL it generates. It produces text, you
read it, you run it. If a tutorial like this one ever offered "click
to execute", that would be the line at which "AI assists humans"
becomes "AI fires the cannon" — keep the human in the loop.

## Known limitations

- **Schema introspection is your job.** The tutorial doesn't connect
  to a live database; you provide the DDL as a `.sql` file. (Stretch
  goal: auto-introspection via a connection string.)
- **Hallucinated table / column names.** 7B is mostly faithful, but
  ambiguous schemas (two `status` columns on different tables) confuse
  it. Always read the SQL — that's the whole pedagogical point.
- **Date arithmetic.** Same caveat as #02 (Meeting Notes →
  Action Items): the model occasionally miscomputes day offsets.
  Anchor relative phrases ("last month", "this quarter") explicitly
  in your question or pre-resolve them in your prompt.
- **No EXPLAIN / dry-run.** A sibling tool that reads `EXPLAIN ANALYZE`
  output and warns about full-table scans is a stretch goal.

## Files

- [`Program.cs`](Program.cs) — ~190 lines including comments.
- [`SqlFromNaturalLanguage.csproj`](SqlFromNaturalLanguage.csproj)
- [`assets/sample-schema.sql`](assets/sample-schema.sql) — bundled
  SaaS-billing schema for the no-args demo.
