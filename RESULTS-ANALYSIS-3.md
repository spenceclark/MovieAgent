# MovieAgent — sweep v3

**22 models, 23 questions, n=2, 1,012 recorded runs.** Output-format contract **1.3**, and a changed
system prompt.

> **Final-score update:** the recorded v3 runs were regraded with
> `deterministic-substring-v4`. An explicitly labelled decline now earns credit only after its
> reachable evidence path has been completed. This removes 14 unsupported refusal passes across
> seven models. The leaderboard below, the generated reports and the `sweep v3` workbook tab are
> updated. Historical v2→v3 aggregate comparisons later in this document retain their original
> grading provenance and should not be quoted as final v4 comparisons unless v2 is also regraded.

> This supersedes [RESULTS-ANALYSIS-2.md](RESULTS-ANALYSIS-2.md), which supersedes
> [RESULTS-ANALYSIS.md](RESULTS-ANALYSIS.md). Both are kept: v2 is the reference for what the
> harness measured before the tool-call budget existed, and this document repeatedly compares
> against it.

**v3 is not comparable with v2 on any axis.** v2 changed the tool-output contract, which made it
incomparable on refusal alone. v3 also changes the system prompt, which sits in front of every
token the model reads. The prompt hash is recorded on every run so the two are never silently
pooled.

---

## What changed, and why

Three deliberate changes, all following from one defect found after v2 was published.

**1. A tool-call budget of 15 replaces the iteration cap as the work budget.** v2 bounded runs at
10 iterations. A model calling one tool per turn therefore got 9 calls; a model batching several
into one turn got as many as it liked. That is not a small difference — measured across the whole
v2 corpus, **9 runs passed only because they batched past a budget that bound everyone else**, and
four of those were a single model on the two questions its headline numbers came from.

Batching is a real capability and the fix is not to forbid it. A budget on *calls* leaves the
advantage intact — a batching model still finishes in fewer turns, and calls-per-iteration still
measures it — while removing the part that was spurious: not having to be efficient. Repeats count
against the budget, because the model is told on every blocked repeat not to issue it again, and
two models spend around 40% of their calls ignoring that.

**2. `MaxIterations` raised 10 → 20.** Necessary, not cosmetic: at 10 a serial model could never
reach 15 calls and still have a turn left to answer, so the budget would have bound only the
batchers — the same inequality wearing a different label. Iterations are now a runaway guard.
Misconfiguring this is a startup error rather than a silent skew.

**3. The prompt no longer says "call one tool at a time".** It said that while
`MeanCallsPerIteration` was reported as a batching metric, so the metric measured disobedience.
Worse, disobedience paid: the models that ignored the instruction were the ones escaping the cap.
The line now permits several independent lookups in one turn and keeps the dependency discipline
that the harness exists to measure — wait for a value rather than guessing it.

The budget is **not stated in the prompt**. Telling a model how many calls it has hands it a
ready-made reason to decline, and refusal is a headline axis. The first it hears of the budget is
an `ERROR:` on the call after it runs out, which closes off retrying without presenting giving up
as the expected response. The run continues; the model still gets to answer with what it has.

**Two eval-set corrections** landed at the same time. `hop5-title-2025-renter` was labelled hop 5
but its `requires_tools` has only four groups — the year filter is reasoning, not a tool call — so
it was half of a two-question hop-5 bucket that was really four-hop data. It is now hop 4, and
`hop5-customer-country` was written to replace it: five sequential calls with no short-circuit,
zero fan-out, verified end to end against the database.

---

## v3 leaderboard

