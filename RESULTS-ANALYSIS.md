# MovieAgent sweep v1: validation and analysis

> **The numbers here are superseded. See [RESULTS-ANALYSIS-2.md](RESULTS-ANALYSIS-2.md).**
>
> This analysis found two harness defects that were affecting the results it was analysing —
> concern 9b (local and hosted models were sent different tool-output formats) and the retry hint on
> a terminal condition, which sat directly on the refusal axis. Both are now fixed and 22 of the 27
> models have been re-run under output-format contract 1.2.
>
> This document is kept because it is where those defects were found, and because its method —
> the strict score, the question-family taxonomy, the `sql-shortcut` control, the concerns list —
> carries forward unchanged. **Read it for how the harness was audited; read v2 for what the models
> scored.** Its own figures are v1, at contract 1.1, and are not comparable with v2 on refusal.

Analysis of the model sweep recorded in `LlmMoveiAgentResults.xlsx`, validated against the JSONL in
`runs/`. Every number in the spreadsheet was recomputed from the recorded runs using the same
aggregation as `EvalSummary.From`, so for the original 22 models this is a check of the
transcription, not a re-run.

**gpt-5.6-sol and gpt-5.6-terra were added afterwards** and run here directly, on the same
configuration and graded live at v3. One config difference from the earlier GPT rows: the OpenAI
client now retries 429/5xx with backoff, which changes resilience, not output.

**phi4-mini** was added later still, and **deepseek-r1:8b was re-run** at a higher token cap. Both
used `Repeats=1` and `MaxOutputTokens` 6000, so their run *counts* are out of 21 rather than 42 —
rates are comparable, totals are not.

**`qwen2.5:1.5b` and `mistral` were finally run too**, on the same config as the rest of the main
sweep. They had been carried as "Hard Fail — didn't call tools" with no run file, which is a label
rather than a measurement. Running them confirmed one and refuted the other — see section 3. That
brings the set to **27 models, all 27 with run files**.

All 968 runs were then regraded under grader `deterministic-substring-v3`, which makes two changes:
fabricated arguments are split into invented row ids versus invented search terms, and an answer
truncated by the token cap can no longer be graded as a refusal. **Both are additive: regrading
produced 0 correct/declined flips and 0 outcome reclassifications across the whole corpus.** The
figures below come from the regraded files.

**Eval set**: `pagila-v1` + `pagila-v2`, surface `standard+desc`, 21 scored questions × 2 repeats =
42 runs per model, plus one unscored qualitative exhibit. Seed 42, temperature 0, `MaxOutputTokens`
2500, `MaxIterations` 10, thinking off.

---

## 1. Validation result

**21 of 22 rows matched the logs exactly.** One discrepancy:

| Cell | Column | Was | Should be | Evidence |
|---|---|---|---|---|
| `X4` | `ref /8` (qwen2.5:7b) | 5 | **8** | `runs-20260812-194621.jsonl` records `declined=true` on all 8 decline runs |

Corrected in the sheet, highlighted yellow with a cell comment. Every other value in that row —
and every value in the other 21 rows — matched to the decimal.

A `Notes` column (AJ) is populated for all 27 rows. The sheet also
gains a **`Strict`** column (L, next to `Correct`) and the fabrication column is now split into
**`Fab. ID`** and **`Fab. Term`** (U and V). Backups: `LlmMoveiAgentResults.backup.xlsx` is the
original as received, `LlmMoveiAgentResults.prev-v2.xlsx` the pre-split version.

### The strict score

`Correct` and `NavigationComplete` are graded independently — the first by substring-matching the
final answer, the second by checking which required tools actually ran. Neither implies the other,
so a run can be marked correct while having skipped a required tool. The **strict score** is
`correct AND navigated`, with declines exempt because refusing needs no traversal. It drops passes
the model reached by luck rather than by chaining.

It is now computed by the harness itself as `EvalSummary.NavigatedCorrect`, printed by `eval`, and
carried in the spreadsheet as its own **`Strict`** column next to `Correct`.

Six models were inflated by unnavigated luck:

| Model | Raw | Strict | Lucky passes |
|---|---|---|---|
| llama3.1:8b | 9 | **4** | 5 |
| hermes3:8b | 7 | **4** | 3 |
| ministral-3 | 26 | **24** | 2 |
| qwen2.5:3b | 12 | **10** | 2 |
| qwen3.5:2b-q4_K_M | 22 | **20** | 2 |
| granite3.3:8b | 4 | **3** | 1 |

The mechanism is worth seeing, because it is not subtle. llama3.1 calls `get_film(film_id=1)`
without searching. Film 1 is ACADEMY DINOSAUR, whose language is **English** and whose
`rental_duration` is **6** — which happen to be the expected answers to `nearmiss-film-language`
and `nearmiss-word-order`. It scored four passes on two questions it never navigated. Likewise
qwen2.5:3b guessed `get_film(film_id=123)` on `nearmiss-film-rate`; 123 really is CASABLANCA SUPER
at 4.99.

---

## 2. Winners

**Strict leaderboard** (correct *and* reached every required tool, out of 42):

