# MovieAgent sweep v2: results after two harness fixes

The v1 analysis is in [RESULTS-ANALYSIS.md](RESULTS-ANALYSIS.md) and is still the record of how the
harness was audited — the taxonomy, the strict score, the `sql-shortcut` control and the concerns
list all originate there and are not repeated. **Two of those concerns turned out to be defects
affecting the numbers.** Both are now fixed, 22 of the 27 models re-run, and this is the corrected
result.

Workbook: tab **`sweep v2 (fixed)`**. Sheet1 is preserved unchanged as the v1 record.

---

## What changed

**1. Local and hosted models were not reading the same tool output** (v1 concern 9b). OllamaSharp
serialised the whole content object into the tool message, so every local model read
`{"CallId":"call_1","Result":"film_id | title\n11 | ALAMO VIDEOTAPE\n1 rows"}` — one line, newlines
escaped — where every hosted model read the intended three-line table. Fixed by
`Agent:RepairOllamaToolMessages`, which rewrites the outbound message to the shape the OpenAI SDK
already sent.

**2. A terminal condition carried a retry hint.** The too-short-search-term rejection ended
*"You may retry this tool with different arguments"* — appended to a message whose own body said the
tool would never list every row. Models did what they were told. qwen3.5:9b spent all ten iterations
of `unreachable-total-film-count` on eight successive substring guesses. The rejection is not
terminal in general — a longer term is valid, and near-miss recovery depends on it — so it now uses a
third category between retryable and terminal:

> *"…must be at least 2 characters, and no value of any length will make this tool list every row —
> it only finds rows matching the text you give it. You may retry with a longer, more specific search
> term. If what you need is every row, or a count of them, that is not reachable with the tools you
> have — say so rather than guessing terms."*

Output-format contract bumped **1.1 → 1.2**. v1 and v2 are not comparable on the refusal axis.

**Configuration otherwise identical** to v1: `standard+desc`, seed 42, temperature 0, thinking off,
`MaxOutputTokens` 2500, `MaxIterations` 10, v1+v2 eval sets. One deliberate change: **hosted models
run at n=3** (63 runs) per v1 concern 6, local stay at n=2 (42). Compare **Strict %**, not counts.

## The fix landed where it was aimed

Measured on the **15 local models at matched n=2** — the same models, the same denominators, before
and after. A pooled figure over all 22 would not support the claim: hosted models went from n=2 to
n=3 and five models dropped out, so the pool is reweighted toward GPT and any movement is partly
composition rather than effect.

| | v1 | v2 | |
|---|---|---|---|
| **correct declines** | 67/120 (55.8%) | **72/120 (60.0%)** | **+4.2pp** |
| over-refusals | 102/510 (20.0%) | 104/510 (20.4%) | +0.4pp |
| answers correct | 218/510 (42.7%) | 224/510 (43.9%) | +1.2pp |

**+4.2pp on refusal with over-refusal effectively flat.** That is the result that matters: models got
better at declining without becoming readier to decline in general. Had over-refusal risen with it,
the fix would merely have been pushing everything toward "no".

The per-model picture is more informative than any aggregate, and less flattering. Across all 22
models the decline rate **improved for 10, was unchanged for 8, and got worse for 4**. Five models
moved by a full **+25pp** — qwen2.5:3b, qwen3.5:4b, qwen3.5:9b, qwen3.5:2b-q4_K_M and llama3.2 — and
over-refusal for every one of those five stayed within ±3pp. Four went backwards: qwen2.5:7b,
qwen3.5:2b and ministral-3 by 25pp, gpt-4o by 12.5pp.

---

## v2 leaderboard

Strict = correct **and**, where the question requires traversal, having reached every required tool.

