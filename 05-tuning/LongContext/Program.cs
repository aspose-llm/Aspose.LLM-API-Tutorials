using Aspose.LLM;
using Aspose.LLM.Tutorials.Common;

// -----------------------------------------------------------------------------
// Long-context tuning — stuff a sizeable document into one prompt and ask
// pointed questions about it.
//
// Three things have to line up:
//   1. The model. Every GGUF bakes a maximum trained context length into
//      its metadata. Asking for more than that produces degenerate output
//      silently. Qwen 2 0.5B (used here) supports 32k natively, so raising
//      ContextParameters.ContextSize from the preset default of 4096 up to
//      16384 is safe.
//   2. The compute. A bigger context means bigger KV cache and more
//      per-token work. On CPU the prefill cost grows roughly O(n²).
//   3. The question. Long contexts reward *pointed* questions ("what is the
//      deadline for X") over broad ones ("summarise everything"). The
//      tutorial demonstrates both flavours so you can compare.
//
// The document used here is ~1500 words (~2000 tokens) of fictional meeting
// minutes — not huge, but far beyond the default 4k window after counting
// system prompt, chat template, and the question itself. Grow it to see
// where latency becomes uncomfortable on your hardware.
// -----------------------------------------------------------------------------

ConsoleHelpers.Banner("Aspose.LLM — Long Context");

TutorialEnvironment.ApplyLicense();

var preset = new SmallModelPreset();

// 1) Pin an explicit larger context. Qwen 2 0.5B supports 32k; 16k is
//    ample for this demo and keeps the KV cache under a gigabyte on CPU.
preset.ContextParameters.ContextSize = 16384;

// 2) Let the model say enough to quote facts back. Keep it modest so the
//    tutorial finishes quickly; raise for full-document summaries.
preset.ChatParameters.MaxTokens = 350;

// 3) Once the window starts filling, prefer truncating the oldest turn
//    rather than the system prompt.
preset.ChatParameters.SystemPrompt =
    "You are a careful analyst. Answer questions using ONLY facts explicit in the document the user pastes. " +
    "When asked a factual question, quote the relevant sentence and then give a one-sentence answer.";

using var api = new AsposeLLMApi(preset);

var sessionId = await api.StartNewChatAsync();

// --- Paste the document once; the rest of the session is Q&A against it ---
ConsoleHelpers.Info(
    $"Context window: {preset.ContextParameters.ContextSize} tokens · " +
    $"document: {Document.Length:N0} chars (~{Document.Length / 4:N0} tokens).\n");

// Silent first turn — the model "absorbs" the document. We still call
// SendMessageToSessionAsync because that's how the SDK gets the bytes
// into the KV cache, but we don't print its reply to keep the output short.
var absorb = await api.SendMessageToSessionAsync(
    sessionId,
    $"Below is an internal project memo. Read it carefully. Reply with only the word OK — then wait for questions.\n\n{Document.Text}");
ConsoleHelpers.Info($"[document ingested; model ack: {absorb.Trim().Substring(0, Math.Min(40, absorb.Trim().Length))}]\n");

// --- Q1: pointed fact retrieval --------------------------------------------
ConsoleHelpers.Section("Q1 — pointed fact (needle-in-haystack)");
const string q1 = "According to the memo, what is the deadline for the cache invalidation work, and who owns it?";
ConsoleHelpers.UserLine(q1);
ConsoleHelpers.AssistantLine(await api.SendMessageToSessionAsync(sessionId, q1));

// --- Q2: cross-reference two facts -----------------------------------------
ConsoleHelpers.Section("Q2 — cross-reference two distant sections");
const string q2 = "What is the budget ceiling for Halyard, and what share of it has the infra team already committed?";
ConsoleHelpers.UserLine(q2);
ConsoleHelpers.AssistantLine(await api.SendMessageToSessionAsync(sessionId, q2));

// --- Q3: broad summary (harder) --------------------------------------------
ConsoleHelpers.Section("Q3 — broad summary");
const string q3 = "List the three biggest risks called out in the memo, each in one sentence.";
ConsoleHelpers.UserLine(q3);
ConsoleHelpers.AssistantLine(await api.SendMessageToSessionAsync(sessionId, q3));

ConsoleHelpers.Info(
    "\nThree questions against the same loaded document, one session. " +
    "Prefill happened once; each question reuses the cached KV.");

