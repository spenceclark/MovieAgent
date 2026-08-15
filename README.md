# MovieAgent

A research harness for measuring how well a local LLM performs as an agent against a
relational database. Not a product. The output is measurements.

**What is measured:** whether the model can plan and execute a multi-hop dependency chain,
where the arguments to the third tool call are only obtainable from the results of the first
and second. Accuracy by hop depth is the headline. Secondary: tool calls per question, token
cost, correct refusal on unanswerable questions, run-to-run variance.

## The no-shortcuts constraint

Any tool that resolves a relationship server-side destroys the thing being measured. So:

- **No `execute_sql`, no `get_schema`.** These collapse any question to one or two calls.
  Available on exactly one surface, `sql-shortcut`, which exists to *measure* that claim rather
  than assert it — see [The control surface](#the-control-surface). Never on the measured surfaces.
- **One table per tool.** Enforced by [ToolCatalogueValidator](src/MovieAgent.Agent/Tools/ToolCatalogueValidator.cs),
  which rejects any descriptor containing a join or a second `FROM`.
- **Foreign keys are returned raw.** `get_film` returns `language_id = 1`, never `"English"`.
- **No pre-joined views.** Pagila's seven (`film_list`, `actor_info`,
  `nicer_but_slower_film_list`, `sales_by_store`, `sales_by_film_category`, `staff_list`,
  `customer_list`) are on a banned list, along with `information_schema` and `pg_catalog`.
- **No search tool will list a whole table.** Every text parameter has a two-character
  minimum, so there is no list-everything back door.
- **No repository layer.** The abstraction sits at "execute this parameterised SQL, return
  rows as text", with a thin tool layer above.

The validator runs at DI registration over the **whole catalogue**, not just the selected
surface, so a violating descriptor cannot hide in an unused tool and be switched on later.

## Layout

Three projects. Two of them are the interesting ones.

| Project | Role |
| --- | --- |
| **`MovieAgent.Agent`** | **The agent.** The tool loop, the iteration cap, the JSONL recorder, and `Tools/` — the catalogue of tools the model is offered, argument binding, and the frozen output contract. References nothing: the loop can be read start to finish without leaving the project. |
| **`MovieAgent.Evaluation`** | **The measurement.** Eval set, grader, eval runner, regrader, eval-set verifier. |
| `MovieAgent` | Everything incidental: Postgres access (`Data/`), chat client construction (`Llm/`, the only place OpenAI or OllamaSharp appear), the console host and commands (`EntryPoints/`). The executable. |

The dependency direction is `MovieAgent` → `MovieAgent.Evaluation` → `MovieAgent.Agent`, and
`MovieAgent.Agent` depends on no project at all. That is why the small contracts the agent needs
from infrastructure — `ISqlQueryExecutor`, `IWireCapture`, the options classes — live in
`MovieAgent.Agent/Abstractions` and `MovieAgent.Agent/Configuration` and are *implemented* one
layer up, rather than sitting in a shared `Core` project underneath everything.

## Commands

```bash
dotnet run --project src/MovieAgent -- check
```

| Command | Purpose |
| --- | --- |
| `check` | Database reachable, model reachable, **and model actually emits tool calls**. |
| `verify` | Re-run every eval `reference_sql` and compare with recorded answers. Run before measuring. |
| `tools [surface]` | Print a surface exactly as the model will see it, schemas and SQL included. |
| `ask "<question>"` | One ad-hoc question. Recorded, ungraded. |
| `eval [id-filter]` | Run the eval set, grade it, append to the recorder. |
| `sqlguard` | Exercise the `sql-shortcut` read-only guard, including the queries it must refuse. |
| `report <file.jsonl>` | Render a recorded file as a markdown transcript — see below. |

### Reading a run

```bash
dotnet run --project src/MovieAgent -- report runs/runs-20260812-231344.jsonl
```

Writes `runs-20260812-231344.report.md` alongside the input (or to a path given as the second
argument). The JSONL stays the dataset; this is for reading it. Each run gets:

