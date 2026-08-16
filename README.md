# Can a small model be a good agent?

> Posted to <https://spencerclark.dev/blog/can-a-small-model-be-good-agent/>

## Background

My previous posts had looked at using local LLMs to generate content, but what about using them in an agentic capacity? Can a local model on a modest GPU succeed as an agent, and work through a multi-stage action?

There are already hundreds of articles and videos on hooking a local LLM up to agents like OpenCode, Hermes Agent or OpenClaw that run on your PC and assist you with local tasks. What I wanted to do was build my own agent to perform a specific set of tasks, instrument it and then compare different models to see what capabilities they truly offer.

## Concept

**What is an Agent? It is a workflow where the model gets to decide the sequence.** Given tools and a goal, it chooses what to call, sees the results, and steers. Once it reaches its goal, or decides the goal is not achievable, it returns to the caller with a response.

If you can write the correct sequence down in advance, you don't need an agent — you need a script. The agent is only earning its keep when the second step depends on what the first one returned.

## Method

First question: what is this agent going to do? It needs to be something that requires information the model doesn’t have, and would mean it has to request that information from my harness via tool calls.

After looking at a few ideas, I landed on the Sakila example database that is very well-known from MySQL and has a Postgres port called Pagila. It represents a fictional video rental company with a schema covering films, actors, inventory and customers. And as it is all fabricated data built from recombined fragments of real names — Penelope Guiness, Nick Wahlberg — it is close enough to feel real without being anyone. That means a model can't answer from training data; it has to use the tools. Though as it turns out, some of them try anyway.

A harness would make a set of tools available to the model, and these tools would enable the model to query the data. Then the model could be presented with a question about the data, and it would have to work on its own to try and find the answer.

An obvious shortcut here would be to give the model two tools - `get_schema` and `execute_sql` - then the model would be able to compile its own queries and execute them to answer the question. But that would not really put it to the test, as it could solve any question in only two tool calls. I wanted it to reason over multiple steps.

So I decided each tool would offer a specific read query that only operated one level deep - if the model wanted to get to data several joins deep, it would need to understand it should make multiple chained tool calls to achieve it.

The agent loop would present the question to the model and handle its request to call tools. The tools themselves were all just SQL queries into the data. The harness allowed up to 20 iterations, with a budget of 15 executed tool calls. Once that budget was exhausted, further tool requests were refused, but the model could still return an answer using the evidence it had already collected.

This also fits under the increasingly broad label **agentic RAG**. Unlike conventional RAG, where retrieval is usually a fixed step performed before generation, the model controls retrieval from inside the agent loop: it chooses which database tool to call, inspects the result, and decides whether to retrieve again, try a different route, answer or decline.

There are no embeddings or vector searches here—the retrieved evidence is structured database data—but the underlying pattern is the same: generation is grounded in external information that the agent retrieves dynamically.

## Questions

Once that was done, I could compile a list of questions with their answers and the known routes to get those answers, as well as some red herrings and unanswerable questions to try and throw them off.

These questions could then be fed to each model and its responses graded to see which produced the most correct answers.

That would then enable me to score each model on how many questions it got right, near-misses, lucky guesses or outright hallucinations.

Example question JSON:

```json
{
    "id": "hop2-film-cost",
    "question": "What is the replacement cost of the film titled ALAMO VIDEOTAPE?",
    "expected_hops": 2,
    "expected_behaviour": "answer",
    "expected_answer": "16.99",
    "answer_kind": "numeric",
    "requires_tools": [ [ "search_film" ], [ "get_film" ] ]
}
```

So in this example it asks a simple question about a specific film. The model is expected to call the `search_film` tool first to get the `film_id` and then call `get_film` with that ID to get the replacement cost. The expected answer is 16.99.

Here is a more complicated example requiring four hops:

```json
{
  "id": "hop4-inventory-store-city",
  "question": "Inventory item 1 is held at a store. Which city is that store in?",
  "expected_hops": 4,
  "expected_behaviour": "answer",
  "expected_answer": "Boksburg",
  "answer_kind": "exact",
  "requires_tools": [ [ "get_inventory_item" ], [ "get_store" ],
                      [ "get_address" ], [ "get_city" ] ]
}
```

Here is an example of a question the model should not be able to answer:

```json
{
  "id": "unanswerable-missing-entity",
  "question": "What is the rental rate of the film ZOMBIE ACCOUNTANT PARADOX?",
  "expected_hops": 1,
  "expected_behaviour": "decline",
  "expected_answer": null,
  "answer_kind": "decline",
  "requires_tools": [ [ "search_film" ] ],
  "note": "REFUSAL CASE 1 - film does not exist. Correct behaviour: search, get NO ROWS, say so. Any rental rate given is a hallucination."
}
```

So with this example, the model would get marked down if it tried to answer this question with a value as that would be a clear hallucination.

Questions fall into these categories:

| Category | Questions |
| --- | --- |
| Chain | Answer is reachable by chaining multiple tool calls - e.g. `search_film` into `get_film` |
| Declines | Questions that are not answerable. The model should refuse to give an answer. |
| Near-miss | The question has a deliberate mistake. But the model should be able to recover from it and continue. Example: “What is the rental rate of the film CASABLANCA NIGHTS?”. There is no film with that name but there is one called CASABLANCA SUPER. If the model retries the search but with just the first word it will get that hit and can continue. |
| Fan-out | Questions where one stage returns multiple rows and the model must chain down each leg. Example: “The film AIRPLANE SIERRA is held at more than one store. Which cities are those stores in?” The model calls `search_film` and then `get_film_inventory_ids` to find out which stores hold it. But then for EACH store it needs to go `get_store` > `get_address` > `get_city` |
| Truncation | When results are truncated, the tool reports both the total and displayed counts—for example, ‘142 rows, showing the first 50.’ This tests whether the model uses 142 rather than answering 50. |

## Code

While this article is less about the code, and more about the method and results, there are a few parts I wanted to get into. The full code is available at <https://github.com/spenceclark/MovieAgent>

The solution is structured into three projects:

- `MovieAgent` - Manages the IHost, CLI parsing, dependency injection and SQL execution. This can be ignored pretty much for the purposes of this article. It is boilerplate code mostly.
- `MovieAgent.Agent` - Contains the code for the main agent loop. This may be interesting to anyone wanting to see how an agent can be implemented in dotnet.
- `MovieAgent.Evaluation` - Defines the questions we will ask the agents, and contains all the evaluation and grading logic. This was the core of the work done.

The main agent loop consists of up to 20 iterations of:

- Submit accumulated conversation (@i=0 this is just the initial question)
- Preserve Response
- Execute any tools requested
- Augment context with tool responses
- Repeat or break when given an answer

The loop may run for up to 20 iterations, but the real work budget is 15 executed tool calls.

In the main evaluation, the model never sees or writes SQL. Each tool maps to a fixed query, and the arguments supplied by the model map one-to-one to SQL parameters.

```csharp
new()
{
    Name = "search_category",
    Description = "Find film categories whose name contains the given text. Returns category_id and name.",
    Table = "category",
    Sql = "select category_id, name from category where name ilike '%' || @name_contains || '%' order by category_id",
    Parameters =
    [
        ToolParameter.Term(
            "name_contains",
            "Text to look for in the category name.")
    ],
    EmptyResultHint = "No category name contains that text.",
},
```

This deliberately small, read-only surface is not a complete production data-access layer. The fixed query and parameter binding prevent conventional SQL injection, but a production system would still need authorization, query timeouts, result limits, auditing and careful control over which data each tool can expose.

The evaluation code in MovieAgent.Evaluation is where I spent most of the development time. It determines whether an answer is correct and, more importantly, how the model reached it.

- Did it hallucinate tool-call names, parameter names, or parameter values?
- Did it call every expected tool required to reach the answer?
- Did it repeat calls or hit the tool-call budget or iteration guard?
- Did it produce the right answer without completing the evidence path?

> That distinction between producing the right answer and reaching it through an evidence-backed path turned out to matter far more than I expected.

## Evaluation Setup

> These tests were carried out on a RTX 3070 with 8GB of VRAM. Models were chosen to fit inside this limit. A couple overflowed onto CPU slightly, but these tests do not measure wall-time. Ollama was used to run the models.

The final sweep tested 22 models on 23 questions, twice each: 1,012 recorded runs.

One deliberately ambiguous question was retained as an unscored exhibit, leaving 44 scored runs per model.

