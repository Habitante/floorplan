# Describe this project for FloorPlan

FloorPlan shows a repository as a map: one rectangle per file, nested in its folders. A project description tells it what the parts of the project are for, so the human sees where each part lives and why the parts coexist. You write it; FloorPlan checks it against the files on every scan and shows anything that no longer matches as stale.

The description supplements the project's own documents. It does not copy their plans: it says where things are, what they are for and where they are heading, and points at the documents and lines that say so.

## Before you write

1. Call `get_description` for the project. If a description exists, start from it: keep its system ids, and change only what the project changed.
2. Read what the project says about itself: the README, architecture and design documents, the roadmap. Note which claims come from them.
3. Look at the map: `summary` of the project gives its regions and sizes; `summary` of a folder gives its largest parts and recent commits.
4. Open files only where the documents leave a question.

Never edit the project, and do not run its code to answer a question. The description lives outside the project; `describe` saves it there.

## What to write

JSON, format version 1:

```json
{
  "floorplan": 1,
  "project": "folder name",
  "written": { "at": "YYYY-MM-DD", "by": "your name", "head": "abbreviated HEAD", "from": ["README.md"] },
  "summary": "Two or three sentences: what the project is, and the state it is in.",
  "systems": [
    {
      "id": "kebab-case-id",
      "name": "What the human calls it",
      "code": "UI",
      "role": "One or two sentences: what it does and how it relates to the others.",
      "status": "active",
      "basis": "declared",
      "paths": ["src/ui/**", "tests/test_ui*.py"],
      "docs": ["docs/ARCHITECTURE.md:40-58"]
    }
  ],
  "direction": [
    { "text": "Where the project is heading, in a sentence.", "systems": ["kebab-case-id"], "basis": "declared", "docs": ["docs/ROADMAP.md:12-30"] }
  ],
  "findings": [
    { "title": "Short title", "text": "What you found, with the evidence in paths.", "basis": "observed", "paths": ["docs/ROADMAP.md:18", "src/app.py"] }
  ]
}
```

**Systems** are the parts a human would name when explaining the project: generations of code during a migration, an engine and its tools, a client and a server. Three to five work best; the map gives five systems their own colours, and a sixth or later shares a neutral. Not every file needs a system: leave out documents, assets and one-off scripts unless they belong to a part.

**Codes** have one to three characters, printed on the map where a region belongs to the system. Make them easy to tell apart and easy to remember.

**Paths** are anchors relative to the project root: a file, a folder (which covers everything beneath it), or a glob where `*` stays within a folder name and `**` crosses folders. Where anchors of different systems overlap, the most specific wins: an exact file beats a folder, and a longer literal prefix beats a shorter one. So `screens/**` in one system and `screens/trace.gd` in another put that one file in the second.

**References** in `docs` and in findings' `paths` name files or folders, optionally with lines: `docs/ROADMAP.md:18-19`. The human opens them at those lines.

**Basis** says where a claim comes from: `observed` in the files themselves, `declared` in the project's documents, or `inferred`, your reading. Say `inferred` whenever you are interpreting.

**Status** is a word or two: active, retained, foundation, prototype, shared, deprecated.

**Findings** are things the human should know and would not see on the map: a document that contradicts the code, a gap the documents admit, a part that looks dead but is kept on purpose. Give each one the paths that show it. A size or an old date alone is not a finding.

## Save and check

Call `describe` with the whole description as a JSON string. FloorPlan checks it against a fresh scan and replies with:

- `problems`: what makes it unreadable, such as a wrong version or two systems with one id or code. It is not saved until they are fixed.
- `stale`: anchors and references that name nothing in the project. Fix them and call `describe` again.
- `systems`: the files and lines each system holds. A system with no files has anchors that miss.
- `unnamed`: the largest code that no system names. Decide for each whether it belongs to a system, deserves its own, or is fine left out.

Open windows on the project show the description at once: the Systems fill colours each file by its system, and the side panel lists the systems, the direction and the findings.

## Keeping it current

Keep the description current without being asked: the human relies on the map being true. Refresh it:

- when a project's `summary` says it is missing or out of date (`descriptionNote`);
- at the end of any task of yours that added, moved, renamed or removed a part's files, such as a new folder of code, a module moved elsewhere or a system retired;
- when a role, a status or the direction no longer matches the project.

Start from `get_description`, keep the ids and codes, change only what changed, update `written`, and mention the refresh in a line when you hand over. Rewrite a description wholesale only when the human asks.

When you are done, tell the human in a few lines what the systems are and anything you found, and suggest picking Systems in the map's Fill to see them.