1. **The question and the run's stats** — outcome, model, surface, iterations against the cap,
   tool calls, tokens, elapsed, run id.
2. **Every iteration** — finish reason, tokens, elapsed, content hash, any reasoning text (folded
   into a `<details>` block, since it can run to thousands of characters), what the model said, and
   then **every tool call** with its arguments, its result, rows returned, elapsed, and whether it
   errored, repeated or was blocked.
3. **The grading** — the answer given, pass/fail, what was expected, and the diagnostics.

Fields a surface leaves undefined are omitted, not printed as zero: a `sql-shortcut` report has no
navigation or argument-provenance rows, because with one generic tool those are undefined rather
than zero, and a zero reads as a measured failure.

Long values are clipped with a `… (+N chars)` marker rather than truncated silently. Reports land
in `runs/`, which is gitignored, so they are never committed by accident.

Everything is overridable by environment variable:

```bash
Agent__ToolSurface=minimal Agent__Repeats=5 Llm__Ollama__Model=qwen3:4b-instruct dotnet run --project src/MovieAgent -- eval
```

## Tool surfaces

Three, selected by `Agent:ToolSurface`. Defined in [ToolSurfaces.cs](src/MovieAgent.Agent/Tools/ToolSurfaces.cs).

| Surface | Tools | Contents |
| --- | --- | --- |
| `minimal` | 6 | search + read on film, actor, customer only. No junction tools, so relationship questions are genuinely unreachable. |
| `standard` | 24 | Adds lookup tables and junction tools. The fixed control. |
| `standard+desc` | 25 | Standard plus `search_film_description`. |
| `enriched` | 29 | Standard plus the count tools. |
| `sql-shortcut` | 2 | **The control.** `get_schema` and `execute_sql`. Chain questions only. |

## The control surface

The no-shortcuts constraint above is the premise of the whole harness, and it was asserted rather
than measured. `sql-shortcut` measures it: two tools, `get_schema` and a read-only `execute_sql`,
and the ten linear FK-resolution questions from v1. It separates two failures the main sweep
conflates — **a model that cannot emit a structured tool call at all**, and **a model that can call
one tool but cannot compose a chain across turns**. The first should fail here too, because
`execute_sql` is still a tool call. The second should improve, because one call now suffices.

> **A model scoring higher on `sql-shortcut` is not a better agent.** Text-to-SQL has vastly more
> training data behind it than agentic tool composition. An improvement here shows that *the task
> changed*. Read the delta against the same model's chain score, never the absolute number. The
> `eval` output prints this caveat with every result on this surface, and the surface refuses to
> run any question outside the chain family.

Design notes, all deliberate:

- The control has its own system prompt. The standard prompt says each tool reads one table and
  cannot join, which is false on this surface; `SystemPrompt.ForSurface` selects a SQL-specific
  prompt that describes PostgreSQL, permits joins and tells the model to inspect the schema first.
  The prompt and its hash are recorded on every run.
- The two tools live in `SqlShortcutCatalogue`, **not** `ToolCatalogue`, so `ToolCatalogueValidator`
  keeps proving the main catalogue is join-free and one-table-per-tool. The validator now also
  rejects any non-descriptor tool that turns up in the main catalogue, so the split cannot rot.
- `execute_sql` is guarded twice: [`SqlShortcutGuard`](src/MovieAgent.Agent/Tools/SqlShortcutGuard.cs)
  screens the text (single statement, `SELECT`/`WITH` only, no DDL/DML, no banned objects) and the
  query then runs inside a Postgres `READ ONLY` transaction, which refuses a write regardless of
  what the regex missed. Run `dotnet run --project src/MovieAgent -- sqlguard` to exercise both.
- Database errors come back to the model verbatim and marked retryable — the opposite of the
  descriptor path, where a SQL failure is a harness fault the model cannot fix. Here the model
  wrote the query, so Postgres's complaint is legitimate feedback and acting on it is measured.