| # | Model | Strict | Answers /34 | Declines /8 | Over-refusals |
|---|---|---|---|---|---|
| 1 | **gpt-5.4** | **42** | 34 | 8 | 0 |
| 2 | **qwen3.5:9b** | **38** | 34 | 4 | 0 |
| 3= | gpt-5.6-luna | 37 | 31 | 6 | 3 |
| 3= | gpt-5.6-sol | 37 | 30 | 7 | 4 |
| 5 | gpt-5.5 | 36 | 30 | 6 | 4 |
| 6 | gpt-5.6-terra | 34 | 28 | 6 | 6 |
| 7 | **qwen3.5:4b** | **32** | 30 | 2 | 0 |
| 8 | gpt-4o | 31 | 24 | 7 | 8 |
| 9 | qwen3:4b-instruct | 29 | 21 | 8 | 10 |
| 10= | gemma4:e2b | 28 | 20 | 8 | 12 |
| 10= | gpt-4o-mini | 28 | 24 | 4 | 8 |
| 12 | gemma4:e4b | 26 | 22 | 4 | 8 |
| 13= | qwen2.5:7b | 24 | 16 | 8 | 8 |
| 13= | ministral-3 | 24 | 16 | 8 | 10 |

**gpt-5.4 — the only clean sweep.** 42/42 raw and strict, 34/34 answers, 8/8 declines, zero
over-refusals, zero fabricated arguments, zero schema errors, one tool error in 142 calls. It is
the only model that cleared every question family.

**qwen3.5:9b — the result worth writing about.** A 9.65B Q4_K_M model in 6.6GB, running locally,
scored 38/42 strict and **beat GPT-4o (31), GPT-4o-mini (28), GPT-5.5 (36) and all three GPT-5.6
variants (37, 37, 34)**. It answered **every single answerable question correctly — 34/34** —
including 20/20 dependency chains, 8/8 near-miss recovery, 4/4 fan-out and 4/4 at hop 5. All four of
its losses are refusal cases it tried to answer. It never over-refuses once. Only gpt-5.4 finished
above it.

**qwen3.5:4b — the efficiency story.** 3.4GB, and a perfect 20/20 on pure dependency chains, equal
to GPT-4o and gpt-5.4 on that axis.

---

## 3. Losers

Ten of the twenty-seven models scored **at most one correctly-navigated answer**. Their entire
score is refusals or luck:

| Model | Strict | Genuine answers | What actually happened |
|---|---|---|---|
| qwen2.5:1.5b | 0 | 0 /34 | Calls tools rarely (15 in 42 runs) and never chains; both passes are unnavigated guesses |
| deepseek-r1:8b | 1 | 0 /34 | Zero tool calls; describes calls in prose and invents tool names |
| phi4-mini | 1 | 0 /17 | Zero tool calls; emits correct tool-call JSON into the content channel |
| mistral | 2 | 0 /34 | Zero tool calls; writes pseudo-code calls in markdown fences |
| granite3.3:8b | 3 | 0 /34 | Printed tool calls as prose; never used the tool channel |
| llama3.1:8b | 4 | 1 /34 | Spammed fabricated IDs, 58% of calls errored |
| llama3.2:3b | 4 | 0 /34 | One call per run, then stops. Never chains |
| hermes3:8b | 4 | 0 /34 | Leaked the tool-call envelope into the arguments |
| mistral-nemo:12b | 6 | 0 /34 | One call, then answers. Never takes a second hop |
| command-r7b | 8 | 0 /34 | Zero tool calls; refused 33 of 34 answerable questions |

phi4-mini ran at `Repeats=1`, so its denominators are 21 runs / 17 answerable rather than 42 / 34.

**Five of these are hard fails, not low scorers.** All five were wire-checked to rule out a harness
fault, and in all five **25 tool definitions were sent in correct Ollama format** and none were
called:

- **granite3.3:8b** — zero real tool calls in all 42 runs. Emits tool calls as text,
  `<|tool call{"name": "search_film", ...}|>` and fenced JSON blocks, in 17 runs. The model
  described the tools accurately in prose before declining to call any.
- **command-r7b** — zero tool calls in all 42 runs; refuses almost everything instead.
- **deepseek-r1:8b** — zero tool calls in 42 runs at a 2500-token cap *and* in a further 21 runs
  at 6000. See the retest below: the cap was hiding this failure, not causing it.
- **phi4-mini** — zero tool calls in 21 runs, and the narrowest miss of the group. See below.
- **mistral** — zero tool calls in all 42 runs. 31 of them write pseudo-code calls inside markdown
  fences, ` ```search_film(title_contains="alamo videotape")``` `, then continue as though the call
  had run, prefaced with "Assuming…". Both its correct answers are genuine refusals on
  `unreachable-total-film-count`; it genuinely answered 0 of 34.

### The two labels that were never measured

`qwen2.5:1.5b` and `mistral` sat in the sheet as "Hard Fail — didn't call tools" with no run file
behind them. Running them was cheap and it **corrected one of the two**:

| | Label | Reality |
|---|---|---|
| `mistral` | didn't call tools | **Confirmed.** 0 tool calls in 42 runs, wire-checked |
| `qwen2.5:1.5b` | didn't call tools | **Wrong.** 15 tool calls across 42 runs, 11 successful |

qwen2.5:1.5b is not a tool-channel failure. It uses the channel, just rarely — 28 of 42 runs make
no call, and the calls it does make are mostly well-formed (`search_film("AGENT TRUMAN")` returns
the right row). What defeats it is everything after: it does not chain, it invents ids, and in 10
of 42 runs it prints the tool *declaration schema* back as prose —
`{"type": "function", "function": {search…` — echoing the shape it was offered rather than issuing
a call.

Its two "correct" are the most instructive false positive in the corpus. Both are
`nearmiss-film-language`, answered with **zero tool calls**, guessing "English" from priors — and
the expected answer is English, so substring matching passed it. It even appends the non-sequitur
"ERROR: The query returned no results." to its own answer. Strict scoring catches both: **2 raw,
0 strict, 0 of 34 genuinely answered.**