| # | Model | Strict | Strict % | Answers | Declines | Over-ref |
|---|---|---|---|---|---|---|
| 1 | **gpt-5.4** | 63/63 | **100.0** | 51/51 | 12/12 | 0 |
| 2 | **qwen3.5:9b** | 40/42 | **95.2** | 34/34 | 6/8 | 0 |
| 3 | gpt-5.6-sol | 56/63 | 88.9 | 45/51 | 11/12 | 6 |
| 4 | gpt-5.5 | 55/63 | 87.3 | 45/51 | 10/12 | 6 |
| 5 | gpt-5.6-luna | 54/63 | 85.7 | 44/51 | 10/12 | 7 |
| 6 | gpt-5.6-terra | 52/63 | 82.5 | 42/51 | 10/12 | 9 |
| 7 | qwen3:4b-instruct | 32/42 | 76.2 | 24/34 | 8/8 | 8 |
| 8= | qwen3.5:4b | 30/42 | 71.4 | 26/34 | 4/8 | 0 |
| 8= | **qwen3.5:2b-q4_K_M** | 30/42 | **71.4** | 28/34 | 2/8 | 0 |
| 8= | gpt-4o | 45/63 | 71.4 | 36/51 | 9/12 | 12 |
| 11 | gemma4:e2b | 28/42 | 66.7 | 20/34 | 8/8 | 12 |
| 12 | gpt-4o-mini | 40/63 | 63.5 | 34/51 | 6/12 | 12 |
| 13 | gemma4:e4b | 26/42 | 61.9 | 22/34 | 4/8 | 8 |
| 14 | ministral-3 | 22/42 | 52.4 | 16/34 | 6/8 | 8 |
| 15= | qwen2.5:7b | 20/42 | 47.6 | 14/34 | 6/8 | 12 |
| 15= | qwen3.5:2b | 20/42 | 47.6 | 18/34 | 2/8 | 6 |
| 17 | qwen2.5:3b | 11/42 | 26.2 | 5/34 | 6/8 | 9 |
| 18= | llama3.2 | 6/42 | 14.3 | 0/34 | 6/8 | 4 |
| 18= | mistral-nemo:12b | 6/42 | 14.3 | 0/34 | 6/8 | 11 |
| 18= | hermes3:8b | 6/42 | 14.3 | 2/34 | 4/8 | 5 |
| 21 | llama3.1 | 4/42 | 9.5 | 0/34 | 4/8 | 17 |
| 22 | qwen2.5:1.5b | 0/42 | 0.0 | 0/34 | 0/8 | 4 |

**Not re-run** (carried in the tab at format 1.1): mistral, granite3.3:8b, command-r7b,
deepseek-r1:8b, phi4-mini. All five made **literally zero tool calls** across every recorded run —
44/44/44/44/22 — so neither fix can reach them. They remain hard fails.

---

## The headline got stronger

**qwen3.5:9b: 90.5% → 95.2%.** It now beats every GPT model except gpt-5.4 — including all three
5.6 variants — on a 6.6GB local model. It answers **100% of answerable questions** with **zero**
over-refusals, and is perfect on all four question families except refusal, where it takes 6/8.

That is the opposite of what a sceptic would predict from a harness fix. The local models had been
reading the *worse* tool format the whole time; repairing it moved the best of them up, not down.

**qwen3.5:2b-q4_K_M is the biggest mover: +23.8pp** (47.6 → 71.4), now level with gpt-4o. A 1.8GB
Q4 model. Its family deltas show why it was suffering: near-miss **+50pp**, fan-out **+50pp**,
truncation **+100pp**. Its cap hits halved (16 → 8) and calls per run fell 7.43 → 5.62. It had been
drowning in the mangled output, and reading a legible table freed most of a budget it was spending
on confusion.

### The accidental quantisation study

That result put a 23.8pp gap between two rows that look like the same model, so the tags were
checked rather than assumed — the same repo has already been caught out by an Ollama tag that was an
alias for a different build with an identical digest.

| tag | quantisation | size | digest |
|---|---|---|---|
| `qwen3.5:2b` | **Q8_0** | 2.553 GB | `324d162b…` |
| `qwen3.5:2b-q4_K_M` | **Q4_K_M** | 1.812 GB | `124a03c3…` |