- `get_schema` returns a static listing generated from the live database, not a live
  `information_schema` query, because introspection is on the banned list and the ban applies to
  the harness's own SQL too.
- Grading changes shape, not strictness. `requires_tools` names tools that do not exist here, so
  navigation, hop depth and argument provenance are recorded as **null, not zero** — zero reads as
  a failure, and the truth is the question was not asked. Answer correctness is graded identically.

Both variants differ from `standard` by exactly one thing, so any accuracy difference has one
candidate cause rather than two.

`search_film` matches titles by substring; `search_film_description` matches plot descriptions
by Postgres full-text search (`plainto_tsquery`), which ANDs the stemmed terms and ignores word
order and stopwords. That is not a convenience: with a contiguous `ILIKE` the model paraphrased
*"Sumo Wrestler in Ancient Japan"* as `"sumo wrestler ancient Japan"`, lost the stopword, got
`NO ROWS` at hop 1, and the run measured string luck instead of planning.

## Tool output contract

Frozen, versioned (`ToolOutputFormat.Version`, written into every run record). Pipe-delimited,
LF line endings, header row, then a count line.

```
film_id | title
15 | ALIEN CENTER
1 rows
```

- Zero rows returns the literal `NO ROWS`, plus a hint. Never an empty string.
- Truncation is always stated with the true total: `40 rows, showing first 20`.
- Errors state whether retrying can help: `ERROR: ... You may retry this tool with different
  arguments.` versus `ERROR: ... Retrying will not help.`

Model arguments are untrusted. [ToolArgumentBinder](src/MovieAgent.Agent/Tools/ToolArgumentBinder.cs)
re-checks every value against the declared type and range before it reaches Npgsql, regardless
of what the advertised JSON schema said.

## The run record

One JSONL line per run, written to `runs/` (relative to the working directory), flushed on
every write. Each line is self-contained — full question, surface, tool list, model, seed,
temperature, `thinking` (was extended reasoning on for this run — `Agent:Thinking`, mapped to
`ChatOptions.Reasoning`), system prompt and its hash, output-format version — so a mixed file is
still analysable and analysis never has to join against a config file.

`Agent:Thinking` is echoed everywhere a run's configuration is visible, not just the JSONL: the
`check`, `ask`, `eval` and `determinism` console output, and the `eval` startup log line, all
state `thinking on`/`thinking off` next to the model name. `check` in particular builds its own
`ChatOptions` separately from the agent loop (it is a connectivity probe, not a real run), so it
sets `Reasoning` from `AgentOptions.ToReasoningOptions()` explicitly rather than leaving it
unset — otherwise `check` would silently validate a different reasoning configuration than
`eval`/`ask` actually use, defeating the point of running it first.

### Ollama and OpenAI are not sent the same tool output

A second round-trip gap in the same adapter, found the same way. OllamaSharp serialises the whole
`FunctionResultContent` into the tool message body, so a local model reads

```
{"CallId":"call_1","Result":"film_id | title\n11 | ALAMO VIDEOTAPE\n1 rows"}
```

— one line, newlines escaped — where the OpenAI SDK sends the same result as raw text with real
newlines and the id in its own `tool_call_id` field. The frozen output contract above therefore
reached hosted models intact and local models mangled. The assistant message also goes out with
`"id": null`, so `Agent:NormaliseToolCallIds` never reaches the wire on that path.

`Agent:RepairOllamaToolMessages` rewrites the outbound message to match the OpenAI shape. **Off by
default**, because turning it on changes what the model is sent and so invalidates comparison
against everything already recorded. Measured on three models, each against a repair-off control:
gemma4:e4b is unaffected (identical score, per-hop split and calls per run); qwen3.5:4b moves
32/42 → 30/42; qwen3.5:9b moves 38/42 → **40/42**. Both controls reproduced their baselines exactly,
so those are causal. The consistent effect is *less persistence* — fewer calls, more willingness to
decline — which costs 4b some deep chains and costs 9b nothing, because its answers were already at
ceiling. Turn it on for new sweeps.