The general lesson is the one this report keeps arriving at: a label is not a measurement. Two
models were carried for the whole sweep on an assertion, and half of it was wrong.

### phi4-mini: the right payload in the wrong channel

Worth separating from the other three, because it is not a model that failed to try. It emits
**structurally correct tool-call JSON**, with real tool names and real parameter names — into the
content channel:

```json
[{"name": "get_film", "arguments": {"film_id": 1}}, {"name": "get_inventory_item", "arguments": {"inventory_id": 2}}]
```

15 of 21 runs contain that shape. Because the payload is right, this is the one case where a rig
fault was genuinely plausible, so it was checked two ways rather than one:

- `/api/show` — phi4-mini **declares the `tools` capability**, and its chat template *does* handle
  tools: it renders them into `<|tool|>…<|/tool|>` and expects calls back wrapped as
  **`<|tool_call|>[…]<|/tool_call|>`**.
- Wire capture — 25 tools sent; response carries **no `tool_calls` field**; content holds the bare
  JSON array with **no `<|tool_call|>` delimiter**.

So the model omits the delimiter its own template requires, Ollama cannot parse the payload, and it
falls through as text. A model failure, but one pair of tags away from working — and the only
failure in this group that a template tweak might plausibly rescue.

Two things make it worse than the bare score suggests. It **hallucinates the results as well as the
calls**, roleplaying whole exchanges with invented rows — inventing the city "Springfield" for
`hop4-inventory-store-city`, inventing actor ids `[1,2,3]` and concluding "3 actors" for
`hop2-actor-count`. Confident fabricated data in exactly the right shape is the worst possible
output for anything downstream that trusts the text. And its text-format calls are frequently
malformed anyway: `[{"name": "get_actor_film_ids", {"arguments": {"actor_id": 1}}}]` puts the
arguments in a second object rather than a key, and one run uses `"parameters"` instead of
`"arguments"`. It also invents tools that do not exist (`search_rental`, `search_inventory_item`).

Its single "correct" is the same planning-monologue false positive as deepseek-r1's, so its true
score is **0**.

**command-r7b is the cautionary tale for the refusal metric.** It scores a perfect 8/8 on refusal
accuracy. It also refuses 33 of the 34 answerable questions, and answers none of them. Its 8/42 is
composed entirely of correctly saying "I can't". Refusal accuracy without the over-refusal
denominator would rank it as one of the best-calibrated models in the sweep.

---

## 4. Surprising

**GPT-4o and GPT-4o-mini score 0/8 on near-miss recovery, while a 4B local model scores 6/8.**
This is the largest capability inversion in the data. The near-miss questions misquote a title
(`CASABLANCA NIGHTS` for the real `CASABLANCA SUPER`); the first search returns NO ROWS and the
tool's own hint says to try a shorter fragment. GPT-4o's entire run:

```
search_film {"title_contains":"CASABLANCA NIGHTS"}  ->  NO ROWS. ... try a shorter fragment
ANSWER: "There is no film titled CASABLANCA NIGHTS in the database."
```

One search, then it stops. qwen3.5:9b instead shortens to `CASABLANCA`, finds the film, reads the
rate, and *says what it substituted*: "there is no film titled CASABLANCA NIGHTS… the closest match
was CASABLANCA SUPER (film_id 123)… rental rate $4.99." Scores: qwen3.5:9b 8/8, qwen3.5:4b 6/8,
gpt-5.4 8/8, gpt-5.5 4/8, **gpt-4o 0/8, gpt-4o-mini 0/8**.

**The smaller Gemma beat the bigger one.** gemma4:e2b 28 strict vs gemma4:e4b 26. e4b gets more
answers right (22 vs 20) but is worse at refusing (4/8 vs 8/8).

**The version ladder is not monotonic, and gpt-5.4 is the outlier at the top.** With all three 5.6
variants measured, every later model scores below it:

| Model | Strict | chain /20 | near-miss /8 | decline /8 | over-ref |
|---|---|---|---|---|---|
| **gpt-5.4** | **42** | 20 | **8** | **8** | **0** |
| gpt-5.6-luna | 37 | 20 | 5 | 6 | 3 |
| gpt-5.6-sol | 37 | 20 | 4 | 7 | 4 |
| gpt-5.5 | 36 | 20 | 4 | 6 | 4 |
| gpt-5.6-terra | 34 | 20 | 2 | 6 | 6 |

All five are **identical where the harness was designed to discriminate** — 20/20 chains, 4/4
fan-out, 2/2 truncation, and perfect at hops 3, 4 and 5 without exception. The entire spread between
42 and 34 comes from two places: near-miss recovery (8 → 2) and refusal calibration (0 → 6
over-refusals). Whatever separates gpt-5.4 from the 5.6 line here, it is not multi-hop tool use.

**Quantisation cost showed up as loop control, not knowledge.** qwen3.5:2b Q8 vs the same model at
Q4_K_M: raw score actually went *up* by 2, but cap hits went 4× worse (4 → 16) and fabricated
arguments 3.7× (24 → 88). The Q4 build called `get_film` 114 times, brute-forcing IDs sequentially.
It knows the same things; it is worse at stopping.

**The GPT models are not reproducible; the local ones very nearly are.** With `seed=42` and
`temperature=0`, the qwen3.5 family produced bit-identical output across both repeats — 0 of 21
questions varied — while gpt-5.4 varied on **9 of 21**. The variation never changed a gpt-5.4
outcome (it still scored 42/42), but "seed + temperature 0" does not mean reproducible against the
hosted API.