**Different weights, and worth knowing that Ollama's default `qwen3.5:2b` tag is Q8_0**, not Q4_K_M
— the row that reads like the baseline is the heavier build.

So this is a genuine quantisation pair, and the result is the wrong way round: **the Q4 build beats
the Q8 build by 23.8pp**, 71.4% against 47.6%, at 71% of the file size. Both were re-run as controls
and both reproduced their figures exactly — same score, cap hits, calls per run, declines and
over-refusals — so neither number is a flip.

The honest reading is not "Q4 is better". In v1 both sat at **47.6%**, identical. The entire gap
opened in v2: Q4 gained 23.8pp and Q8 moved **0.0**. Q4 was the one hitting the iteration cap
constantly (16 of 42 runs) and burning 7.43 calls per run on mangled output; Q8 was never cap-bound.
The fix returned a budget Q4 had been wasting and Q8 had not — and the two questions Q4 gained,
`fanout-actor-most-films` and `nearmiss-word-order`, are exactly the two it had been hitting the cap
on in v1. What this measures is which build was being penalised by the harness defect, not which
quantisation is better at the task — and it is a reminder that a harness bug does not penalise every
model equally.

#### Why the Q8 build scores 0% on near-miss and fan-out

"Q8 wasn't cap-bound" explains why the fix could not help it. It does not explain why it fails those
two families at all, so the transcripts were read rather than left as the inert half of the pair.

**It is not failing for lack of budget. It is stopping one hop short and inventing the last step.**
Twelve of its sixteen wrong answers — 75% — **never reached a required tool**, against four of six
for Q4. Ten of those twelve stopped at **4–6 calls against a cap of 10**, with four to six iterations
of headroom left unused. Its mean calls per answerable run is **4.18**; the lighter Q4 build spends
**5.12**. The heavier model does less work.

What that looks like on the wire, on `fanout-store-cities`:

| | calls | last hop | answer |
|---|---|---|---|
| Q8 | 6 | **`get_city` never called** | "city ID 85, which corresponds to **Baicheng**" |
| Q4 | 8 | `get_city(85)`, `get_city(200)` | "cities 85 (**Boksburg**) and 200 (Hamilton)" ✓ |

Q8 resolves store → address correctly, gets two `city_id`s, and then supplies the city names from
priors instead of making the two calls that would have produced them. The answer is fluent,
correctly structured, cites the right ids, and is fabricated at precisely the last hop. The same
shape on `nearmiss-film-language`: it finds ALABAMA DEVIL, calls `get_language(language_id: null)` —
a schema error — and concludes the language is unknown; Q4 calls `get_language(1)` and answers
English.

The two near-miss failures that are *not* premature stopping are worse in a different way. On
`nearmiss-actor-film-count` ("Angela Astaire", stored as Angela Astaire at actor 76) Q8 broadens the
failed search to **`"Angela"`** — the first name, which matches two other actors — and answers about
one of them. Q4 broadens to `"Astaire"` and finds the right actor in one call. On
`nearmiss-word-order` both builds locate WARDROBE PHANTOM; Q8 then reads the wrong number off the
row, and reads a *different* wrong number in each arm (7 days in v1, 3 in v2).

So Q8 does belong with qwen2.5:7b, but the cluster is not "gives up early" — it is **terminates the
chain before the evidence is in and fills the gap confidently**. 7b's version stops at two calls and
says nothing was found; Q8's goes almost all the way and confabulates the final lookup. Q8's is the
more dangerous failure, because nothing in the answer looks wrong.

---

## What moved, and what conspicuously did not

**Chain accuracy barely moved.** Most models are within ±10pp, and seven now sit at 100%:
qwen3.5:9b, gpt-4o, gpt-5.4, gpt-5.5, and all three 5.6 variants. The metric the harness was built
for remains saturated at the top — v1 concern 1b stands unchanged.