`Agent:SendReasoningEffort` (default true) suppresses the reasoning-effort parameter entirely.
gpt-4o and gpt-4o-mini reject it with `HTTP 400: Unrecognized request argument supplied:
reasoning_effort` — effort is a reasoning-model parameter and they are not reasoning models. Note
this is **not** a general "reasoning off" switch: on Ollama the same options object becomes `think`,
and an absent `think` is not `think:false`, so a thinking model would fall back to its own default.

### `Agent:Thinking` does not give the model continuity of thought

On Ollama, reasoning is **not carried between iterations** — each turn's `thinking` output is
generated, paid for in tokens, and thrown away before the next request goes out. Verified
against the raw wire traffic (`Agent:CaptureWireTraffic=true`), not inferred:

- Ollama's response for a turn does include a populated `message.thinking` field, and
  OllamaSharp correctly maps it into a `Microsoft.Extensions.AI.TextReasoningContent` — this
  harness's own `messages` list genuinely contains it going into the next iteration.
- The *next* request's re-sent assistant message carries no `thinking` field and no trace of
  that reasoning text anywhere in the request body — confirmed by diffing the actual JSON, not
  by inspecting types.
- This isn't a protocol ceiling: `OllamaSharp.Models.Chat.Message.Thinking` is a settable
  property on the same type used to build outbound history, so the wire format has no evident
  objection to carrying it. The mapping simply doesn't do it on the way out (OllamaSharp 5.4.30,
  Microsoft.Extensions.AI 10.8.3). Reads correctly, never gets replayed.

So with `Agent:Thinking` on, the model re-derives its plan from tool-call history alone every
turn — the "let me reconsider" you see in `assistant_text` is that re-derivation happening in
the open, not evidence of continuity. This harness cannot fix it without reaching past
`IChatClient` into OllamaSharp-specific types, which would break the one architectural rule
that's held since session one (every provider goes through the same abstraction — see the
`AgentLoop` remarks). So instead it's measured: every iteration's `reasoning_text` is recorded
in full (`RunRecord.Iterations[].ReasoningText`), and `scripts/analyse.py` reports total
reasoning volume generated per run. Read `Agent:Thinking=true` as "single-turn reasoning,
regenerated from scratch every iteration," not as "the model thinks continuously across the
loop" — the two would produce very different numbers and only the first is what you're getting.

Per iteration: input/output tokens, elapsed ms, finish reason, assistant text, and every tool
call with the arguments **exactly as the model sent them** and the exact text returned.

Token counts sit at iteration level, not per tool call, because the provider reports usage per
model turn. Attributing tokens to individual calls would be a fabrication.

Outcomes are `Answered`, `IterationCapReached`, `Errored`. Cap hits are data, not errors.

`finish_reason` is **not trustworthy on the Ollama path** — OllamaSharp reports `stop` even for
turns that made tool calls. Do not use it to detect truncation; compare `total_output_tokens`
against a known cap instead.

### Repeated tool calls

`Agent:BlockRepeatedToolCalls` (default true) intercepts a call byte-identical to one already
made in the run and tells the model what that call returned last time, rather than executing it
again. Every call is tagged `was_repeat` and `blocked` regardless of the setting, so repetition
rate stays measurable either way.

This is a variable, not a fix, and it does not uniformly help. On qwen3.5:4b it rescued
`unanswerable-missing-entity` (cap hit → pass in 6 calls) and broke
`unreachable-total-film-count` (pass in 4 calls → cap hit), because being told to stop repeating
pushed it into exploring more terms instead of concluding. Run it both ways.

### Navigation versus answer

Every grade carries `required_tools`, `required_tools_missing` and `navigation_complete`,
computed from whether the run successfully took each step in the question's `requires_tools`.