An early spot check appeared to contradict the local half of that, so I tested it properly: 10
`eval` invocations of `nearmiss-film-rate` against qwen3.5:9b through the same code path as the
sweep, 20 runs, comparing `content_sha256` for every iteration.

**19 of 20 runs were bit-identical across all five iterations.** One diverged, and the shape of the
divergence is the interesting part:

- Its first four iterations hash **identically** to the other nineteen. Only the fifth — the final
  free-text summary — differs.
- Every tool call in the run is identical: same tools, same arguments, same order. The answer is
  still correct.
- The prose differs by a clause: it drops `Release year: 2009` and rephrases the opening line.
- That one generation took **28,955 ms against a ~2,330 ms median — a 12× outlier.**

`load_duration` was flat at 238–324 ms across every captured exchange in all ten invocations, and
`/api/ps` showed the model resident with a rolling expiry throughout, so **this was not a cold load
or a model reload.** Something perturbed that single generation — GPU contention or a layer spill
are the plausible candidates, and neither is recoverable after the fact. The tell is the latency,
not the load.

So the sweep's determinism figure is sound and needs no hedge: local runs are reproducible, and the
one failure mode observed does not touch tool selection or arguments — it perturbs only free-text
generation, and it announces itself with a large latency outlier. That is worth having as a
detection rule.

**deepseek-r1:8b ignored `Agent:Thinking=false` completely** — reasoning text in 42/42 iterations —
and hit the 2500-token cap mid-thought in 31 of 42 runs, producing 31 empty answers and zero tool
calls. Mean 2298 output tokens against ~100 for every other model, and 26 minutes of wall clock.

### The token cap was hiding the failure, not causing it

The obvious read was that 2500 tokens made it untestable. It was re-run at **6000** to check
(`runs-20260813-162941.jsonl`, 21 questions × 1 repeat):

| | cap 2500 | cap 6000 |
|---|---|---|
| `finish_reason: length` | 31/42 (74%) | **3/21 (14%)** |
| Empty answers | 31 | **3** |
| **Tool calls emitted** | **0** | **0** |
| Correct | 1 | 1 (the same false positive) |
| Mean output tokens | 2,298 | 3,418 |
| Mean sec/run | 37.6 | **56.2** |

The cap was a real constraint and lifting it removed it — truncation fell from 74% to 14%, so most
runs now finish cleanly. **The score did not move, because it still made zero tool calls in 21 of 21
runs.** Every run is exactly one iteration: no tool call means nothing to feed back, so the loop
terminates on the first turn regardless of budget.

What it does instead is write an essay about calling tools — **309,720 characters of reasoning
against 10,186 of answer, a 30:1 ratio** — naming tools that mostly do not exist
(`search_film_by_title` ×3, `get_rental_film_id` ×2, `film_actor_list`; the real `search_film` is
named once across 21 runs). Two runs abandoned tools entirely and wrote raw SQL.

Wire capture at the 6000 cap settles it: **25 tool definitions sent** in correct Ollama format,
`think:false` sent, and the response carries no `tool_calls` field at all — 11,497 characters in
`thinking`, 319 in content, ending
`<tool>search_film_by_title</tool> with argument: ALAMO VIDEOTAPE`. It invents its own pseudo-syntax
in the content channel while the real tool-calling channel goes unused. So this is the same failure
as granite3.3 and command-r7b, and **"untestable at this setting" was wrong** — it is a hard fail.

Its single "correct" is a grader artefact. That run finished with `finish_reason: "stop"` and a
complete sentence, so it is not a truncation. It is a *planning monologue*: the model narrates a
call it never makes — "I am calling `search_films_by_title`…" — and closes with "if it returns no
rows, then the film is not in the database, and I cannot proceed further without guessing". The
refusal classifier reads that conditional as an actual refusal. Its true score is **0**. This is
unfixed; see concern 3.

---

## 5. Patterns

**Chain depth is solved for the top tier; recovery and refusal are not.** Nine models score a
perfect 20/20 on pure dependency chains: qwen3.5:4b, qwen3.5:9b, gpt-4o, gpt-4o-mini, gpt-5.4,
gpt-5.5, and all three gpt-5.6 variants. The metric the harness was built to measure is saturated at
the top, and adding two more frontier models added two more 20/20s. What separates those nine is
entirely near-miss recovery (0/8 to 8/8) and refusal calibration — a spread of 31 to 42 on the total
with **zero** variation on the chain metric.

**There is a hard cliff, not a gradient.** Models either chain or they do not. Scores on the
chain-only metric cluster at 20, 18, 17, 14 — and then fall off to 4, 3, 2, 0. Nothing sits between
14 and 4. The failure mode below the cliff is uniform: one tool call, then an answer.

**Refusal behaviour splits into three populations**, and it is nearly independent of competence:

- *Over-refusers* — command-r7b (33 over-refusals), llama3.1 (16), gemma4:e2b (12),
  mistral-nemo (12), qwen2.5:3b (10), ministral-3 (10), qwen3:4b-instruct (10).
- *Under-refusers* — the whole qwen3.5 family: 0 over-refusals, but 0–4 of 8 declines. They never
  give up, which costs them the refusal questions and produces cap hits instead.
- *Calibrated* — gpt-5.4 alone: 8/8 declines with 0 over-refusals.

**Argument fabrication is the mechanism of failure — but only the id half of it.** Splitting the
metric changes who looks bad:

| Model | Flagged | Invented **ids** | Invented **search terms** | **Schema-enumerated** |
|---|---|---|---|---|
| llama3.1:8b | 145 | **138** | 3 | 0 |
| qwen3.5:2b-q4_K_M | 88 | 30 | 22 | **36** |
| qwen2.5:3b | 78 | **67** | 8 | 0 |
| qwen3.5:9b | 48 | **2** | 14 | **32** |
| llama3.2:3b | 26 | 20 | 6 | 0 |
| qwen3.5:2b | 24 | 8 | 16 | 0 |
| hermes3:8b | 21 | 11 | 0 | 0 |
| qwen2.5:7b | 18 | 8 | 0 | **10** |
| **qwen3.5:4b** | 18 | **0** | **18** | 0 |
| every model scoring ≥26 except the above | 0 | 0 | 0 | 0 |

Three distinct things, and lumping them together made the strong models look reckless.

**Inventing a row id** asserts that a specific record exists. That is the hallucination worth
counting, and llama3.1's 138 are the pure case: `get_film(film_id=1)` and `get_film(film_id="123")`
standing in for a search it never performed.

**Inventing a search term** is how searching works. A model hunting for an entity that turns out not
to exist will try several, and that is correct behaviour. **qwen3.5:4b's 18 are entirely search
terms and zero are ids.**

**Schema-enumerated** is the category this analysis was missing until a run was read closely. The
grounding corpus is the question plus prior tool results, and deliberately excludes the tool
declarations — so a model that reads `"Category identifier, 1 to 16"` off `get_category`'s own
schema and walks 1..16 scores as fabricating all sixteen values, despite every call succeeding.
qwen3.5:9b's `decline-easy-category` is exactly that, and it drops the model from 34 invented ids to
**2 in 42 runs.**

The obvious fix would have been wrong. **68% of all fabricated ids in the corpus (278 of 409) are
inside their advertised range**, and most are single blind guesses — treating in-range as grounded
would have excused llama3.1 entirely. Contiguity is what separates them: only 41 of 311 numeric
fabricated ids form a sweep of four or more consecutive values, and llama3.1 has none. The
threshold of four is a judgement call, documented at
`ArgumentProvenance.SchemaEnumerated`; the corpus shows nothing between four and a full sweep.

Regrading every run under the new classification produced **0 grade flips** and moved exactly three
rows. Note also that flagged ≠ id + term + enumerated for two models. The remainder is arguments
sent on parameter names no tool declares — hermes3's 10 are its leaked
`CallId`/`FunctionName`/`Arguments` envelope, which is also the whole of its schema-error count.

**Distinctive failure signatures**, each visible in the recorded arguments:

- qwen2.5:3b passes literal placeholders as IDs: `"result of previous search_film call"`,
  `"{{result.store_id}}"`. It has learned the *shape* of chaining without the substance.
- hermes3:8b sends the harness's own call envelope back as the argument object:
  `{"Arguments":{"staff_id":1},"CallId":"call_2","FunctionName":"get_staff"}`.
- llama3.1 issues ~4 calls inside a single iteration and then stops — 2.0 iterations per run
  against 3–5 for models that work. It has no read-then-plan loop at all.

**Cleanliness does not imply capability.** ministral-3 made 102 tool calls with zero fabricated
arguments, zero schema errors and zero tool errors — the cleanest caller in the sweep — and still
only reached 24/42, because it stops early rather than because it calls badly.

---

## 6. Concerns about the testing method

Ordered by how much they affect the conclusions.

**1. The hop-depth columns are confounded by question type.** All four near-miss questions and the
truncation question sit at hop 2, so `hop 2 ans` measures search recovery as much as chain depth.
The effect is not marginal — it inverts the headline. **All five GPT-5.x models score 100% at hops
3, 4 and 5 and lose only at hop 2** (gpt-5.4 14/14, luna 11/14, sol 10/14, gpt-5.5 10/14, terra
8/14). Read naively, the by-hop table says accuracy *improves* with depth for every frontier model
tested. What it actually says is that the hop-2 bucket contains a different task.
**Recommendation: report chain-only accuracy by hop, with near-miss, fan-out and truncation broken
out as their own families.** The family table is in the appendix.

**1b. The chain metric is now saturated and no longer discriminates.** Nine of the twenty-two models
with run files score exactly 20/20 on pure dependency chains, including every frontier model tested
and two local ones. Adding gpt-5.6-sol and gpt-5.6-terra added two more perfect scores and zero new
information about chaining. The headline metric has a ceiling, and the sweep has hit it: a spread of
31 to 42 in total score across those nine comes entirely from recovery and refusal. **To keep
measuring chain depth, the set needs hops beyond 5, wider fan-out, or chains with a decoy branch** —
otherwise the interesting differences will keep migrating to the non-chain families.

**2. Substring grading produces false positives, and they are concentrated in the models you would
most want to rank correctly.** 15 of the 470 passes across the sweep were not navigated. The
`navigation_complete` flag catches these, but only because it is checked separately — the headline
`Correct` count does not. granite3.3 has a pass where the model printed a tool call as prose and
then asserted an answer containing "English"; it was graded correct having called nothing.

**3. The refusal classifier still has false positives — on conditional and planning language, not
on truncation.** v3 now refuses to grade a truncated answer as a refusal, which is the right
invariant, but measuring it first showed two things worth recording:

- Under the natural reading — *any* iteration truncated — the guard would have **erased 4 genuine
  over-refusals**, all llama3.1, where an early truncated turn was followed by a complete one. Only
  the **final** iteration can count, because that is the turn that produced the answer.
- Under that correct reading the guard matches **0 runs in the entire corpus**. It is a safety rail
  against a future false positive, not a fix for a present one.