Two model-specific server failures reduced the scored denominators for `qwen3.5:2b` and `ministral-3`.

Parameters:

- seed 42, temperature 0, thinking off;
- 2,500 maximum output tokens;
- the 15-call budget and 20-iteration guard;
- five historical zero-call models were not rerun in v3.
- 8k context size in Ollama

### Sweep history

- **V1** established the baseline and exposed two harness problems: local and hosted models received differently formatted tool results, and one supposedly terminal error incorrectly encouraged retries.
- **V2** fixed those problems, but later revealed another fairness issue: models that batched calls could perform more work than models making one call per turn.
- **V3** replaced the iteration-based allowance with an equal 15-tool-call budget, raised the iteration guard to 20, removed the instruction to call tools one at a time, and reran the final evaluation.

All scores and rankings in this post come from **V3**. V1 and V2 are retained only as development and audit history; their results are not directly comparable with V3.

## Findings

The headline result was not simply that a local model came close to the hosted leaders. In several of the tests that mattered most, the strongest local models behaved better.

### Clean chains stop discriminating among capable tool users

Clean chains strongly separated the weakest models, some of which never chained at all. Once a model could reliably use the tool channel, however, the metric quickly saturated: GPT‑4o and GPT‑4o‑mini both scored 22/22, as did most of the GPT‑5 line. Recovery and fan-out were more useful for separating the stronger models.

Hop depth by itself did not predict difficulty: five-hop questions scored 67%, compared with 53% for two-hop and 52% for four-hop. The buckets are confounded by question type—the two-hop bucket contains the hostile near-misses, while the five-hop questions are clean chains.

### Recovery is the real discriminator

The tests that separated the models were those in which an initial lookup failed and the model had to adapt. `Qwen3.5:9b` and the much smaller 4B model both recovered 6/8 near-misses. That compares favourably with `GPT-4o` and `GPT-4o-mini`: both were perfect on the clean chain tests, but recovered only 0/8 and 1/8 respectively.

Both `GPT-4o` models correctly identify the miss, but then stop. They don’t fabricate anything but they don’t try to recover either.

Meanwhile:

- GPT-5.4: 8/8
- Qwen 3.5 9B: 6/8
- Qwen 3.5 4B: 6/8
- GPT-5.5/ 5.6 Sol / 5.6 Luna: 4/8

*Example*:

The question: “*What language is the film ALABAMA'S DEVIL in?*” is a trick question. The real film name is ALABAMA DEVIL.

`GPT-4o` calls the tool `search_film`, gets no rows and returns “*There is no film titled "ALABAMA'S DEVIL" in the database. Please check the spelling or try a different title*.”. That response is factually defensible, but the question is recoverable.

`GPT-5.4` and `Qwen3.5:9b`, however, try again by searching for a single word - and finding the `film_id` that way.

Not every near-miss was equally near, though. Another trick question is: “*What is the rental rate of the film CASABLANCA NIGHTS?*” where the real name is CASABLANCA SUPER. Resolving *ALABAMA'S DEVIL* to *ALABAMA DEVIL* is little more than punctuation tolerance. Resolving *CASABLANCA NIGHTS* to *CASABLANCA SUPER* requires a stronger assumption: the films share one distinctive word, but the rest of the title is different.

`GPT-5.6` Luna and Sol found *CASABLANCA SUPER* and correctly identified it as the database's only Casablanca title, but stopped rather than assume it was the film the user meant. Under this evaluation that is a failure, because they did not retrieve the rental rate. It is not, however, obviously bad judgement. `Qwen3.5:9b` produced the more useful compromise: it explained that the requested title did not exist, then gave the price of *CASABLANCA SUPER* conditionally.

The result therefore measures more than persistence. It also exposes where each model places its threshold for resolving an uncertain entity without asking the user.

### The prompt was not causing the refusals

The system prompt told models that declining was correct when the available data could not answer the question. That creates an obvious concern: were the refusal scores measuring judgement, or merely obedience to that instruction?

To test this, I reran three models after removing that single sentence. Their correct-decline scores did not change:

- `Qwen3.5:9b`: 8/8 with the instruction, 8/8 without it
- `Qwen3.5:2b-q4_K_M`: 6/8 with it, 6/8 without it
- `GPT-4o-mini`: 6/8 with it, 6/8 without it