`requires_tools` is a list of **steps**, each a list of alternative tools, any one of which
satisfies that step — `[["search_film"], ["get_film_actor_ids", "count_film_actors"]]`. Counting
a film's actors is `get_film_actor_ids` on standard and `count_film_actors` on enriched; with a
flat list the enriched run reached the right answer by the better route and was recorded as
having missed a required tool.
"Reached the right rows but never resolved the last identifier" and "went somewhere else
entirely" both score `correct: false`, and only one is interesting. The eval output prints the
unreached tools inline, e.g. `FAIL hop5-title-2025-renter ... never reached get_customer`.

It is a necessary condition, not a sufficient one — calling `get_customer` proves nothing about
the argument. Read it as a floor on navigation quality.

**NOT FIXED, DOCUMENTED INSTEAD.** `navigation_complete` verifies that every required tool was
*reached*, not that the chain was *correct*. Real example, qwen2.5:3b, `hop4-inventory-store-city`
(`runs-20260812-160025.jsonl`): the run calls `get_inventory_item`, `get_store`, `get_staff`,
`get_address`, `get_city`, `get_country` — every required tool present — and answers "Yerevan,
Armenia." That's wrong: it resolved the store's *manager's own home address* (via
`manager_staff_id` → `get_staff` → that staff member's `address_id`), not the store's address
(`get_store` already returns `address_id` directly). `navigation_complete` is `true` on this run
regardless, because it only checks which tools ran, never which argument each one ran with. Same
asymmetry as the truncation cross-check above: it can falsify a claim of navigation, it cannot
confirm one.

Substring answer matching has the mirror problem: it can pass an answer containing both correct
and incorrect content. `"ADAPTATION HOLES is in English and Italian"` passes on `"Italian"`
whether or not "English" being there too is also wrong for some other question shape. Neither of
these has a general fix attempted here — read the transcript for anything the summary numbers
alone make you want to trust.

### Argument provenance and schema errors

Two more diagnostics, both computed purely from `arguments_raw`, `result_text` and `question` —
regrade-safe by the same rule as everything else on this page. See
[ToolCallDiagnostics.cs](src/MovieAgent.Evaluation/ToolCallDiagnostics.cs) for the exact rules
and their limits; the short version:

- **`fabricated_argument_count` / `fabricated_arguments`.** An argument value is *grounded* if it
  appears in the question or in an earlier tool call's result in the same run (never a
  same-turn sibling call — the model has not seen a sibling's result when it proposes both), and
  *fabricated* otherwise. `call_id_as_argument_count` is a fabricated-argument subset: values
  matching `^call_\d+$`, which means the model read one of this harness's own normalised
  tool-call identifiers out of the conversation and sent it back as though it were data — a
  harness-caused failure mode, worth its own count rather than blending into ordinary
  hallucination. `argument_type_mismatch_count` is the opposite kind of near-miss: the value
  *is* grounded, just sent as the wrong JSON kind for the tool's declared parameter type, e.g.
  `{"film_id":"3"}` where 3 is a real prior film_id but the tool declares an integer.
- **`schema_error_count` / `schema_errors`.** Calls that failed for a wrong parameter name or
  type, classified by matching the *exact* message substrings
  [ToolArgumentBinder](src/MovieAgent.Agent/Tools/ToolArgumentBinder.cs) already produces (`does not
  take '`, `requires the argument '`, `must be a whole number, but got '`), not by re-deriving
  validation logic. Deliberately excludes out-of-range ids and too-short search terms — those
  are the right type and shape, just referring to data that is not there, a data reason rather
  than a schema one.

Two correctness traps, both hit and fixed while building this, both worth knowing about before
trusting the numbers on a new corpus:

- **Row-count lines look like data.** Every successful tool result ends in a line like `"1
  rows"`. Before this was stripped from the grounding search, a fabricated `film_id: 1` would
  read as *grounded* purely because some earlier result happened to have exactly one row —
  confirmed against `hop3-film-language`, where the real film_id is 3 and the model fabricated
  `1`, and the only prior result ended in `"1 rows"`.