The live false positive is a different shape: deepseek-r1's point comes from a *complete* sentence
containing a conditional — "if it returns no rows, then… I cannot proceed further without guessing"
— inside a plan the model never executed. A refusal classifier working on final-answer text alone
cannot separate "I cannot" from "if X then I cannot". The cheap partial fix is to refuse to grade a
run as declining when it made zero tool calls *and* its answer contains tool-call syntax or
first-person future-tense planning; the honest fix is an LLM judge on the recorded answer.

**4. `MaxOutputTokens` shapes what a reasoning model's failure *looks like*, without changing it.**
At 2500 deepseek-r1 truncated mid-thought in 31 of 42 runs and banked 31 empty answers, which reads
like a harness artefact. At 6000 it finishes cleanly in 18 of 21 and still scores zero, because the
real failure — never emitting a tool call — was never about budget. **Retested rather than assumed**,
which is the point: the original conclusion here ("untestable at this setting rather than proven
bad") was wrong, and only a re-run at a different cap could show that. The residual caution stands
in weaker form — a cap that truncates changes the *shape* of the evidence and makes an empty answer
ambiguous between "ran out of room" and "had nothing to say" — so a reasoning model should be run at
a cap it does not hit before any conclusion is drawn from its output. It costs: 50% more wall clock
per run at 6000 for an identical score.

**5. Small denominators at depth.** Hop 5 is two questions × two repeats = 4 runs, so one run is 25
points. Hop 4 is 6 runs. The deep end of the headline metric — the part the harness exists to
measure — is the least statistically supported part of it. More questions at hops 4 and 5 would buy
more than more models.

**6. Two repeats cannot separate instability from difficulty.** With n=2 a question is 0, 1 or 2.
For the local models repeat 2 was bit-identical throughout the sweep and bought nothing but time;
for the hosted ones n=2 is far too few — gpt-5.4 varied on 9 of 21 questions. The 20-run check above
measured the local divergence rate at 1 in 20, confined to free-text generation and flagged by a 12×
latency outlier. So: **n≥3 for hosted models, n=2 retained for local ones**, and log a warning when
an iteration's `elapsed_ms` is a large multiple of the run median — that catches the one local
failure mode directly instead of paying for repeats to find it.

**7. "Correct behaviour" on near-miss is a design choice, not ground truth.** GPT-4o answering
"there is no film titled CASABLANCA NIGHTS" is defensible — it is literally true, and inventing a
substitution the user did not ask for has its own risks. The eval rewards persistence because the
tool emits a hint telling the model to retry. That is a reasonable rule, but it should be stated as
a rubric decision rather than presented as a capability gap, because it is scoring an interaction
style.

**8. Hard-fail classification was inconsistent, and one of the labels was simply wrong.** For most
of this analysis qwen2.5:1.5b and mistral were carried as "Hard Fail — didn't call tools" with no
run file, while granite3.3 and command-r7b did the same thing — zero tool calls, wire-confirmed —
and were scored, landing mid-table on refusal credit alone. Same behaviour, different treatment.

Both have now been run. mistral's label held; **qwen2.5:1.5b's did not** — it makes 15 tool calls
across 42 runs. The class is now uniform (every model has a run file, all five zero-call models are
scored and marked), but the general point stands and is worth stating plainly: **a model excluded
on an unverified label is a hole in the dataset shaped like a conclusion.** It cost two sweeps of
about a minute each to close, and closing it changed one of the two answers.

**9. Two duplicate full runs exist and are not referenced.** `runs-20260812-214734.jsonl` is a
second complete ministral-3 sweep (identical 26/42 — a useful reproducibility datapoint that
deserves to be in the sheet) and `runs-20260812-224854.jsonl` is the gpt-4o attempt that errored
42/42 under rate limiting. The latter is worth keeping precisely because it is what a rate-limited
run looks like in the data.

**9b. Local and hosted models were not sent the same tool output.** Found by reading a wire
capture, and the most serious methodological defect in the whole harness. The frozen output
contract — pipe-delimited, header row, LF line endings, count line — reached the two providers
differently:

| | OpenAI `/v1/chat/completions` | Ollama `/api/chat` |
|---|---|---|
| tool message keys | `content`, `role`, **`tool_call_id`** | `content`, `role` |
| content | `film_id \| title⏎11 \| ALAMO VIDEOTAPE⏎1 rows` | `{"CallId":"call_1","Result":"…\n…"}` |
| real newlines | **2** | **0** |
| assistant call id | `"id": "call_1"` | `"id": null` |

OllamaSharp serialises the whole `FunctionResultContent` object into the message body, so every
local model read the result table as **one line of escaped JSON** while every hosted model read the
intended three-line table. It also drops the call id from the assistant message entirely, which
means `NormaliseToolCallIds` never reached the wire on that path — the normalised id survives only
inside the content blob.

**Measured rather than assumed.** A repair handler (`Agent:RepairOllamaToolMessages`, off by
default) rewrites the outbound message to match the OpenAI shape exactly. Three models were run in
both arms, each with a repair-off control so the effect could be separated from run-to-run
variance:

| Model | repair off | repair on | control reproduced baseline? | change |
|---|---|---|---|---|
| gemma4:e4b | 26/42 | **26/42** | — | none: identical per-hop, per-refusal, calls/run |
| qwen3.5:4b | 32/42 | **30/42** | yes, exactly | **−2** |
| **qwen3.5:9b** | 38/42 | **40/42** | yes, 0 flips | **+2** |

Calibrated against the noise floor: gpt-4o re-run on its own recorded configuration moved by
**one run** (31 → 30, a single `decline-hard-director` repeat), which is the hosted variance to
judge against. The 4b and 9b arms flipped six runs each. This is well above noise.

**The mechanism is consistent and the net effect is not.** With clean tool output both qwen3.5
models become *less persistent* — fewer calls, more willingness to decline:

- **4b**: answers 30 → 26, declines 2 → 4, calls 208 → 188. Loses both repeats of
  `hop5-title-2025-renter` and `nearmiss-film-language`, gains `unreachable-total-film-count`.
- **9b**: answers 34 → **34** *(already at ceiling — nothing to lose)*, declines 4 → 6, calls
  202 → 172. `decline-easy-category` alone drops from 17 calls to 10.

Same shift; the sign of the net differs only by whether the model had answer headroom to give up.
gemma4:e4b, which is far less persistent to begin with, did not move at all.

What this does and does not undermine. **Local-versus-local comparisons are sound** — every Ollama
run in the corpus had identical treatment. The **local-versus-hosted** comparison carries a real
caveat: they were reading different formats. It does not weaken the headline; it strengthens it.
The local models were reading the *worse* format, and repairing it moves qwen3.5:9b from 38 to
**40/42**, closer to gpt-5.4's 42 rather than further away. **Any future sweep should turn it on**;
it is off by default only so the existing corpus stays interpretable.

**9c. One configuration was never recorded at all.** The gpt-4o and gpt-4o-mini rows were produced
by hand-patching out the reasoning-effort parameter, because those models reject it
(`HTTP 400: Unrecognized request argument supplied: reasoning_effort` — effort is a reasoning-model
parameter and they are not reasoning models). That made two rows unreproducible from the repository
and left nothing in their JSONL to say why. It is now `Agent:SendReasoningEffort`, recorded per run
as `send_reasoning_effort`, and the gpt-4o re-run above used it. The general lesson is the same one
as the call-id flag: **a run variable that is not recorded is a hole in the dataset**, and the way
it surfaces is a re-run that inexplicably fails.

**10. GPU offload varied between models.** mistral-nemo:12b ran at 0.73 and ministral at 0.92,
everything else at 1.0. That affects latency comparisons but not correctness.

---

## Appendix: accuracy by question family

Every model, split by what the question actually tests. `chain` is the pure multi-hop dependency
metric — the thing the harness was built for.

| Model | Total /42 | chain /20 | near-miss /8 | fan-out /4 | trunc /2 | decline /8 | over-ref /34 |
|---|---|---|---|---|---|---|---|
| gpt-5.4 | 42 | 20 | 8 | 4 | 2 | 8 | 0 |
| qwen3.5:9b | 38 | 20 | 8 | 4 | 2 | 4 | 0 |
| gpt-5.6-luna | 37 | 20 | 5 | 4 | 2 | 6 | 3 |
| gpt-5.6-sol | 37 | 20 | 4 | 4 | 2 | 7 | 4 |
| gpt-5.5 | 36 | 20 | 4 | 4 | 2 | 6 | 4 |
| gpt-5.6-terra | 34 | 20 | 2 | 4 | 2 | 6 | 6 |
| qwen3.5:4b | 32 | 20 | 6 | 2 | 2 | 2 | 0 |
| gpt-4o | 31 | 20 | 0 | 2 | 2 | 7 | 8 |
| qwen3:4b-instruct | 29 | 17 | 0 | 2 | 2 | 8 | 10 |
| gemma4:e2b | 28 | 18 | 0 | 0 | 2 | 8 | 12 |
| gpt-4o-mini | 28 | 20 | 0 | 2 | 2 | 4 | 8 |
| ministral-3 | 26 | 14 | 0 | 2 | 2 | 8 | 10 |
| gemma4:e4b | 26 | 18 | 0 | 2 | 2 | 4 | 8 |
| qwen2.5:7b | 24 | 14 | 1 | 1 | 0 | 8 | 8 |
| qwen3.5:2b-q4_K_M | 22 | 18 | 2 | 2 | 0 | 0 | 0 |
| qwen3.5:2b | 20 | 14 | 0 | 0 | 2 | 4 | 4 |
| qwen2.5:3b | 12 | 4 | 2 | 0 | 2 | 4 | 10 |
| llama3.1:8b | 9 | 2 | 4 | 0 | 0 | 3 | 16 |
| command-r7b | 8 | 0 | 0 | 0 | 0 | 8 | 33 |
| hermes3:8b | 7 | 3 | 0 | 0 | 0 | 4 | 5 |
| mistral-nemo:12b | 6 | 0 | 0 | 0 | 0 | 6 | 12 |
| llama3.2:3b | 4 | 0 | 0 | 0 | 0 | 4 | 4 |
| granite3.3:8b | 4 | 0 | 1 | 0 | 0 | 3 | 9 |
| deepseek-r1:8b | 1 | 0 | 0 | 0 | 0 | 1 | 3 |
| phi4-mini * | 1 | 0 | 0 | 0 | 0 | 1 | 1 |

`*` phi4-mini ran at `Repeats=1`, so its denominators are half the rest: 21 runs, chain /10,
near-miss /4, fan-out /2, trunc /1, decline /4, over-ref /17.

Families: **chain** = the 10 v1 linear FK-resolution questions (hop2–hop5). **near-miss** = 4
questions whose first search is designed to return NO ROWS. **fan-out** = 2 questions with genuine
breadth. **trunc** = the 142-row truncation question. **decline** = 4 questions that should be
refused on this surface. Counts are runs, not questions (2 repeats each).

---

## 7. The control: does the no-shortcuts constraint cause the failures?

The premise of this whole harness is that a generic SQL surface destroys what is being measured —
one join replaces a five-hop chain. That was asserted, never tested. This is the test.

**Surface**: `sql-shortcut`, two tools. `get_schema` returns a static column/PK/FK listing;
`execute_sql` runs one read-only SELECT and renders it through the same output contract and the
same 20-row cap as the standard surface, so output shape is not a second variable.

**Questions**: the 10 linear FK-resolution questions only (hop2–hop5, v1). Everything else is
excluded in code, not by convention — near-miss recovery is about a search tool that does not
exist here, and the decline questions are labelled unreachable *relative to a tool surface*, so
`unreachable-total-film-count` becomes a one-line `count(*)` and grading a refusal as correct would
mark the model wrong for being right. Selecting anything outside the chain family throws before the
first model call.

**Models**: the two that made zero structured tool calls, the two that made one call and stopped,
one mid-tier, and one control already at ceiling. Same config as the main sweep.

> **This is not a capability ranking.** Text-to-SQL has vastly more training data behind it than
> agentic tool composition. A model scoring higher here shows the task changed, not that the model
> is a better agent. The delta is the finding.

### Results

| Model | chain (main) /20 | sql-shortcut /20 | Δ | q /10 | calls/run | iters/run | SQL calls | SQL errors | **read schema first** |
|---|---|---|---|---|---|---|---|---|---|
| granite3.3:8b | 0 | 0 | — | 0 | 0.00 | 1.00 | 0 | 0 | — (no tool call at all, 20/20 runs) |
| command-r7b | 0 | 0 | — | 0 | 0.00 | 1.00 | 0 | 0 | — (no tool call at all, 20/20 runs) |
| llama3.2 | 0 | 0 | — | 0 | 1.00 | 2.00 | 20 | **20 (100%)** | **0/20** |
| mistral-nemo:12b | 0 | **2** | **+2** | 1 | 1.00 | 2.00 | 14 | 10 (71%) | **0/14** |
| gemma4:e4b | 18 | 18 | — | 9 | 3.40 | 4.40 | 48 | 18 (38%) | **20/20** |
| qwen3.5:4b | 20 | 20 | — | 10 | 4.20 | 5.20 | 64 | 4 (6%) | **20/20** |

### What it shows

**The constraint is not what is causing the failures.** This is the headline, and it is the
opposite of the comfortable result. Handing the four failing models a generic SQL tool moved the
aggregate by two runs out of eighty. If the no-shortcuts surface were unfairly hard — if it were
manufacturing failure through awkwardness rather than measuring it — removing it should have lifted
them. It did not.

**Prediction 1 confirmed: a shortcut is still a tool call.** granite3.3 and command-r7b made zero
tool calls here, exactly as in 42 runs each on the main surface. Their failure has nothing to do
with chain composition; they never reach the point where composition matters. Nothing about the
tool surface can fix a model that will not use the tool channel.

**Prediction 2 refuted: "one call then stop" did not become "one call is enough".** This was
expected to improve sharply, and it barely moved. Look at the loop shape rather than the score:
llama3.2 and mistral-nemo both sit at **exactly 1.00 tool calls and 2.00 iterations per run** — on
this surface *and* on the main one. One call, then answer, regardless of what the call returned.
The surface changed; the shape of the failure did not. mistral-nemo's +2 is the two runs where a
single blind guess happened to be right.

**The behavioural column is the sharpest result in the whole report.** Whether a model read the
schema before writing SQL separates the two groups perfectly, with no overlap:

```
never read it   llama3.2      0/20 runs    →  100% of queries failed  →  0/20 correct
                mistral-nemo  0/14 runs    →   71% of queries failed  →  2/20 correct
always read it  gemma4:e4b   20/20 runs    →   38% of queries failed  → 18/20 correct
                qwen3.5:4b   20/20 runs    →    6% of queries failed  → 20/20 correct
```

The models that skip it invent plural table names that do not exist — `films`, `actors`,
`customers` — and columns on the wrong table. That is the main sweep's fabricated-argument
instinct, unchanged, in a new costume: assert a name, do not check it. The models that read it
first write joins that work. **The failing behaviour is not "cannot chain", it is "will not
check".**

**Errors are only useful to a model that reads them.** gemma4 hit 18 SQL errors in 48 calls and
still scored 18/20, because it iterates: read the error, fix the query. llama3.2 hit 20 errors in
20 calls and scored 0, because it never issues a second query. The database's feedback is identical
in both cases; only one model is listening.

**The control held.** qwen3.5:4b 20 → 20 and gemma4:e4b 18 → 18. Worth noting gemma4's flat score
hides a moved failure: it fails `hop5-title-2025-renter` on the main surface and
`hop4-inventory-store-city` on this one. Same total, different question.

### What this does and does not license

It does license the premise. The main sweep's chain scores (0, 0, 0, 0, 18, 20) and the shortcut
scores (0, 0, 0, 2, 18, 20) agree almost exactly, which means the chain metric is tracking tool-use
competence rather than an artefact of a deliberately awkward surface.

It does not license reading the shortcut column as a capability score, for the reason in the
caveat above — and note that the models which *did* well here were already doing well on chains, so
this data cannot separate "good at SQL" from "good at tool use" for them. The only clean
observation is the negative one: **the shortcut rescued nobody.**

Two caveats on the run itself. The brief named five models and listed six; all six were run. And
`llama3.2:3b` is not a pulled Ollama tag — the first attempt returned 404 on every run, recorded as
20 `Errored` runs, and was re-run against `llama3.2`, which is the tag the main sweep used. The
errored file was discarded, not reported.
