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

### 15 October 2026: step 2 from the agent's side (proposal)

A review by a Claude Code session that will be the shared pointer's first user, on the case study. Proposals, not yet decisions:

- **A stable "this".** The human selects, then types the question; by the time the agent calls `get_selection()`, the selection may have moved. Return it with when it was made and the HEAD it was made at, plus the last few selections, so "this" means what the human meant.
- **Pointers that stay and say who.** `show(ids, caption)` stays lit until replaced or dismissed, and the panel keeps a short list of recent pointers to step back through, like a conversation in pointers. Several agents can work in one checkout at once (the case study has two), so `show` takes the agent's name and each agent gets its own outline colour.
- **An answer when no window is open.** `show` reports that nobody is looking, so the agent falls back to prose instead of pointing at nothing.
- **The shape of a change.** At the end of a task, `show` on the files it touched (new, changed, moved) with one caption is a visual handoff: the human sees where the work landed instead of reading a file list.
- **Meaning before skeletons, tested cheaply.** The owner's stated pain is "where is what, and why". A first description anchored to paths and globs only (the case study's main parts and the conventions that cross its folders), drawn as regions, may answer it before symbol anchors exist. If it does, milestone 4 deserves to come before 3; if not, an afternoon is lost.
- **What the pointer won't do.** In the case study, the problems the owner found by playing were about feel, not files. The board serves the architecture conversation; it shouldn't try to judge feel.

### 15 October 2026: step 2 built (Claude)

The owner set step 1d aside as a running list of visual tweaks, so the shared pointer came next. From the proposals above, adopted because each is cheap and serves the acceptance test:

- **A stable "this":** `get_selection` returns the selection with when it was made, the HEAD at that time if it has moved since, and the last five selections. The reader adds the lines in view and any lines selected, so "this" can be a passage as well as a file.
- **Pointers that stay and say who:** one per agent, replaced by its next `show` or dismissed by the human, named by the agent or after its MCP client.
- **An answer when no window is open:** `get_selection` and `show` say that nobody is looking; `summary` scans the project itself.
- **The shape of a change:** a convention in the server's instructions (point at the files you changed at the end of a task), not another tool.

Deferred: per-agent outline colours, because the overlay palette already holds the selection, uncommitted changes and pointers, and a second pointer hue can wait until two agents point at once in practice; and a history of pointers.

Choices made while building:

- **Channel:** a named pipe per project root, current user only, one JSON line each way per call. The first window on a project serves it and a second takes over when the first closes. The MCP process looks for a window on its folder, then on each folder above it.
- **Paths:** whatever an agent writes (absolute, relative to the root or to its own folder, either slash) resolves only to entries of the current scan, through the same index that now guards the reader's file reads. Unknown paths are reported back.
- **Colour:** raspberry, `#c2257a` light and `#f566ab` dark, checked with the palette validator against the activity ramp, the kind hues and the uncommitted violet: CVD separation at least 9.3 and normal-vision separation at least 16.9 in both themes. Teal was rejected for falling below the chroma floor and sitting too close to the code blue. The dark value is above the categorical lightness band on purpose: it is an outline on a dark surface, not a fill.
- **Captions beside the map:** the first version laid a card over a corner of the map. At narrow width it covered the tile it pointed at, against "overlays never erase measurements", so captions moved to the row under the map.
- **Measurement:** a smoke run started while a FloorPlan window was open shared that window's WebView2 browser and GPU processes (same data folder). Memory then read about 300 MiB across 6 processes, and the first map looked about 200 ms faster. Smoke runs now use their own data folder, and the re-measured baseline matches the trial's.

"Meaning before skeletons" stays open. After the acceptance test, a path-only description of the case study decides whether milestone 4 comes before 3.

### 16 October 2026: step 2 in use (Claude)

The owner and a Claude Code session working from the owner's "house" repository ran the acceptance test and more on the case study: "this" resolved exactly, `show` replaced a file list, and `summary` matched Git and `wc`. Its findings, most important first, and what came of them:

- **One project per registration.** The agent lives in one repository and works across several, so a server tied to one project followed the wrong map. Now one server serves every window: each window has its own pipe and says which project it shows. A call goes to the project it names, else the one an absolute path points into, else the window the human used last, which is where "this" is. A name must be an open window's or an existing folder (beside the agent's folder counts); testing caught a bare name falling into the agent's own project.
- **Line numbers in diffs.** The extra visible line was the editor's empty line after a final newline, now dropped. Replies say which version the numbers count in, flag selections that touch removed lines, and carry the selected text, which settles the rest and saves the agent a read.
- **Two windows on one project.** Only the first window served the board, and nobody could tell which one that was. With a pipe per window, every window on the project draws a pointer and the reply counts them; there is no ownership to hand over.
- **What the human sees.** The server's instructions now describe the map (area, fill, colours, outlines, caption row) and the view reports its theme.
- **Opening or raising a window.** A new `open` tool, and `reveal` on `show`. Neither takes keyboard focus from the human, who is usually typing to the agent; a started window leaves the agent host's job, so it outlives the session.
- **Stale selections.** Replies carry the window's idle time and `stale` after ten minutes without the human doing anything in it: idleness, not the selection's age, since a file selected long ago and read since is still "this".
- **Ignored paths.** Paths that exist but are off the map come back as such, with the reason from `git check-ignore`, and their nearest folder on the map is outlined, since agents leave renders and reports there for the human.
- **Line totals.** A folder's total is now `codeAndDocLines`, so it no longer reads as a mismatch with a composition that includes data.
- **Wishes taken:** pointing at line ranges (`path:480-492`, highlighted in the reader) and the selected text in `get_selection`.

The board protocol is version 2; windows and servers of version 1 must be restarted.