- **Error messages echo the model's own bad input back.** `ToolArgumentBinder`'s messages
  include the value that failed (`"...but got '$store_id'"`). Without excluding error results
  from the grounding search, a fabricated value that already failed once looks *grounded* on
  retry — via nothing but its own error message repeating it back. Confirmed against
  `hop3-store-manager-email`: `$store_id` sent twice, correctly Fabricated both times only once
  error text was excluded from the search corpus.

**Known blind spot, not attempted.** Grounding is textual, not semantic — a value appearing
*anywhere* in an earlier result counts, regardless of which column it came from. An id that is a
real `store_id` in one row would ground an argument named `customer_id` using the same number.
Column-aware grounding would need to parse each tool's result shape per call; this deterministic
classifier does not attempt it, the same trade-off already made for substring answer matching
above.

### `EmptyAnswer`

A run can stop calling tools with a blank final message. Before this existed that pooled under
`Answered` alongside every ordinary wrong answer, indistinguishable without opening the
transcript. `RunOutcomeClassifier.Effective` reclassifies `Answered` with a blank
`final_answer` to `EmptyAnswer` — applied live in `AgentLoop` for new runs, and by `regrade` for
old ones, from fields already on the record, no re-running needed. `eval`'s summary and
`analyse.py` both report it as a separate count.

### `mean_calls_per_iteration`

The batching metric: total tool calls divided by iterations that made at least one call (an
iteration that ends the run with zero calls would otherwise drag a per-iteration average toward
zero without saying anything about batching). A model that always asks for one thing at a time
sits at 1.0; `fanout-actor-most-films` batching three `count_actor_films` calls into a single
turn pulls it up. Printed in `eval`'s summary and in `analyse.py`'s per-model breakdown.

## Eval sets

Two files under `EvalSet/`: `pagila-v1.json` (13 questions, unchanged) and `pagila-v2.json`
(8 more — near-miss recovery, graded declines, fan-out at depth, truncation handling). Additive
by design; running v1 alone reproduces its numbers exactly.

`EvalSet:Files` selects which to load (comma-separated), default `pagila-v1.json`:

```bash
EvalSet__Files=pagila-v2.json dotnet run --project src/MovieAgent -- eval
EvalSet__Files=pagila-v1.json,pagila-v2.json dotnet run --project src/MovieAgent -- verify
```

`verify`, `eval`, `regrade` and `determinism` all honour it. Question ids must be unique across
the whole selection — a v1/v2 id collision fails loudly at load rather than silently overriding.

### v1 (13 questions)

[pagila-v1.json](src/MovieAgent.Evaluation/EvalSet/pagila-v1.json) — 13 questions, each with
reference SQL, expected answer, and expected hop depth (2 to 5). Three refusal cases, which are
three genuinely different behaviours:

- `unanswerable-missing-entity` — the film does not exist. Decline on every surface.
- `unreachable-total-film-count` — no path to a total on `minimal` or `standard`; becomes
  answerable on `enriched`. This is the direct measurement of what the counting tool buys.
- `ambiguous-sumo-2025-renter` — **ill-posed, not unanswerable.** 82 films mention a sumo
  wrestler, across 392 copies and 805 rentals in 2025, giving 545 valid answers. Correct
  behaviour is to report the ambiguity, not to pick one.

**Do not seed a deep-chain question from a description.** Pagila's descriptions are
combinatorial templates, so no natural phrase isolates a single film — a single noun matches
30–80 of 1000, and even *"teacher in an abandoned mine shaft"* matches 5 films with 27 renters
between them. Seed deep chains from a title or an identifier, where hop 1 is exact, so the run
measures planning rather than search selectivity.

Reachability is surface-relative. Each question lists `requires_tools`; if the selected surface
lacks any of them, the expected behaviour flips to decline automatically. Running the whole set
on `minimal` therefore turns most questions into refusal cases without editing anything.

