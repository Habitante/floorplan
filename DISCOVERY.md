# Discovery and decisions

Research behind [DESIGN.md](DESIGN.md) and a dated log of the decisions that shaped it. DESIGN.md is the current contract; this file explains why.

## The problem

A software architect working through coding agents, often on several large projects at once, loses the picture of each project when switching. Getting it back means asking an agent basic questions about one's own project, which is slow, leaves no persistent understanding, and turns an architect into a client. The first decision on returning is what kind of work the project needs (feature, documentation, architecture, audit, tests), and that needs the parts, direction, documentation and health in view.

What the owner needs to feel oriented comes from low-level programming: code plus data. Execution (startup, loops, systems, async hand-offs), data (structures, owners, where state moves) and resources (where the memory budget goes). Size and colour of regions are read instantly, before any label, which is why SpaceSniffer is the reference. Text spends attention: definitions belong behind an info control, not on screen.

Constraints from discussion: projects vary widely (games, AI, tools; many engines and languages); opening a project must be quick with no setup; a professional interface usable by others; single-branch work, with worktrees considered harmful to the big picture; returning after absence and supervising live work should use the same view.

## Case study: a private Godot game

A private Godot game, inspected read-only; other agents work in its checkout, so every figure is a point-in-time observation. Its name, layout and contents stay out of this public repository.

Observations that shaped the design:

- Its documents describe generations of code that live side by side while it migrates, and a document can contradict the code: a roadmap may call a part missing and later describe it as built. A recent modification date would not reveal this.
- Keeping old code that is still the reference for a migration is a deliberate decision. Large or inactive areas must not be labelled dead code.
- Recent commits touch design documents, runtime scripts, art-pipeline scripts and generated assets together.
- Conventions that cross folders, such as names an art pipeline creates and scripts look up, are invisible to import graphs.
- A byte map and a line map emphasise very different parts of a project; neither is a judgement of waste.
- Tests on disk, tests that import a file, passing tests and measured coverage are different facts.
## Reference projects

Shallow clones in `ref/` (provenance in [ref/README.md](ref/README.md)), inspected from source without running them.

- **Repo Atlas** (MIT): a single HTML file with a byte-weighted squarified treemap, import parsing for Python, JS/TS and C-family files, and Git overlays from imported history. No GDScript support; layout is not position-stable. The closest visual baseline.
- **GRASP** (Elastic License 2.0, not open source): broad overlays, CLI, watcher and timeline. Its parser classifies neither `.gd`, `.tscn`, `.tres` nor `.gdshader` as code or text, and its "test coverage" links tests by imports and names rather than measuring runs.
- **Agenttrail** (MIT): components declared in an agent-maintained `PLAN.md` with globs and links, plus file-watched activity. A useful model for small agent-maintained metadata; it adds its own convention and initialisation edits project files. Windows validation was pending.
- **An earlier, agent-centric attempt** by the owner: worth keeping are intent and trajectory attached to project regions, and the separation of observed facts from agent claims. Its dashboard deliberately excluded a file view, which left the owner blind to the repository.

## Decision log

### 15 October 2026: direction (discovery with Sol)

- Physical facts first, meaning on top: an immediate map from files and Git, with subsystem roles, migration boundaries and documentation claims layered on the same places.
- Treemaps for containment and footprint only; other relationships get graphs and flow schematics. Every leaf carries a price tag with an explicit unit.
- A shared board: human and agent point at the same entities and discuss proposals against the current code.
- A small agent-maintained project description is acceptable as long as basic browsing works without it.
- Lean interface: no persistent instructions or disclaimers; definitions behind an info control; glyphs instead of repeated unit words.
- Modification-date comparison is accepted as a first documentation-review signal, provided it exposes its evidence and claims no more than "review suggested".

Sol's interactive concept (seven folder boxes, a system grid and a code + data card grid, all hand-filled) is kept locally, outside the repository.

### 15 October 2026: design review (Claude)