**Refusal moved the most, in the intended direction**, +25pp for six models: qwen2.5:3b, qwen3.5:4b,
qwen3.5:9b, qwen3.5:2b-q4_K_M, llama3.2, and gpt-5.x by smaller margins.

**Four models came back bit-identical** — gemma4:e2b, gemma4:e4b, mistral-nemo:12b and llama3.2
reproduced their v1 figures exactly, to two decimal places on calls per run. That is a free
determinism check across a two-day gap and a rebuild. It means different things for the two pairs,
though, and they should not be grouped: **the gemmas genuinely answer questions** (59% and 65% of
answerable ones), so their stability says a model reading one line of a tool result is unaffected by
how the rest is formatted. **mistral-nemo and llama3.2 answer 0/34.** Nothing moved there because
nothing was working — that is not evidence of robustness, it is the absence of anything to disturb.

### The counterexample: qwen2.5:7b, −9.5pp

The one model where the fix clearly hurt, and therefore the one worth a control rather than a
shrug. **It reproduces exactly** — a re-run under identical v2 configuration returned 20/42, 2 cap
hits, 2.71 calls per run, 6/8 declines, 12 over-refusals, every figure matching. The regression is
real, not a flip.

It is also the same mechanism as everywhere else, with the sign reversed. Six runs were lost and two
gained:

| flipped | |
|---|---|
| `decline-easy-category` ×2, `hop3-rental-film-title` ×2, `fanout-actor-most-films`, `nearmiss-word-order` | lost |
| `hop3-film-categories` ×2 | gained |

The clean tool output makes it **conclude faster and wrongly**. On `truncation-category-count` it
previously ground through `get_film_category_ids(1..9)` until the iteration cap and produced nothing;
now it stops after **two** calls and answers *"None of the films in the Horror category are found
among the results"*. Both are wrong — that question scores **0/2 in both arms** — but the failure
changed shape from `IterationCapReached` to a confident wrong answer, and three of its four new
over-refusals are of exactly that kind.

That is the "less persistent" effect seen in qwen3.5:4b and 9b. For 9b, already answering 100%,
less persistence was free. For 7b, which needed those extra iterations, it costs. The fix does not
help every model; it removes a crutch that some were relying on.

**ministral-3, −4.8pp** is the remaining uncontrolled mover, driven by fan-out 50% → 0% (one
question, both repeats) and declines 100% → 75%. Small enough that a single question explains it,
and not investigated.

## Family accuracy, v2

| Model | chain | near-miss | fan-out | trunc | decline |
|---|---|---|---|---|---|
| gpt-5.4 | **100%** | **100%** | **100%** | 100% | **100%** |
| qwen3.5:9b | **100%** | **100%** | **100%** | 100% | 75% |
| gpt-5.6-sol | 100% | 50% | 100% | 100% | 92% |
| gpt-5.5 | 100% | 50% | 100% | 100% | 83% |
| gpt-5.6-luna | 100% | 42% | 100% | 100% | 83% |
| gpt-5.6-terra | 100% | 25% | 100% | 100% | 83% |
| gpt-4o | 100% | **0%** | 50% | 100% | 75% |
| gpt-4o-mini | 93% | **0%** | 50% | 100% | 50% |
| qwen3:4b-instruct | 90% | 25% | 50% | 100% | 100% |
| qwen3.5:4b | 90% | 50% | 50% | 100% | 50% |
| gemma4:e2b | 90% | 0% | 0% | 100% | 100% |
| gemma4:e4b | 90% | 0% | 50% | 100% | 50% |
| qwen3.5:2b-q4_K_M | 80% | 75% | 100% | 100% | 25% |
| qwen3.5:2b | 80% | 0% | 0% | 100% | 25% |
| ministral-3 | 80% | 0% | 0% | 100% | 75% |
| qwen2.5:7b | 70% | 0% | 0% | 0% | 75% |
| hermes3:8b | 35% | 0% | 0% | 0% | 50% |
| qwen2.5:3b | 15% | 25% | 0% | 100% | 75% |
| llama3.1 | 10% | 25% | 0% | 0% | 50% |
| llama3.2 / mistral-nemo:12b / qwen2.5:1.5b | 0% | ≤25% | 0% | 0% | 0–75% |