## Analysis

```bash
python scripts/analyse.py runs/sweep-n3.jsonl
```

Reports accuracy and navigation per surface and per hop depth, refusal rates, runs that
navigated correctly but still answered wrong, and — the reason n>1 matters — **run-to-run
variance**: question/surface pairs that did not always land the same way despite a fixed seed.

Temperature 0 and a fixed seed do not make this stack deterministic. Until you know that noise
floor, no surface comparison is readable, because a one-question difference between surfaces
may just be the same question flipping. Run n≥3 before believing any surface effect.

**The eval answers are specific to this database.** It is a scaled Pagila variant (999
customers, 1500 staff, 500 stores, films in several categories) and the answers are not
portable to stock Pagila. Run `verify` after any reload.

### v2 (8 more questions)

Closes four gaps v1 couldn't see:

| Category | Questions | Tests |
| --- | --- | --- |
| Near-miss recovery | `nearmiss-film-language`, `nearmiss-film-rate`, `nearmiss-actor-film-count`, `nearmiss-word-order` | A plausible first search fails (NO ROWS); does the model retry rather than declining or hallucinating? Four distinct distortion shapes on purpose — three fail by a wrong/extra word recoverable by shortening the same failed query from one end, the fourth (`nearmiss-word-order`) is a straight word-swap that trimming can't recover from at all (verified: no prefix or suffix of the failed query matches), forcing a search on the individual words instead. If a local model recovers from the first three but not the fourth, that is evidence it can shorten a query but not actually search under a wrong assumption — a materially narrower capability than "near-miss recovery" would suggest from the first three alone. |
| Graded declines | `decline-easy-category` (one tool, one NO ROWS), `decline-hard-director` (film exists, field genuinely absent from the schema — must be recognised after reading the record, not from an empty result) | Whether refusal accuracy holds at a harder difficulty than "entity doesn't exist" |
| Fan-out at depth | `fanout-store-cities` (breadth 2, depth 3 per branch), `fanout-actor-most-films` (breadth 3, depth 2 per branch) | Every v1 3+ hop question was a single linear chain. These require collapsing or comparing several branches, not just following one. |
| Truncation | `truncation-category-count` | `get_category_film_ids` caps at 50; Horror has 142. The frozen output format states the true total on the count line even when truncated — the question is whether the model reads it correctly or answers 50. Grading cross-checks this rather than trusting a bare correct/incorrect score: `ToolOutputFormat.TryParseTruncation` recovers the stated total from every tool call in the run, independent of what the model wrote, and every graded run carries `truncation_seen` / `truncation_stated_total` / `answer_matches_stated_total`. A match isn't proof the model read the line (it could land on the right number some other way), but a mismatch is proof the answer didn't come from there — which a bare pass/fail can't distinguish from a correctly-read one. `scripts/analyse.py` prints a cross-check section for any run that saw truncation, not just this question. |

`expected_hops` follows v1's own convention — the length of the shortest tool-call chain under
the current catalogue — not a literal SQL join count, because a literal join count does not
reproduce v1's own numbers (`hop2-actor-count`'s reference SQL joins two tables and is still
hop2, since `get_film_actor_ids` consumes the `film_id` search already returned with no
intermediate read). This is documented in `pagila-v2.json`'s own notes.

Calibration: gpt-5.4 on `enriched`, **9/9**, all hop depths 100%, 0 over-refusals, 0 repeated
calls, recovered from all four near-miss shapes including the word-order swap. Two of the nine
initially failed on a real grader bug the near-miss questions were designed to surface: the
model wrote *"I couldn't find CASABLANCA NIGHTS... CASABLANCA SUPER is 4.99,"* both declining
and answering correctly in the same breath, and the grader's decline check ran first and never
looked at whether a right answer followed. Fixed by checking the answer's value first and only
falling back to refusal detection on an actual miss — validated by regrading every run recorded
to date before trusting it, twice (once per grader change in this batch): zero spurious flips
either time.