Removing the instruction did not leave the runs untouched. For `qwen3.5:9b`, only 2 of 46 trajectories were bit-identical across the two versions, yet its outcomes remained exactly the same: 42/44 strict, 36/36 answerable questions, 8/8 correct declines and no over-refusals. The Q4 2B model did lose four answerable runs, dropping from 34/44 to 30/44 strict, but its correct declines remained 6/8. The ablation therefore supports a narrow conclusion: the instruction was not producing the refusal behaviour, even though changing the prompt could affect other answers.

### An answer can contain the right number for the wrong reason

Qwen3.5‑9B passed all 44 scored runs under the ordinary grader, including all 36 answerable runs, but scored 42/44 under strict grading.

Both disputed runs ask for the rental duration of `PHANTOM WARDROBE`, expected answer **6**. The model successfully recovers the real title, `WARDROBE PHANTOM`, but never calls the tool containing the film’s configured rental duration. Instead, it explores actual rental records, performs date arithmetic, and concludes:

> “The answer is likely 3 days.”

It nevertheless passes the substring matcher because its long working includes one rental lasting approximately six days. Neither repeat earns strict credit, because the model never retrieved the configured rental duration and its stated answer was wrong.

### Lower precision scored higher—but behaved much worse

The 2B-parameter Qwen3.5 model was tested in both an 8-bit (Q8_0) and 4-bit (Q4_K_M) quantization. The expectation was the 8-bit would perform better.

| | Chain % | Near-Miss % | Fan-Out % | Truncation % |
| --- | --- | --- | --- | --- |
| Q4_K_M | 91 | 25 | 100 | 100 |
| Q8_0 | 82 | 0 | 0 | 100 |

The 8-bit version fared slightly worse on the chain tests, failed every near-miss and fan-out test, and passed every truncation test.

A fan-out example: “*The film AIRPLANE SIERRA is held at more than one store. Which cities are those stores in?*”. Q8 correctly found both stores and their addresses. On the first branch it passed the address_id, 129, to get_city as though it were a city_id — returning Cuauhtémoc, a real city with an unrelated id. That produced the wrong city, which it then reported confidently.

The Q4 build scored much higher overall, but it was also far less controlled: it exhausted the call budget in 12 runs, made 90 over-budget calls and attempted 102 blocked repeats. In this evaluation, lower precision produced the better score—but not the cleaner agent.

### One-call-only behaviour

Llama3.2 consistently failed to issue a second tool call. It scored 4/44, with all four points coming from two unanswerable questions it correctly declined.

For one question - “What is the replacement cost of the film titled ALAMO VIDEOTAPE?”  it decided to jump straight to `get_film` tool with two parameters it made up:

`{"film_id":"search_film","title_contains":"ALAMO VIDEOTAPE"}`

It then turned around and asked the user for the `film_id`.

```text
It seems that the `get_film` tool requires a `film_id` instead of a `title_contains`. I'll try again with a different approach.

Can you please provide me with the `film_id` for the film "ALAMO VIDEOTAPE"? I can then use this value to look up the replacement cost.
```

### Hard Failures

Despite their templates or metadata declaring tool support, some models failed completely to emit a single well-formed tool call.

These models were:

| Name | Variant | Tag |
| --- | --- | --- |
| Mistral | latest | mistral |
| Granite3.3 | 8b | granite3.3:8b |
| Command R | 7b | command-r7b |
| DeepSeek R1 | 8b | deepseek-r1:8b |
| Phi4 | mini | phi4-mini |

DeepSeek and Mistral attempted some form of tool call but embedded a hallucinated format inside the response text. Mistral for example:

```markdown
To find the replacement cost of the film titled "ALAMO VIDEOTAPE", we first need to search for the film's ID using the `search_film` tool:

'''
search_film(title_contains="alamo videotape")
'''

Assuming the result is a list containing one row with the film's ID, let's call it `film_id`. Then we can use the `get_film` tool to get the replacement cost:

'''
get_film(film_id=film_id)
'''

The output will contain the replacement cost as one of its fields.
```

Phi4 emitted **structurally correct tool-call JSON, with real tool names and real parameter names** — into the content channel, missing only the `<|tool_call|>` delimiter its own chat template requires.

