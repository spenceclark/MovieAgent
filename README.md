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
- **One table per tool.** Enforced by [ToolCatalogueValidator](src/MovieAgent.Tools/ToolCatalogueValidator.cs),
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

| Project | Role |
| --- | --- |
| `MovieAgent.Core` | Options, `QueryResult`, `ISqlQueryExecutor`. No provider SDKs. |
| `MovieAgent.Data` | Npgsql implementation. Untyped reads, so Pagila's custom types need no mapping. |
| `MovieAgent.Tools` | Tool descriptors, surfaces, argument binding, the frozen output contract. |
| `MovieAgent.Agent` | The tool loop, the iteration cap, the JSONL recorder. |
| `MovieAgent.Evaluation` | Eval set, grader, eval runner, eval-set verifier. |
| `MovieAgent.Llm` | Chat client construction. The only project referencing OpenAI or OllamaSharp. |
| `MovieAgent.App` | Host and entry points. |

## Commands

```bash
dotnet run --project src/MovieAgent.App -- check
```

| Command | Purpose |
| --- | --- |
| `check` | Database reachable, model reachable, **and model actually emits tool calls**. |
| `verify` | Re-run every eval `reference_sql` and compare with recorded answers. Run before measuring. |
| `tools [surface]` | Print a surface exactly as the model will see it, schemas and SQL included. |
| `ask "<question>"` | One ad-hoc question. Recorded, ungraded. |
| `eval [id-filter]` | Run the eval set, grade it, append to the recorder. |

Everything is overridable by environment variable:

```bash
Agent__ToolSurface=minimal Agent__Repeats=5 Llm__Ollama__Model=qwen3:4b-instruct dotnet run --project src/MovieAgent.App -- eval
```

## Tool surfaces

Three, selected by `Agent:ToolSurface`. Defined in [ToolSurfaces.cs](src/MovieAgent.Tools/ToolSurfaces.cs).

| Surface | Tools | Contents |
| --- | --- | --- |
| `minimal` | 6 | search + read on film, actor, customer only. No junction tools, so relationship questions are genuinely unreachable. |
| `standard` | 24 | Adds lookup tables and junction tools. The fixed control. |
| `standard+desc` | 25 | Standard plus `search_film_description`. |
| `enriched` | 29 | Standard plus the count tools. |

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

Model arguments are untrusted. [ToolArgumentBinder](src/MovieAgent.Tools/ToolArgumentBinder.cs)
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

## Eval sets

Two files under `EvalSet/`: `pagila-v1.json` (13 questions, unchanged) and `pagila-v2.json`
(8 more — near-miss recovery, graded declines, fan-out at depth, truncation handling). Additive
by design; running v1 alone reproduces its numbers exactly.

`EvalSet:Files` selects which to load (comma-separated), default `pagila-v1.json`:

```bash
EvalSet__Files=pagila-v2.json dotnet run --project src/MovieAgent.App -- eval
EvalSet__Files=pagila-v1.json,pagila-v2.json dotnet run --project src/MovieAgent.App -- verify
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
#   M o v i e A g e n t  
 