- The concept established a visual vocabulary (price tags, lenses, glyphs, info control) but had not tested the SpaceSniffer premise: its areas did not follow size, and the code + data view had no edges. A deep treemap of the real files became the first trial.
- Static analysis cannot recover execution flow and data ownership across GDScript, C#, Python, Dart and C at reasonable cost; coding agents can. So agents write meaning, Map validates it against the scan, and Map never calls a model. That keeps it agent-agnostic, credential-free and token-free, and the description doubles as an agent briefing.
- Agent access moved forward to milestone 2: referring to "this" and pointing instead of writing prose are the cheapest, most valuable parts of the shared board.
- "What changed" is a range on the history strip, which serves both return-after-absence and live supervision without an acknowledgement workflow.

### Stack

15 October 2026. The owner asked for a self-contained executable in the spirit of SpaceSniffer, and for the stack that implements this most leanly with a good interface, not the one matching personal preference. Their projects span Godot (GDScript, C#), Flutter, React with TypeScript, WPF, C and Python.

Chosen: a C# executable hosting a TypeScript front end in WebView2. The map is the same canvas code in any stack; most of the rest is text (code, diffs, documents, tooltips), where web technology is far quicker to do well, and the explored visual language was already HTML/CSS. C# keeps the core readable to the owner, scans quickly, publishes as one file and has an official MCP SDK.

Set aside:

- **Electron + React:** one language, but a ~150 MB folder and process-model ceremony for a canvas tool.
- **Avalonia (all C#):** the runner-up; one language and a native executable, but slower to make code, diff and document views good.
- **Flutter:** strong canvas and hot reload; ships as a folder, puts the core in Dart, thinner developer-tool components.
- **Tauri:** smallest executable with the same front end; puts the core in Rust.
- **Dear ImGui:** very lean; weak for rich text, diffs and a polished interface for others.

### 15 October 2026: milestone 1 trial

Built and run on the case study, read-only. Without any semantic description:

- Under Lines with the latest 40 commits, the new code and its tests are active while the shared core and the retained code are cold. Widening to 207 commits warms the documents but leaves the retained code cold, consistent with keeping it as a reference.
- Under Bytes, the art (305 MiB) dominates and all code and documentation fit in a corner.
- Selecting one recent commit shows a single change reaching design and pipeline documents, a Blender script, a scene script, a shader and a test.
- Another agent edited two files 17 seconds after a scan, and the cross-check caught it: a scan is an observation of a live checkout.

Implementation choices made during the trial: Lines counts code plus documentation; a Kind lens with three hues; merges excluded; activity follows paths. Colours were checked with a palette validator: the activity ramp passes monotone lightness, step gaps and surface contrast in both themes, and the three kind hues pass all-pairs colour-vision checks (the light-theme green is under 3:1 contrast, so tiles carry labels and the kind is given in text).

Line counts then dropped the "L" suffix for Font Awesome's `bars-staggered` glyph, present in the owner's Pro font and in the free solid font the app bundles.

### 15 October 2026: roadmap and skeletons

After a first test ride, the owner asked for the roadmap in writing and proposed condensing source files into skeletons (key sections, data, method names, later expandable and related) so the tool teaches a project as well as reviewing it. Adopted as milestone 3, before meaning: it is the treemap continuing into files, and the meaning layer needs symbol-level anchors (for example a data structure's owner) that only parsing provides. Tree-sitter was chosen over per-language tooling because one engine covers colouring, skeletons and symbol identities across all the owner's languages. The first reader pane uses CodeMirror with stock language modes; tree-sitter arrives with the skeletons.

### Name

15 October 2026. "map" is a working codename: unsearchable, and in the agent space "repo map" already names Aider's codebase summary. Rename before the first public push (GitHub redirects renamed repositories, but the name is also in the executable, data folder, namespaces and docs). Checked candidates: **Plano** is taken by a popular AI-agent infrastructure project (katanemo/plano); **Mirador** is an established open-source image viewer; **Floorplan** had no codebase-tool collision and was recommended. In a separate session Sol proposed CodePerch, RepoRelief, Repovium, RepoForma, RepoBearings, SourceCairn, RepoReins and RepoReentry (no exact GitHub, npm or NuGet collisions found then), and the positioning that frames the launch: "Stay the architect of your own codebase" and "Ever feel lost in your own repo?" Claude's view: the tagline carries the pain, the name should stay short; Floorplan pairs naturally with the architect tagline, CodePerch is the runner-up, long Repo- compounds are best avoided. Undecided.

15 October 2026: FloorPlan, chosen by the owner (casing FloorPlan; `floorplan` for the executable and repository).
