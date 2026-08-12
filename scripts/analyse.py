"""Read one or more run JSONL files and report accuracy, navigation and run-to-run variance.

Usage:
    python scripts/analyse.py runs/sweep-n3.jsonl

Deliberately a separate script rather than a harness feature: the JSONL is the dataset, and
analysis should be re-runnable over old files without rebuilding or re-running anything.
"""

import json
import sys
from collections import defaultdict


def load(paths):
    rows = []
    for p in paths:
        with open(p, encoding="utf-8") as fh:
            rows.extend(json.loads(line) for line in fh if line.strip())
    return rows


def pct(n, d):
    return f"{100.0 * n / d:.0f}%" if d else "-"


def main(paths):
    runs = load(paths)
    if not runs:
        print("no runs found")
        return

    exhibits = [r for r in runs if (r.get("grade") or {}).get("scored") is False]
    runs = [r for r in runs if (r.get("grade") or {}).get("scored") is not False]

    print(f"{len(runs)} scored runs from {len(paths)} file(s)"
          + (f"  ({len(exhibits)} unscored exhibit runs excluded)" if exhibits else "") + "\n")

    # ---------------------------------------------------------------- per surface
    # Accuracy is split by expected behaviour, and navigation is computed over answerable
    # questions only. A decline-expected question can never have navigation_complete — the tool
    # it needs is by definition absent from the surface — so pooling them makes a surface that
    # is mostly unreachable look like a navigation failure when it is a reachability definition.
    print("SURFACE SUMMARY")
    print(f"  {'surface':10s} {'n':>4s} | {'answerable':>17s} {'nav':>9s} | {'decline':>15s} | "
          f"{'caps':>5s} {'empty':>5s} {'repeats':>8s} {'calls':>6s}")
    by_surface = defaultdict(list)
    for r in runs:
        by_surface[(r["tool_surface"], r["model"])].append(r)

    for (surface, _model), rs in sorted(by_surface.items()):
        answerable = [r for r in rs if (r.get("grade") or {}).get("expected_behaviour") == "answer"]
        decline = [r for r in rs if (r.get("grade") or {}).get("expected_behaviour") == "decline"]
        ac = sum(1 for r in answerable if (r.get("grade") or {}).get("correct"))
        dc = sum(1 for r in decline if (r.get("grade") or {}).get("correct"))
        nav = sum(1 for r in answerable if (r.get("grade") or {}).get("navigation_complete"))
        caps = sum(1 for r in rs if r["cap_hit"])
        empty = sum(1 for r in rs if r.get("outcome") == "EmptyAnswer")
        reps = sum(1 for r in rs for i in r["iterations"] for c in i["tool_calls"] if c.get("was_repeat"))
        calls = sum(r["tool_call_count"] for r in rs) / len(rs)
        print(f"  {surface:10s} {len(rs):4d} | {ac:6d}/{len(answerable):<3d} {pct(ac,len(answerable)):>5s} "
              f"{nav:4d} {pct(nav,len(answerable)):>4s} | {dc:6d}/{len(decline):<3d} {pct(dc,len(decline)):>5s} | "
              f"{caps:5d} {empty:5d} {reps:8d} {calls:6.2f}")
    print("  Surface totals are NOT comparable: each surface has a different answerable/decline mix.")
    print("  For a like-for-like read, compare only questions answerable on both surfaces.")
    print("  empty = outcome EmptyAnswer: the model stopped calling tools and said nothing.")
    print()
    print("  NECESSARY, NOT SUFFICIENT: 'nav' checks that every required tool was called, not that")
    print("  the chain was correct. hop4-inventory-store-city can navigate completely and still")
    print("  answer 'Yerevan, Armenia' by following the staff member's address instead of the")
    print("  store's - every required tool ran, with the wrong argument. It can falsify a claim of")
    print("  navigation; it cannot confirm one. Not attempted to fix.")
    print()
    print("  KNOWN FALSE-POSITIVE MODE: 'correct' is substring matching and can pass a partly wrong")
    print("  answer that happens to also contain the right substring, e.g. 'ADAPTATION HOLES is in")
    print("  English and Italian' passes on 'Italian' whether or not English is also right. Not")
    print("  attempted to fix.")

    # ------------------------------------------------------------- tool-call diagnostics
    # Argument provenance and schema-error classification, both computable purely from
    # arguments_raw/result_text/question — see MovieAgent.Evaluation.ToolCallDiagnostics.
    print("\nTOOL-CALL DIAGNOSTICS (argument provenance and schema errors)")
    print(f"  {'surface':10s} {'model':20s} {'fab/call':>10s} {'call_id':>8s} {'type_mm':>8s} "
          f"{'schema':>8s} {'calls/iter':>10s}")
    for (surface, model), rs in sorted(by_surface.items()):
        total_calls = sum(r["tool_call_count"] for r in rs)
        fab = sum((r.get("grade") or {}).get("fabricated_argument_count", 0) for r in rs)
        call_id = sum((r.get("grade") or {}).get("call_id_as_argument_count", 0) for r in rs)
        type_mm = sum((r.get("grade") or {}).get("argument_type_mismatch_count", 0) for r in rs)
        schema = sum((r.get("grade") or {}).get("schema_error_count", 0) for r in rs)
        call_iters = sum(1 for r in rs for i in r["iterations"] if i["tool_calls"])
        cpi = total_calls / call_iters if call_iters else 0
        print(f"  {surface:10s} {model:20s} {fab:4d}/{total_calls:<4d}  {call_id:8d} {type_mm:8d} "
              f"{schema:8d} {cpi:10.2f}")
    print("  fab/call = fabricated arguments as a fraction of all tool calls (not all fabrications")
    print("  are one-per-call, so this is a rate, not a per-call average).")
    print("  calls/iter = mean_calls_per_iteration, the batching metric: calls / iterations that")
    print("  made at least one call.")

    # ------------------------------------------------------------------ variance
    # The point of n>1 at a fixed seed: any question that does not always land the same way
    # is noise, and sets a floor under how large a surface difference has to be to mean anything.
    print("\nRUN-TO-RUN VARIANCE (same seed; a split result is pure noise)")
    unstable = 0
    groups = defaultdict(list)
    for r in runs:
        groups[(r["tool_surface"], r["question_id"])].append(r)

    for (surface, qid), rs in sorted(groups.items()):
        if len(rs) < 2:
            continue
        outcomes = [bool((r.get("grade") or {}).get("correct")) for r in rs]
        if len(set(outcomes)) > 1:
            unstable += 1
            calls = ", ".join(str(r["tool_call_count"]) for r in rs)
            print(f"  {surface:14s} {qid:32s} {sum(outcomes)}/{len(outcomes)} correct   calls: {calls}")

    total_groups = sum(1 for rs in groups.values() if len(rs) >= 2)
    print(f"  {unstable}/{total_groups} question-surface pairs gave inconsistent results "
          f"({pct(unstable, total_groups)} unstable)")
    if unstable:
        print("  Any surface comparison smaller than this noise floor is unreadable.")

    # --------------------------------------------------------------- by hop depth
    print("\nBY HOP DEPTH (answerable only)")
    print(f"  {'surface':14s} {'hops':>4s} {'n':>4s} {'correct':>9s} {'navigated':>11s}")
    hops = defaultdict(list)
    for r in runs:
        g = r.get("grade") or {}
        if g.get("expected_behaviour") == "answer":
            hops[(r["tool_surface"], r.get("expected_hops"))].append(r)
    for (surface, h), rs in sorted(hops.items(), key=lambda kv: (kv[0][0], kv[0][1] or 0)):
        c = sum(1 for r in rs if (r.get("grade") or {}).get("correct"))
        n = sum(1 for r in rs if (r.get("grade") or {}).get("navigation_complete"))
        print(f"  {surface:14s} {h:4} {len(rs):4d} {c:4d} {pct(c,len(rs)):>4s} {n:6d} {pct(n,len(rs)):>4s}")

    # ------------------------------------------------------------------- refusals
    print("\nREFUSAL")
    print(f"  {'surface':14s} {'cases':>6s} {'declined':>9s} {'over-refusals':>14s}")
    for (surface, _model), rs in sorted(by_surface.items()):
        cases = [r for r in rs if (r.get("grade") or {}).get("expected_behaviour") == "decline"]
        answerable = [r for r in rs if (r.get("grade") or {}).get("expected_behaviour") == "answer"]
        ok = sum(1 for r in cases if (r.get("grade") or {}).get("declined"))
        over = sum(1 for r in answerable if (r.get("grade") or {}).get("declined"))
        print(f"  {surface:14s} {len(cases):6d} {ok:4d} {pct(ok,len(cases)):>4s} {over:14d}")

    # ----------------------------------------------------- navigated but not correct
    # The interesting failure: reached every required tool and still got the answer wrong.
    print("\nNAVIGATED BUT WRONG (reached every required tool, answer still wrong)")
    found = False
    for r in runs:
        g = r.get("grade") or {}
        if g.get("navigation_complete") and not g.get("correct") and g.get("expected_behaviour") == "answer":
            found = True
            print(f"  {r['tool_surface']:14s} {r['question_id']:32s} rep{r['repeat']}  {g.get('note')}")
    if not found:
        print("  none")

    # ------------------------------------------------ correct but did not navigate
    # The mirror case, and the sharper one: the answer is right despite never calling a tool
    # the shortest correct chain needs. navigation_complete is a necessary condition for having
    # actually done the work, so this is at minimum suspicious and worth reading by hand — a
    # coincidentally-right guess and a genuinely-derived answer are indistinguishable from the
    # score alone.
    print("\nCORRECT BUT DID NOT NAVIGATE (right answer, a required tool was never called - check for a guess)")
    found = False
    for r in runs:
        g = r.get("grade") or {}
        if g.get("correct") and not g.get("navigation_complete") and g.get("expected_behaviour") == "answer":
            found = True
            print(f"  {r['tool_surface']:14s} {r['question_id']:32s} rep{r['repeat']}  missing: {g.get('required_tools_missing')}")
    if not found:
        print("  none")

    # ---------------------------------------------------------------- reasoning volume
    # Ollama's per-turn reasoning (message.thinking) is not carried into the next request —
    # verified against the raw wire traffic, see RunRecord.IterationRecord.ReasoningText. Every
    # iteration regenerates its plan from tool-call history alone. This does not say whether
    # that costs accuracy; it quantifies what it costs in tokens, which is otherwise invisible.
    reasoning_iters = [
        i for r in runs for i in r["iterations"] if i.get("reasoning_text")
    ]
    if reasoning_iters:
        lengths = [len(i["reasoning_text"]) for i in reasoning_iters]
        thinking_runs = sum(1 for r in runs if r.get("thinking"))
        print(f"\nREASONING VOLUME ({len(reasoning_iters)} iteration(s) across {thinking_runs} thinking-on run(s))")
        print(f"  mean length: {sum(lengths)/len(lengths):.0f} chars   "
              f"total: {sum(lengths):,} chars generated, 0 chars carried to the next iteration")

    # ---------------------------------------------------------- truncation cross-check
    # For any run where a tool call truncated: does the model's stated total match the true
    # total the tool itself reported? A match is not proof of having read it (the model could
    # coincidentally land on the right number some other way), but a mismatch is proof it did
    # not come from here, which a bare correct/incorrect score cannot distinguish from a right
    # answer reached by reading the notice properly.
    truncated = [r for r in runs if (r.get("grade") or {}).get("truncation_seen")]
    if truncated:
        print(f"\nTRUNCATION CROSS-CHECK ({len(truncated)} run(s) saw a truncated list)")
        for r in truncated:
            g = r["grade"]
            match = g.get("answer_matches_stated_total")
            verdict = "answer matches stated total" if match else "MISMATCH - answer did not come from the count line"
            print(f"  {r['tool_surface']:14s} {r['question_id']:32s} rep{r['repeat']}  "
                  f"stated total={g.get('truncation_stated_total')}  {verdict}")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    main(sys.argv[1:])
