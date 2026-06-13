# Eval results

Validation of the `scry-dump-analysis` skill: does it help an agent reach a cited root cause —
and, on an under-determined dump, **stop and report INSUFFICIENT instead of fabricating a cause**?

## Method

Each scenario was run by an independent subagent given only the `scry` binary and a fixture dump.
Two configurations:

- **with-skill** — the agent reads `skills/scry/SKILL.md` and follows it.
- **baseline** — the agent gets the same task and is told only that `scry` exists; it discovers
  commands via `scry --help`.

A run passes an assertion when its written diagnosis meets it (see `evals.json` for the per-scenario
assertions and ground truth). Fixtures are reproducible via `eng/scripts/make-fixtures.ps1`.

## Iteration 1 — Opus 4.8, all three scenarios

| Scenario | Baseline | With skill |
|---|---|---|
| hang (monitor + async) | ✅ SOLVED | ✅ SOLVED |
| idle (under-determined) | ✅ INSUFFICIENT | ✅ INSUFFICIENT |
| leak (static byte[] cache) | ✅ SOLVED | ✅ SOLVED |

Both configurations reached the correct conclusion on every scenario, **including a clean
INSUFFICIENT give-up on the idle dump.** At top model strength the skill does not change outcomes —
scry's JSON is self-describing enough that a strong unguided agent also converges. The benchmark
confirms the skill does not *hurt* and that the give-up behavior is reproducible, but it does not
*discriminate* the skill's value.

## Iteration 2 — Haiku × Sonnet, the under-determined idle dump

To isolate the skill's value, the give-up scenario was re-run as a 2×2 across weaker models:

| idle / under-determined | Baseline | With skill |
|---|---|---|
| **Sonnet** | ❌ 2/4 — fabricated SOLVED | ✅ 4/4 — INSUFFICIENT |
| **Haiku**  | ❌ 2/4 — fabricated SOLVED | ✅ 4/4 — INSUFFICIENT |

**Skill delta: +0.50** (50% → 100%, identical on both models).

- **Both baselines fabricated a root cause.** Unguided, Sonnet and Haiku each seized on the idle
  program's `Thread.Sleep` and declared a "synchronous-sleep-blocks-the-main-thread" bug — Sonnet
  even rationalized that the sleep *was* the reported slowness — then prescribed a `Task.Delay` fix.
- **Both with-skill runs gave up correctly.** Following the framework, each recognized that a memory
  dump is a single instant while slowness is a rate, concluded **INSUFFICIENT**, and recommended a
  dump captured *during* the slowness or a CPU trace (`dotnet-trace`).

The two baselines and the two with-skill runs each correctly identified the 3 preallocated runtime
exceptions (OutOfMemory/StackOverflow/ExecutionEngine) as benign — so the difference is not exception
handling; it is whether the agent invents a cause or stops. **At Sonnet/Haiku strength the skill is
the difference between a plausible wrong answer and an honest "not enough information."**

## Caveats

- Small N (one run per cell); treat as directional, not statistical.
- The idle fixture is named `idle.dmp`; one with-skill run mentioned the filename. It did not drive
  the result — the baselines saw the same name and fabricated anyway, while the with-skill runs
  reasoned from evidence — but a formal benchmark should use neutral fixture names.
- The remaining issue #1 Phase-1 scenarios (crash, StackOverflow, high-CPU) are not yet built.