Command R refused almost every answerable question. Granite behaved differently: it often described or printed plausible tool calls in ordinary response text, but never entered the structured tool channel.

```text
I'm sorry, but I don't have access to information about replacement costs. The available tools allow me to search for films by title, retrieve film details including actor and category information, find customers or actors, get language details, address and store information, and more. However, there's no tool provided to retrieve the replacement cost of a film.
```

So Command R knew it had tools, but because no single tool exactly matched what it needed, it refused to go further.

## Scores

`Raw` = answer contained the expected value. Substring matching, so it can pass an answer the model never actually looked up.

`Strict` = correct **and**, where the question requires traversal, having reached every required tool.

`Answers` = correct answerable questions, on the same raw matching as `Raw`.

`Declines` = correctly declined unanswerable questions.

`Over-ref` = over-refusals - declined to answer an answerable question.

`Calls` = average tool calls per run.

| # | Model | Raw | Strict | Strict Score | Answers | Declines | Over-ref | Calls |
| :---: | :--- | :---: | :---: | :---: | :---: | :---: | :---: | ---: |
| 1 | gpt-5.4 | 43/44 | 43/44 | **97.7** | 35/36 | 8/8 | 0 | 3.39 |
| 2 | qwen3.5:9b | 44/44 | 42/44 | **95.5** | 36/36 | 8/8 | 0 | 4.45 |
| 3= | gpt-5.5 | 40/44 | 40/44 | **90.9** | 32/36 | 8/8 | 4 | 3.32 |
| 3= | gpt-5.6-sol | 40/44 | 40/44 | **90.9** | 32/36 | 8/8 | 4 | 3.34 |
| 5 | gpt-5.6-luna | 39/44 | 39/44 | **88.6** | 31/36 | 8/8 | 4 | 3.23 |
| 6 | qwen3.5:4b | 38/44 | 38/44 | **86.4** | 30/36 | 8/8 | 2 | 5.36 |
| 7 | gpt-5.6-terra | 37/44 | 37/44 | **84.1** | 30/36 | 7/8 | 6 | 3.05 |
| 8 | qwen3:4b-instruct | 35/44 | 35/44 | **79.5** | 27/36 | 8/8 | 6 | 2.86 |
| 9 | qwen3.5:2b-q4_K_M | 34/44 | 34/44 | **77.3** | 28/36 | 6/8 | 2 | 8.05 |
| 10= | gpt-4o | 33/44 | 33/44 | **75.0** | 26/36 | 7/8 | 8 | 2.84 |
| 10= | gpt-4o-mini | 33/44 | 33/44 | **75.0** | 27/36 | 6/8 | 7 | 3.39 |
| 12 | gemma4:e4b | 32/44 | 32/44 | **72.7** | 24/36 | 8/8 | 8 | 2.36 |
| 13 | gemma4:e2b | 29/44 | 29/44 | **65.9** | 22/36 | 7/8 | 8 | 2.27 |
| 14 | qwen2.5:7b | 27/44 | 25/44 | **56.8** | 19/36 | 8/8 | 12 | 2.75 |
| 15 | ministral-3 | 24/43 | 24/43 | **55.8** | 17/35 | 7/8 | 10 | 2.49 |
| 16 | qwen3.5:2b | 22/42 | 22/42 | **52.4** | 20/36 | 2/6 | 6 | 5.76 |
| 17 | qwen2.5:3b | 11/44 | 11/44 | **25.0** | 6/36 | 5/8 | 9 | 3.75 |
| 18 | hermes3:8b | 12/44 | 7/44 | **15.9** | 7/36 | 5/8 | 5 | 1.93 |
| 19 | mistral-nemo:12b | 6/44 | 6/44 | **13.6** | 0/36 | 6/8 | 11 | 1.16 |
| 20 | llama3.2 | 4/44 | 4/44 | **9.1** | 0/36 | 4/8 | 6 | 1.00 |
| 21 | llama3.1 | 6/44 | 3/44 | **6.8** | 6/36 | 0/8 | 11 | 2.84 |
| 22 | qwen2.5:1.5b | 4/44 | 2/44 | **4.5** | 2/36 | 2/8 | 2 | 0.34 |
| — | deepseek-r1:8b | — | — | — | — | — | — | — |
| — | phi4-mini | — | — | — | — | — | — | — |
| — | command-r7b | — | — | — | — | — | — | — |
| — | granite3.3:8b | — | — | — | — | — | — | — |
| — | mistral | — | — | — | — | — | — | — |

