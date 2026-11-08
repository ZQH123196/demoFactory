# Domain Docs

How the engineering skills should consume this repo's domain documentation when exploring the codebase.
This page records layout and steps; normative wording lives in `AGENTS.md` 「契约索引」 and is
cited here by rule name only (「约束唯一出处」).

**Layout: single-context** — one root `GLOSSARY.md` (the glossary) plus `docs/adr/` (the decision
records). There is no `GLOSSARY-MAP.md`, and no per-prototype `GLOSSARY.md`.

Why single-context, from the state of this repo: prototypes share nothing at runtime and never
reference each other (「原型隔离」), while the vocabulary that actually needs to be unified is
project-level and already defined in `AGENTS.md` — GugaFramework, Template, 原型目录骨架, 三个观测口,
机制切片, 活人感测试, 垂直切片. Each prototype's own design conclusions and measured numbers live in
`docs/prototypes/<prototype>/` (「原型细节只进原型夹」、「原型文档夹」). Giving each of the nine prototypes its own
`GLOSSARY.md` would copy the same block of words nine times.

## Before exploring, read these

- **`GLOSSARY.md`** at the repo root, or
- **`GLOSSARY-MAP.md`** at the repo root if it exists: it points at one `GLOSSARY.md` per context. Read each one relevant to the topic.
- **`docs/adr/`**: read ADRs that touch the area you're about to work in

If any of these files don't exist, **proceed silently**. Don't flag their absence; don't suggest creating them upfront. The `/domain-modeling` skill (reached via `/grill-with-docs` and `/improve-codebase-architecture`) creates them lazily when terms or decisions actually get resolved.

"Not yet" is the current state here, and it is the normal one.

## File structure

```
/
├── GLOSSARY.md
├── docs/adr/
│   ├── 0001-<slug>.md
│   └── 0002-<slug>.md
└── src/
```

仓库地图（内容去处表、目录结构、公共层目录与文档层的对应）见 `docs/仓库地图.md`。

## Use the glossary's vocabulary

When your output names a domain concept (in an issue title, a refactor proposal, a hypothesis, a test name), use the term as defined in `GLOSSARY.md`. Don't drift to synonyms the glossary explicitly avoids.

If the concept you need isn't in the glossary yet, that's a signal: either you're inventing language the project doesn't use (reconsider) or there's a real gap (note it for `/domain-modeling`).

The two tests for "is this a real term" are in 「禁自造术语」.

Time wording follows 「时间基准」: seconds / minutes / milliseconds only, steps ÷ 10 = seconds.

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than silently overriding:

> _Contradicts ADR-0007 (event-sourced orders), but worth reopening because…_

Name the ADR in the output of the skill that owns this page (`to-spec` / `to-tickets` / proposal
skills), then wait for the user's ruling — 「出入只报告」: the AI doesn't pick a side or smooth the
difference over.