Strict = correct **and**, where the question requires traversal, having reached every required tool.
Explicit decline questions must complete their reachable evidence path; surface-relative declines
caused by an unavailable tool remain exempt.
Read this column. `Correct` is substring-based and will pass an answer that never navigated — see
[the worked example](#the-gap-between-correct-and-strict) below, which is the top local model
getting a question visibly wrong and being marked right.

| # | Model | Strict | Strict % | Answers | Declines | Over-ref | Calls/run |
|---|---|---|---|---|---|---|---|
| 1 | gpt-5.4 | 43/44 | **97.7** | 35/36 | 8/8 | 0 | 3.39 |
| 2 | qwen3.5:9b | 42/44 | **95.5** | 36/36 | 8/8 | 0 | 4.45 |
| 3= | gpt-5.5 | 38/44 | 86.4 | 32/36 | 6/8 | 4 | 3.32 |
| 3= | qwen3.5:4b | 38/44 | 86.4 | 30/36 | 8/8 | 2 | 5.36 |
| 5= | gpt-5.6-luna | 37/44 | 84.1 | 31/36 | 6/8 | 4 | 3.23 |
| 5= | gpt-5.6-terra | 37/44 | 84.1 | 30/36 | 7/8 | 6 | 3.05 |
| 7 | gpt-5.6-sol | 36/44 | 81.8 | 32/36 | 4/8 | 4 | 3.34 |
| 8 | qwen3.5:2b-q4_K_M | 34/44 | 77.3 | 28/36 | 6/8 | 2 | 8.05 |
| 9 | qwen3:4b-instruct | 33/44 | 75.0 | 27/36 | 6/8 | 6 | 2.86 |
| 10 | gpt-4o-mini | 32/44 | 72.7 | 27/36 | 5/8 | 7 | 3.39 |
| 11 | gpt-4o | 31/44 | 70.5 | 26/36 | 5/8 | 8 | 2.84 |
| 12 | gemma4:e4b | 28/44 | 63.6 | 24/36 | 4/8 | 8 | 2.36 |
| 13 | gemma4:e2b | 25/44 | 56.8 | 22/36 | 3/8 | 8 | 2.27 |
| 14 | qwen3.5:2b | 22/42 | 52.4 | 20/36 | 2/6 | 6 | 5.76 |
| 15 | qwen2.5:7b | 23/44 | 52.3 | 19/36 | 6/8 | 12 | 2.75 |
| 16 | ministral-3 | 20/43 | 46.5 | 17/35 | 3/8 | 10 | 2.49 |
| 17 | qwen2.5:3b | 11/44 | 25.0 | 6/36 | 5/8 | 9 | 3.75 |
| 18 | mistral-nemo:12b | 6/44 | 13.6 | 0/36 | 6/8 | 11 | 1.16 |
| 19 | hermes3:8b | 4/44 | 9.1 | 7/36 | 2/8 | 5 | 1.93 |
| 20 | llama3.1 | 3/44 | 6.8 | 6/36 | 0/8 | 11 | 2.84 |
| 21 | llama3.2 | 2/44 | 4.5 | 0/36 | 2/8 | 4 | 1.00 |
| 22 | qwen2.5:1.5b | 0/44 | 0.0 | 2/36 | 0/8 | 2 | 0.34 |

**Not re-run:** deepseek-r1:8b, phi4-mini, command-r7b, granite3.3:8b, mistral. All five made
literally zero tool calls in v1, and neither a call budget nor permission to batch can move a model
that never calls a tool.

**`qwen3.5:2b` is scored on 42 runs and `ministral-3` on 43**, not 44 — see
[reproducible server errors](#two-reproducible-500s) below. Read rates, not raw counts.

---

## The headline: the batching advantage was real, and one model proves it

Under the pre-v4 grading used for the original sweep comparison, pooled strict was
**57.0% → 57.3%**, with 7 models improving, 6 unchanged and 9 worse on the 21 common questions.
That comparison is retained as audit history, not as a final v4 statistic: v2 has not been
regraded with the evidence-path rule. The narrower conclusion still holds independently:
**the batching loophole was not badly distorting most of the leaderboard.** Most models never
used it.

Where it mattered, it mattered enormously, and `qwen3.5:2b-q4_K_M` is the case. It was v2's
headline mover — the 1.8GB model that scored 75% on near-miss recovery, an axis where gpt-4o scores
zero. Given the same 15 calls as everyone else:

| `qwen3.5:2b-q4_K_M` near-miss | v2 | v3 |
|---|---|---|
| `nearmiss-actor-film-count` | **pass** in 3 calls | fail in 5 |
| `nearmiss-film-language` | **pass** in 4 calls | fail in 14 |
| `nearmiss-film-rate` | fail (cap, 10) | **pass** in 15 |
| `nearmiss-word-order` | **pass** in 11 calls | fail (cap, 15) |
| **strict** | **6/8 — 75%** | **2/8 — 25%** |

Its calls per run rose 5.62 → 8.05 and it exhausted the budget in 12 of 44 runs, more than every
other model combined. **The v2 figure of 75% cannot be quoted.** It was bought with a call
allowance no other model had.

The mechanism is not the simple one, though, and the middle two rows are why. This is not a model
being starved — it is a model *given more room and using it worse*. On `nearmiss-film-language` it
went from solving the question in 4 calls to failing it in 14. On `nearmiss-actor-film-count`, 3
calls to 5, pass to fail. Only `nearmiss-film-rate` fits "it needed more budget".

That is the same effect the thinking-on experiments turned up before this sweep: **extra headroom
makes some models wander rather than converge.** Near-miss is the family that punishes it most,
because recovery rewards one good retry, not ten bad ones. So q4_K_M's overall strict score *rose*
(71.4% → 76.2%) while the axis that made it interesting collapsed.

---

## Near-miss is still the discriminator, and the GPT-4o line still scores 0%

Every model reaches near-total accuracy on plain chains. The families that separate them are
recovery and fan-out. On **strict**, near-miss:

| model | near-miss strict | chain strict |
|---|---|---|
| gpt-5.4 | **8/8 — 100%** | 21/22 |
| qwen3.5:9b | 6/8 — 75% | **22/22** |
| qwen3.5:4b | 6/8 — 75% | 20/22 |
| gpt-5.5 / 5.6-sol / 5.6-luna | 4/8 — 50% | 22/22 |
| gpt-5.6-terra | 2/8 — 25% | 22/22 |
| qwen3:4b-instruct | 3/8 — 38% | 20/22 |
| gpt-4o-mini | 1/8 — 12% | **22/22** |
| **gpt-4o** | **0/8 — 0%** | **22/22** |

**gpt-4o and gpt-4o-mini are perfect on chains and score 0% and 12% on near-miss.** That finding has
now survived three sweeps, two tool-output contracts and a system-prompt rewrite without moving.
Two 4B-class local models beat both of them on it.

The rubric qualifier belongs at the point of use: near-miss credit requires **retrying the search
with a variant and finding the row**, so a model that correctly reports the miss and stops scores
zero here. Both GPT-4o models do exactly that, cleanly, without fabricating. On a rubric that
rewarded honest misses they would not be at zero. On this one — where the question is whether a
model keeps going when a chain breaks — they do not do the thing at all.

What v3 removes is the *other* half of v2's most quotable pair. "A 1.8GB model does something a
frontier model cannot" is no longer supported: that model is at 25% once its call budget matches
everyone else's. The honest version is **qwen3.5:9b and qwen3.5:4b at 75%** — still local, still
small, still beating gpt-4o by a distance, but not the 1.8GB headline.

---

## The gap between `correct` and `strict`

`qwen3.5:9b` scores **8/8 on near-miss by `correct` and 6/8 by `strict`.** The two runs that differ
are the same question twice, and they are the clearest illustration in the corpus of why the strict
column exists.

On *"What is the rental duration, in days, of the film PHANTOM WARDROBE?"* (expected **6**) it
searched, recovered from the NO ROWS, found `WARDROBE PHANTOM` — then never called `get_film`, so
it never saw `rental_duration` at all. It went to inventory and rentals instead, computed date
arithmetic across six rentals for 2,700 characters, and concluded:

> **The answer is likely 3 days**, as that's a standard rental period commonly used by such systems.

That is wrong, it says so itself, and it was graded **correct** — because the numeric matcher
extracts every number in the answer and somewhere in the working it had written *"June 17 → June 23
= ~6 days"*.

Tightening the matcher is not the fix. Requiring the *last* number in an answer to be the expected
one flags 24 of 230 numeric passes across the v2 corpus, nearly all legitimate — "…has 7 actors
credited in it. The actor IDs are 21, 23, 62, 108, 137, 169, 197" is a correct answer that happens
to end in an id list. The discriminator that works is the one already recorded:
`navigation_complete`. Publish strict.

The same weakness inflates `llama3.1`, which shows 75% near-miss on `correct` against **0/22 on
chains** — 38% once navigation is required, and still the least trustworthy number in the table.

---

## Hop depth

| hops | answered | navigated |
|---|---|---|
| 2 | 162/308 (53%) | 172/308 (56%) |
| 3 | 145/220 (66%) | 145/220 (66%) |
| 4 | 92/176 (52%) | 93/176 (53%) |
| 5 | 58/87 (67%) | 58/87 (67%) |

Depth still does not predict difficulty — hop 5 outscores hop 2. v1's concern 1 stands and is now
better evidenced: the buckets are confounded by question *type*, not chain *length*. The hop-2
bucket contains the near-miss questions; the hop-5 bucket contains two clean chains. Fixing the
mislabelled hop-5 question and adding a real one did not change this, because the confound was
never about the labels.

---

## Harness findings from this sweep

**Two reproducible 500s.** `qwen3.5:2b` on `unreachable-total-film-count` and `ministral-3` on
`fanout-store-cities` returned Ollama 500s on *every* attempt — four and two occurrences across
re-runs. Not transport blips. The qwen3.5:2b case is diagnosed: it looped `search_film` for **18
iterations and 74,278 input tokens** before the server gave up, which is a context overflow made
possible by raising `MaxIterations` to 20. At 10 it never accumulated enough context to fail this
way. `ministral-3` died at 4 iterations and 10,648 tokens right after a turn with two parallel
`get_store` calls, which is a different fault and undiagnosed.

**This makes the errored-run exclusion load-bearing, and a judgement call.** Errored runs now leave
the denominator, on the reasoning that a transport failure says nothing about a model's tool use.
For `ministral-3` that is clearly right. For `qwen3.5:2b` it is arguable in the other direction: a
model that loops until it overflows its own context has *failed*, and excluding it flatters the
model. The numbers both ways:

- excluded, as recorded: **22/42 = 52.4%**
- counted as failures: **22/44 = 50.0%**

Recorded the first way, flagged here rather than settled silently.

**A metric bug, found and fixed mid-sweep.** The errored-run count was computed *after* errored
runs were filtered out, so it could only ever report zero. Two re-runs printed `errors 0` while
their denominators silently sat at 42 and 43. Fixed; the sheet's `Errored` column is derived from
the JSONL, not from that counter.

**Refusals past the budget were inflating everything.** Caught by the pilot, before the sweep. One
`qwen2.5:3b` turn emitted **123 tool calls** — repeating an unsubstituted template,
`get_address({"address_id":"{{result.address_id}}"})` — until it hit its output-token limit. All
111 over-budget refusals were being counted in `tool_call_count` (inflating its calls-per-run from
3.75 to 6.27) and each received the full budget message, putting ~3,000 tokens of identical text
into the next turn's context. Refusals are now recorded with `over_budget: true`, excluded from the
count, and only the first in a turn carries the full text.

---

## The decline instruction is not carrying the refusal numbers

The prompt tells the model that *"Declining when the data is not reachable is a correct answer"* —
scored guidance, sitting on the axis the results lead with. A fair objection is that the refusal
figures therefore measure instruction-*following* rather than any disposition to decline. Three
models were re-run with that one sentence removed and nothing else changed, which gives the prompt
a different hash and keeps the two populations separable in the recorded data.

| model | arm | strict | correct declines | over-refusals | answers |
|---|---|---|---|---|---|
| qwen3.5:9b | with | 42/44 | **8/8** | 0 | 36/36 |
| qwen3.5:9b | **without** | 42/44 | **8/8** | 0 | 36/36 |
| qwen3.5:2b-q4_K_M | with | 34/44 | **6/8** | 2 | 28/36 |
| qwen3.5:2b-q4_K_M | **without** | 30/44 | **6/8** | 0 | 24/36 |
| gpt-4o-mini | with | 32/44 | **5/8** | 7 | 27/36 |
| gpt-4o-mini | **without** | 33/44 | **5/8** | 6 | 28/36 |

**Correct declines are identical in all three models.** Over-refusals do not rise; they fall
slightly or hold. The sentence is not what produces the refusal behaviour, so the refusal numbers
can be reported as measuring refusal rather than compliance.

The qwen3.5:9b row is the strongest form of that result, because the sentence is not inert — only
**2 of its 46 runs are bit-identical** across the two arms. Removing it rewrote nearly every
trajectory in the sweep and changed **not one outcome**: same strict score, same declines, same
zero over-refusals, same 36/36 answers.

The exception is not a refusal effect. `qwen3.5:2b-q4_K_M` lost four *answers* (28 → 24) with its
declines unchanged, and the flipped runs are the same instability it shows everywhere in this
sweep: `hop2-film-cost` went from 2 calls to hitting the budget at 15, while `nearmiss-film-language`
went the other way, 14 calls to 5, and started passing. It is a model whose trajectories are
extremely sensitive to any perturbation of the prompt — which is a finding about that model, not
about the sentence.

---

## Concerns

**Closed by v3:**

- *The batching loophole.* Budget is on calls, equal for everyone, and the prompt no longer
  penalises compliance.
- *The batching metric measured disobedience.* The instruction it contradicted is gone.
- *Mixed denominators.* Local and hosted both run n=2, so raw counts are comparable across the two
  groups for the first time.
- *hop-5 was half four-hop data.* Relabelled, and a genuine five-hop question added. Every
  answerable question's `expected_hops` now equals its number of tool groups.
- *Tool schemas were unversioned.* Every run records `tool_schema_sha256`, so a regrade can no
  longer describe a schema the model never saw.
- *The decline instruction is scored guidance* — **measured, and it is not.** See above.

**Still live:**

- **1** — hop depth is confounded by question type. Worse in v3, not better: hop 5 now outscores
  hop 2 and hop 4.
- **2** — substring grading passes unnavigated answers. Now with a worked example on the second-best
  model in the sweep.
- **3** — the refusal classifier still reads conditional and planning language as refusal.
- **7** — near-miss scoring encodes a judgement that persistence is correct. Stated at the point of
  use, above.

**New:**

- **Hosted models are non-deterministic and now run at n=2.** In v2 at n=3, 1–3 questions per model
  flipped correctness between repeats and gpt-4o spread two runs across three (16/15/14). At n=2 a
  single flip moves a hosted model 2.3pp and cannot be distinguished from a real difference. Every
  hosted delta below about 5pp in this document should be read as noise. Local models remain
  bit-identical across repeats.
- **Verbosity is rewarded by substring grading, and local models are far more verbose.** Mean answer
  length runs from 71 characters (gpt-5.6-sol, 0% over 250) to 381 (qwen3.5:2b, 44% over 250). A
  longer answer gives the matcher more surface to hit. The false positive above is exactly this
  failure mode, and the bias runs in the local models' favour on the answer axis.
- **v3 changed three things at once.** Budget, iteration guard and prompt all moved together, so no
  individual v2→v3 delta can be attributed to any one of them. `qwen3.5:4b` gaining 14.3pp and
  `qwen3.5:9b` going 6/8 → 8/8 on declines are real, but *why* is not established by this sweep.
- **Context exhaustion is now reachable, and it silently truncates the final answer.** Raising
  `MaxIterations` to 20 lets a conversation grow until the model's context window, not the harness,
  ends the turn. Twelve turns in this sweep finish on `length`; **eight of them are the context
  ceiling** — input + output summing to exactly 8192 — rather than the 2500 output cap. All eight
  are `ambiguous-sumo-2025-renter`, which is the unscored exhibit, so **no reported figure is
  affected**. It came close, though, and the failure mode is nasty in a specific way: the run does
  not error, it returns a sentence that stops mid-clause, and the decline questions are the ones
  with the longest conversations, so it lands on the refusal axis first. A side run of a 9B
  fine-tune at a 4k window made the point unmissable — six truncated turns, every one summing to
  exactly 4096, and *every one* of its four failed declines was a refusal cut off before the
  classifier could see it (*"Based on my search, there is"*, with 7 tokens of headroom left).
  **`num_ctx` is a run variable this harness does not record**, which it should, and a turn ending
  on `length` short of `MaxOutputTokens` should be flagged rather than graded as an answer.
- **The reproducible 500s are a chat-template failure, not a capacity one, and the harness cannot
  see why.** The Ollama server log gives the cause the harness never captured:
  `Jinja Exception: No user query found in messages`, raised by the model's own chat template
  *before* inference. It is not context pressure — `ministral-3` fails at a 2,841-token prompt with
  an 8k window. The one thing the three failing turns share is an assistant message carrying **both
  text and tool calls**, which is the shape the template then has to render. The harness records
  only `Response status code does not indicate success: 500` and discards the response body, which
  is why this needed a server log to diagnose at all; capturing the body on a failed request is the
  fix, and it is a small one.