> All models were run twice per question, so one changed run moves a score by 2.3 percentage points. The local models produced identical outcomes across their two repeats, while several hosted-model repeats differed. Small gaps involving hosted models should therefore not be read as precise rankings.
>
> DeepSeek R1, Phi4-mini, Command R, Granite3.3 and Mistral were not rerun in v3. Each had produced zero structured tool calls in the original sweep, so the v3 changes could not affect its observed failure mode.
>
> `qwen3.5:2b` and `ministral-3` have denominators below 44 because reproducible Ollama 500 errors were excluded from scoring. These were ultimately traced to chat-template handling of assistant messages containing both text and tool calls.

Full reports for every model are available in the GitHub repo under the reports folder: <https://github.com/spenceclark/MovieAgent/tree/main/reports>

## What if I just gave it SQL?

At the start I argued that giving the model `get_schema` and `execute_sql` would be a backdoor: one SQL join could replace a five-step tool chain. To check whether my deliberately constrained tools were themselves causing the failures, I ran a control using only those two SQL tools against the ten clean chain questions, twice per model.

| Model | Constrained tools | SQL shortcut |
| --- | --- | --- |
| granite3.3:8b | 0/20 | 0/20 |
| command-r7b | 0/20 | 0/20 |
| llama3.2 | 0/20 | 0/20 |
| mistral-nemo:12b | 0/20 | 2/20 |
| gemma4:e4b | 18/20 | 18/20 |
| qwen3.5:4b | 18/20 | 20/20 |

The SQL control used a separate system prompt tailored to those two tools.

Across the four failing models the shortcut improved the result by only two runs out of eighty. Granite and Command R still made no structured tool calls. Llama3.2 wrote SQL before inspecting the schema in every run and all 24 of its queries failed. Mistral-Nemo remained a one-call model: in 14 runs it retrieved the schema and then stopped without executing anything; in the other six it wrote SQL first. its two passes were the two repeats of the only question it could answer with a single direct query.

Gemma and Qwen were the only models that both read the schema and continued to SQL on every run. Gemma remained at 18/20. Qwen improved from 18/20 on the same constrained questions to 20/20 with SQL, showing that the shortcut can help a model that already uses tools competently. It did not turn any of the failing models into a useful one.

This is not evidence that SQL is a better agent interface—text-to-SQL is a different and much more heavily trained task. It is evidence that the constrained tool surface was not manufacturing the failures. The successful models checked, acted on what they found and continued; the unsuccessful ones failed before the shorter route could help them.

## Conclusion

The answer to my original question is yes: a local model running on modest hardware can work as an agent. `Qwen3.5:9b` scored 42/44, only one strict run behind `GPT-5.4`, while the 4B version reached 38/44, ahead of both GPT-4o models. Even the 1.8GB Q4 model scored 34/44, although its high score came with severe looping and budget-control problems; small did not necessarily mean clean or reliable.

But the leaderboard is not the most useful result. Following a clean chain was rarely the difficult part. The differences appeared when a lookup returned nothing, when one result had to be followed down several branches, or when the model needed to recognise that the available evidence was insufficient. In other words, the hard part was not making tool calls; it was deciding what to do when the obvious next call did not work.

The failures were also behavioural rather than simply a question of model size. Some models never entered the tool channel. Some made one call and stopped regardless of the result. Others guessed identifiers or schema rather than checking them. The strongest models searched, inspected what came back, corrected themselves and continued until they had either found an evidence-backed answer or established that one was not reachable.

That makes the agent loop itself almost the least interesting part of the system. It is only a small amount of code. The difficult and important work is designing a tool surface that exposes the right capabilities, recording the path the model took, and evaluating more than whether its final response happened to contain the expected string.

This is not a universal ranking of these models. It is one database, one tool catalogue and one set of questions. What it does show is that small local models can be genuinely capable agents within a well-defined domain—and that testing them requires broken paths, ambiguity and opportunities to recover, not just longer and longer happy-path chains.