**Near-miss recovery is still the sharpest discriminator, and the GPT-4o line still scores 0%.**
This is now a robust finding rather than a suspicion. The tool-output format changed underneath both
models — the thing they read on every single call — and the score did not move by a single run.
gpt-4o and gpt-4o-mini are **perfect on chains (100% and 93%) and score 0% on near-miss**, while
qwen3.5:9b scores 100% and **qwen3.5:2b-q4_K_M, at 1.8GB, manages 75%** — with the rubric attached:
*near-miss credit requires retrying the search with a variant and finding the row, so a model that
correctly reports the miss and stops scores zero here* (concern 7). Both GPT-4o models do exactly
that, cleanly and without fabricating; on a rubric that rewarded honest misses they would not be at
0%. On this one — where the question is whether the model keeps going when the chain breaks — a
1.8GB local model does something a frontier hosted model does not do at all.

**And it is the same axis that produced both of this sweep's surprises.** The fix made models less
persistent, because it removed the mangled output they had been burning iterations on. Near-miss is
the one family that *pays* for persistence: recovery means trying again after the first search
returns nothing. So qwen3.5:9b, which had persistence to spare, banked the saving as a gain, and
qwen2.5:7b, which was living off it, lost 9.5pp — the same mechanism, the same direction of change
in behaviour, opposite signs on the scoreboard purely because of headroom. Q8 sits at the far end of
that continuum with the least persistence of the three, and scores 0%.

---

## Concerns: what this closes and what it does not

**Closed by the re-run:**

- *9b, provider-asymmetric tool output.* Fixed and measured. Local and hosted now read the same
  format.
- *9c, an unrecorded configuration.* The gpt-4 reasoning hack is now `Agent:SendReasoningEffort`,
  recorded per run.
- *The retry hint on a terminal condition.* Fixed, +4.2pp refusal at flat over-refusal, matched-n.
- *6, repeats.* Hosted now n=3.

**Still live, unchanged from v1:**

- **1 and 1b** — hop-depth buckets confounded by question type, and the chain metric saturated at
  100% for seven models. Both worse in v2, not better, because more models now clear the chains.
- **2** — substring grading still passes unnavigated answers; the strict column still catches them.
  This sweep shows how much work that column is doing: **12 of qwen3.5:2b's 16 wrong answers never
  reached a required tool**, and they are fluent, correctly structured and cite real ids.
- **3** — the refusal classifier still reads conditional and planning language as refusal.
- **5** — hop 5 is still two questions; at n=2 local that is four runs.
- **7** — near-miss scoring still encodes a judgement that persistence is correct: credit requires
  retrying with a variant, so reporting the miss honestly scores zero. This is the qualifier on the
  gpt-4o-0%-versus-1.8GB-75% pair, and it is attached at the point of use rather than only here.
  It is a defensible rubric — the question is whether a model recovers when a chain breaks — but it
  is a choice, and the GPT-4o line fails it by being careful rather than by being wrong.

**New, from this sweep:**

- **ministral-3's −4.8pp has no control run.** Every other model that moved materially now does —
  qwen3.5:4b, 9b, gemma4:e4b, qwen2.5:7b and both 2b builds all reproduced their figures exactly on
  a second pass. ministral-3 is the one adverse result still resting on a single run.
- **Mixed denominators.** Local n=2 and hosted n=3 means raw counts are not comparable across the two
  groups anywhere in the v2 tab. Strict % is.
- **A harness defect does not penalise models equally.** The Q4/Q8 pair makes this concrete: an
  identical bug cost one build 23.8pp and the other nothing, because only one was hitting the
  iteration cap because of it. Any per-model number from a sweep with a known defect is suspect by an
  unknown and *non-uniform* amount — which is the argument for re-running rather than adjusting.
