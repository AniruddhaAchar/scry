# scry-dump-analysis skill

An agent skill that teaches how to drive [`scry`](../../README.md) to diagnose .NET memory dumps —
and, crucially, a **bounded reasoning loop** (orient → hypothesize → test → progress-check → stop)
that converges on a cited root cause or stops and reports the dump lacks enough information, instead
of spinning. See [issue #1](https://github.com/AniruddhaAchar/scry/issues/1).

## Contents

```
skills/scry/
├── SKILL.md                 # entry point: setup, command index, the reasoning loop + stop conditions
├── references/
│   ├── commands.md          # every command: args, exact JSON shape, gotchas
│   └── playbooks.md         # per-symptom recipes (crash / deadlock / async hang / leak-OOM / high-CPU)
└── evals/
    └── evals.json           # the validation scenarios: prompts, ground truth, assertions
```

## Using it

**Claude Code (plugin — recommended)** — this repo is a Claude Code plugin marketplace, so you can
install the skill directly:

```
/plugin marketplace add AniruddhaAchar/scry
/plugin install scry@scry
```

That's it — Claude Code clones the skill into its plugin cache and loads it. The `description`
frontmatter governs when it triggers (any .NET dump / crash / hang / leak / deadlock task); you can
also invoke it explicitly as `/scry:scry-dump-analysis`. Refresh later with
`/plugin marketplace update scry`. (See [the plugin docs](https://code.claude.com/docs/en/plugins).)

**Claude Code (manual copy)** — alternatively, copy (or symlink) the skill into a skills directory:

```bash
# project-scoped (this repo), or swap for ~/.claude/skills for personal use
cp -r skills/scry ./.claude/skills/scry-dump-analysis
```

**Other systems** — point your agent at `skills/scry/SKILL.md` and let it follow the references.
The skill assumes `scry` is on `PATH` (or that you pass its path).

**Packaging** — to produce a distributable `.skill` bundle, run the skill-creator's
`package_skill.py` against `skills/scry`.

## How it was validated

`evals/evals.json` defines three scenarios — a multi-bug **hang** (monitor + async), an
under-determined **idle** dump (the give-up test), and a static-cache **leak**. Each was run with an
agent *with* the skill vs. a *baseline* agent (scry + `--help` only). Both reached the correct
conclusion on all three — including a clean INSUFFICIENT on the idle dump — confirming the skill
produces well-formed SOLVED/INSUFFICIENT outcomes and the give-up behavior is reproducible.

The fixtures aren't committed (memory dumps are large and git-ignored). Regenerate them with:

```bash
pwsh ./eng/scripts/make-fixtures.ps1     # builds samples/fixture-victim, writes hang/idle/leak.dmp
```