// ---------------------------------------------------------------------------
// Fictional meeting minutes. Roughly 1500 words. Deliberately scatter the
// facts across sections so Q1-Q3 have to look in different places.
// ---------------------------------------------------------------------------
public static class Document
{
    public static readonly string Text = @"
Project Halyard — Status Update (Internal Memo, 2026-Q1)

Prepared by Mira Tanaka, Engineering Lead. Distribution: executive staff, platform
and infrastructure leads, finance partner.

Background
----------
Halyard is the working name for our cross-region session-replication initiative,
chartered last October after the P1 outage on 2025-11-14 that dropped 36 minutes
of user-facing traffic in EU-West. The outage post-mortem identified three root
causes: (1) a single-region KV store as the authoritative session backend, (2)
an unbounded retry loop in the gateway that amplified the upstream stall into a
thundering herd, and (3) the absence of circuit-breaker policy on the mobile
SDK. Halyard addresses cause (1) directly and touches (2) indirectly; cause (3)
is owned by the mobile platform team under a separate initiative (codename
'Flotilla') and is not in scope for Halyard.

Scope
-----
The initiative replaces the single-region Redis cluster with an active-active
session store backed by FoundationDB's record layer, fronted by a regional
sharding proxy we are building in-house. Anything behind the session boundary —
user preferences, auth tokens, per-device state — is in scope. Out of scope:
analytics events, marketing attribution cookies, anything the billing pipeline
consumes. The billing team was specifically exempted at the 2025-12-09 review
after flagging compliance sensitivity around their data residency contracts
with the APAC affiliates.

Decisions since last memo
-------------------------
- D-14: we will support three regions at GA: EU-West, US-East, and AP-Southeast.
  AP-Northeast and US-West remain on the post-GA roadmap but are deferred until
  Q4-2026 at the earliest.
- D-15: replication mode defaults to synchronous for auth tokens and async for
  all other session payloads. The split was proposed by Amir Kovacs after
  latency modelling showed the fully-synchronous mode would push p99 login
  time above our 400ms SLO in cross-Atlantic scenarios.
- D-16: the sharding proxy will be written in Rust, not Go. The deciding factor
  was the existing expertise on the platform SRE team (four engineers with
  production Rust experience) rather than any performance gap; in-house
  benchmarks showed both languages comfortably within the proxy's latency
  envelope.
- D-17: cache invalidation strategy. Instead of the TTL-only approach debated
  through January, we will use a write-through invalidation bus keyed by
  session-id-prefix, published on the existing NATS cluster. Owner: Priya
  Sharma (platform/caching). Deadline: 2026-05-15 for the prototype, 2026-07-01
  for production rollout. This decision overrides the tentative guidance in the
  Q4-2025 architecture review.
- D-18: the migration from legacy Redis will happen session-by-session (cold
  migration on expiry), not bulk. The bulk option was briefly reconsidered
  after the 2026-02 sync but rejected again once we quantified the 90-minute
  write lock it would impose on the old cluster.

Action items and owners
-----------------------
1. [infra] Provision FoundationDB clusters in all three regions. Owner: Sofia
   Martinelli. Target: 2026-04-30. Depends on finance approval of the US-East
   capacity reservation (see Budget).
2. [platform] Draft the sharding proxy's wire protocol (TLV over framed TCP).
   Owner: Deepak Iyer. Target: 2026-03-31. A preliminary draft exists as of
   2026-03-18 and is circulating for review.
3. [platform] Implement the proxy's hot-path in Rust to the spec above.
   Owner: Amir Kovacs. Target: first runnable binary by 2026-06-15.
4. [platform] Cache invalidation bus integration. Owner: Priya Sharma. See
   D-17 for dates.
5. [sre] Observability dashboards and alerting baselines. Owner: Lena
   Christensen. Target: live 2 weeks before production rollout, i.e. around
   2026-06-15.
6. [mobile] Coordinate with Flotilla team on circuit-breaker rollout timing.
   Owner: engineering lead to be named by 2026-04-10; currently unassigned.

Budget
------
Halyard has a ceiling of USD 1,400,000 for calendar-2026, inclusive of headcount,
cloud spend, and vendor engagements. This was approved at the 2025-12-18 board
session subject to monthly burn-rate reviews starting April.

As of 2026-03-15, commitments by owning team are:
- Infrastructure: USD 520,000 (FoundationDB licences and regional capacity
  reservations). This represents roughly 37% of the ceiling.
- Platform: USD 110,000 (primarily contractor augmentation for the proxy
  development ramp).
- SRE: USD 40,000 (monitoring-stack scaling and on-call training).
- Unallocated: USD 730,000.

The finance partner, Nadia Rashid, has flagged that the infrastructure
commitment is close to the monthly-review threshold and will likely trigger a
line-item justification request after April's close.

Risks
-----
Risk-1 (High, likelihood high). FoundationDB licence cost for the US-East
region came in 19% above the November 2025 estimate. If the remaining two
regions land similarly, the ceiling will be breached by approximately USD
95,000. Mitigation: renegotiation is in progress; if unsuccessful, D-14 will
be revisited to drop AP-Southeast from the GA set.

Risk-2 (High, likelihood medium). The Rust talent pipeline is thin — Priya's
team has three open reqs for senior engineers and has been advertising since
January with two offers declined. A stretched timeline on D-17 or D-16
deliverables is plausible.

Risk-3 (Medium, likelihood medium). Legacy Redis cluster capacity. Our pre-GA
dual-write phase will briefly push the old cluster to ~80% of commit-log
bandwidth. SRE is watching closely and has a kill-switch procedure documented;
see runbook RB-0421.

Risk-4 (Medium, likelihood low). Compliance change in APAC. Singapore's MAS is
reviewing cross-border data-residency guidance; a tightening could force
AP-Southeast into a fully-local session store and invalidate parts of D-15.
Tracking via legal.

Timeline milestones
-------------------
- 2026-03-31: wire protocol spec frozen.
- 2026-04-30: FoundationDB regional provisioning complete.
- 2026-05-15: cache invalidation prototype (D-17).
- 2026-06-15: proxy first binary; observability baselines live.
- 2026-07-01: cache invalidation in production.
- 2026-08-15: internal-traffic GA (employee dogfooding) across all three regions.
- 2026-10-01: external GA, tied to the product launch in the same quarter.

Open questions for leadership
-----------------------------
- Confirm whether the Flotilla team should absorb action item #6 or whether
  Halyard needs to hire a dedicated coordinator.
- Decision wanted by 2026-04-24 on whether to bring the proxy-in-Rust work
  in-house entirely or keep two contractor seats through Q3. Current headcount
  plan assumes the latter.

End of memo.
";
    public static int Length => Text.Length;
}
