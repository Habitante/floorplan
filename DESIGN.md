# FloorPlan: design

The current product and architecture contract. Implementation status is in [README.md](README.md); research and the reasons behind decisions are in [DISCOVERY.md](DISCOVERY.md).

## Outcome

Help a software architect return to a complex project, recover a clear picture of its code and data, and choose what kind of work it needs next (feature, documentation, architecture, audit, tests) while working with coding agents. The same board stays open on a second monitor during active development.

Success: the human understands the current system and asks better questions with less prose. Opening a project gives something useful immediately, with no setup, no semantic metadata and no AI.

First case study: a private Godot game. Its retained legacy code and the conventions that cross its folders show why file sizes and import graphs alone cannot explain a project.

## Principles

- **The repository is the primary object.** Agents annotate the map; the map does not become an agent dashboard.
- **Physical facts first, meaning on top.** Inventory and Git work on any folder. Systems, flows, data ownership and intent are layered on and tied to paths.
- **Agents interpret; FloorPlan checks.** FloorPlan does not call AI models. Interpretation comes from the coding agent the user already runs (Claude Code, Codex, …) through a project description and MCP. FloorPlan verifies every claim's anchors against the current scan and shows broken ones as stale.
- **Evidence stays inspectable.** Each nontrivial claim carries its basis (observed, declared or inferred) and links to files, commits, documents or measurements. Unknown is distinct from zero. Size or inactivity is never a quality verdict.
- **Self-contained.** One executable, like SpaceSniffer: no installer, no runtime to install, nothing written into the inspected project.

## Product contract

- **Code:** entry points, startup, loops, systems, calls and async hand-offs.
- **Data:** important structures, owners, readers/writers, lifetime, movement and persistence.
- **Costs:** file, source and document sizes; complexity and runtime RAM/VRAM when evidence exists. Each keeps its own unit.
- **Direction:** current phase, intended changes, related documentation and open questions.
- **Shared board:** human and agent point at the same entities, trace paths and discuss proposals against the current code.

## Visual and interaction rules

**The map is a deep treemap.** Nested down to individual files, as in SpaceSniffer: thousands of leaves, readable by size and colour before any label. Drill in and out with spatial continuity. Labels collapse with zoom instead of shrinking into noise. Tiny and zero-weight files stay reachable through drill-down or a list. Structure is drawn without hue: gutters narrow with depth (6 px between regions, the folders one level below the focus; 3 px between subfolders; 1 px between files), regions get a taller label strip, and folder frames sit darker than the files on them.

**Area and fill answer separate questions.** A footprint selector controls area: **Lines** (physical lines in code, test and documentation files) or **Bytes** (size on disk; the only footprint where data, scenes and assets take area). A lens controls fill: **Activity**, **Age** or **Kind**. Switching lens keeps geometry, focus and selection. Changing footprint or receiving file changes may relayout; keep ordering deterministic and movement minimal.

**Colour has a grammar, and its values are measured.** Warm means change: one ramp from red through orange to yellow serves Activity and Age and nothing else, brighter with more in the dark theme and darker with more in the light one. Hue says what a file is; lightness says how much. The selected lens owns the strong colours, and what a warm lens leaves at zero keeps a dim, low-chroma tint of its kind, like a basemap under a thematic layer: the map never turns uniformly grey, and a lit tile needs no legend. A project without history is hatched, so unknown never looks like inactive. Activity steps: 0, 1, 2–3, 4–7, 8–15, 16+ commits. Age steps: within a day, a week, 30 days, 90 days, a year, then older. A file without a commit up to the range's end is "older" only when the history was cut short, no loaded commit touches it, and the oldest loaded commit is more than a year before the range's end; otherwise its age is unknown (it came later, was never committed, or its last commit was not loaded) and it is hatched. Kind uses three hues and a neutral: code blue, tests teal, assets green, and documentation as the neutral that contrasts most with the background; data and other files share a muted neutral. Tests are code that common path conventions mark as tests (a `test`, `tests`, `__tests__`, `spec` or `specs` folder, or names such as `test_x.py`, `x_test.go`, `x.test.ts`, `XTests.cs`); their lines count in the Lines area. Overlays are outlines in hues no fill uses: violet for uncommitted changes, raspberry for agents' pointers, ink for the selection, and ink hairlines, fainter for weaker ties, for the files that change with it. Violet is the one colour for "not committed yet", and shape tells the kind: an outline for a modified file, a filled corner as well for a new one (a rename is new at its new path), and counts in the region headers (`+2 ~1 −1`), where deleted files, which take no area, show too. A new file pulses once when a refresh brings it. Dashed outlines are kept for proposals. The interface names these changes with three words, New, Modified and Deleted (`+ ~ −` in headers, N, M and D in lists), all "uncommitted"; Git's own terms (untracked, added, renamed) stay in the scan model and in agents' replies. Every value is checked per theme for monotone lightness, step separation and colour-vision separation ([DISCOVERY.md](DISCOVERY.md)).

**Activity is a range on the history strip.** The strip along the bottom is a commit timeline. Dragging a range sets which commits the activity lens counts, so one control covers "this week", "since I was last here" and "the last commit". Default: the commits since the human was last there (1e), else the latest 40. Age is measured from the range's newest commit, not from today: a project keeps its own pace while work pauses, and moving the end shows how recently each file had changed at that point. Uncommitted edits stay with the violet outlines. Selecting a commit keeps its files at full strength and dims the rest; drilling down reaches the diff.

**Other relationships get other drawings.** Treemap for containment and footprint, a graph for system relationships, directed schematics for execution and data flow. Views share entity identities and selection. Graph nodes carry a small cost marker rather than distorting the layout.

**Every leaf has a price tag:** lines, bytes, file count or measured complexity, with detail on inspection. Line counts carry the lines glyph (Font Awesome `bars-staggered`) instead of a unit word; sizes keep their units. Disk bytes, source lines and resident memory never share a unit.

**Lean daily interface:** project and location, active view and metric, the map, a contextual inspector and the history strip. Glyphs replace repeated unit words, with full accessible names. Colour scales, definitions and caveats live behind an info control or tooltip. No persistent instructions, disclaimers or overall health scores.

**Overlays never erase measurements.** Selection, agent pointers and proposals use outlines and overlays so the fill underneath stays readable; agents' captions sit beside the map, not over it. A proposal is always distinguishable from existing code.

## Stack

| Part | Choice |
|---|---|
| Distribution | One self-contained Windows `.exe` (.NET single-file publish) with the front end embedded. |
| Core | C# on .NET 10 (LTS): inventory, Git, aggregation, project model, file watching. |
| Window | A minimal WinForms host for WebView2, the Edge runtime that ships with Windows 11. |
| Front end | TypeScript. The map is drawn on Canvas 2D with our own squarified layout. Panels use Preact, with signals for shared state such as selection. |
| Text views | CodeMirror 6 for files and diffs, markdown-it for documents. ELK.js for system and flow layout when those views arrive. |
| Bridge | WebView2 JSON messages between front end and core. No HTTP server, no listening port. |
| Agent access | MCP through the official C# SDK (`ModelContextProtocol.Core`), stdio transport. |
| Git | The installed `git` executable. |
| Persistence | Versioned JSON under `%LOCALAPPDATA%\FloorPlan`, which also holds WebView2's user data. No database. |
| Build | `dotnet` for the core and executable; npm and Vite for the front end at build time only. |
| Checks | xUnit with fixture repositories for core behaviour, Vitest for layout, activity and board logic, smoke checks of the real window. |

Web technology carries the text-heavy parts (code, diffs, documents, tooltips); C# keeps the core readable to the owner, fast and publishable as one file. Accepted costs: two languages and a message bridge, a Node toolchain at build time, Windows first, and an executable of tens of megabytes. A macOS/Linux build replaces only the window host. The alternatives considered are in [DISCOVERY.md](DISCOVERY.md#stack).

Use the reference projects under `ref/` for ideas or small algorithms, keeping licences and attribution. Do not adopt one wholesale.

## Architecture

```text
  project files + Git          project description (agent-written JSON, planned)
           |                                  |
           +---------> FloorPlan.Core <-------+
                inventory, Git, aggregation, model,
                anchor validation, file watching
                             |
      +----------------------+-----------------------+
      |                      |                       |
   floorplan.exe        floorplan.exe scan      floorplan.exe mcp  <--- coding agent
   window mode          JSON to stdout          stdio MCP server        (Claude Code, Codex)
   WebView2 host +      (tests, agents)               |
   TypeScript UI                                      |
      ^                                               |
      +---------------- named pipe -------------------+
             selection, pointers, proposals
```

- `FloorPlan.Core` references no UI, WebView2 or MCP packages, so the window, headless scans, tests and the MCP server share it unchanged.
- The front end is embedded in the executable and served to WebView2 from `https://floorplan.invalid/` with a restrictive content security policy. Navigation elsewhere and new windows are refused.
- The front end has no filesystem or process access. The bridge exposes a small set of validated operations (open project, rescan, read file, read diff, report the view). Every repository path, from the front end or from an agent, must name an entry of the last scan inside the project root.
- Scans and Git queries run off the UI thread with bounded concurrency, report progress and can be cancelled.
- Layout calculation is separate from drawing and interaction.

```text
src/FloorPlan.Core/           Inventory, Git parsing, tree aggregation, scan model, the board's model and channel
src/FloorPlan.App/            floorplan.exe: window host, bridge, MCP server, scan and smoke modes
web/                          Front end: canvas map, panels, layout, activity and pointers
tests/FloorPlan.Core.Tests/   Fixture repositories and behaviour checks
examples/                     Example project descriptions (planned)
```

### Inventory and history

For a Git project, inventory the tracked files present in the working tree and the untracked files Git does not ignore, outlined as new, since agents create files long before they commit them. The human can hide untracked files per project. For a plain folder, inventory regular files with configurable exclusions. Do not traverse symlinks, junctions or nested repositories; show them as boundaries.

Record bytes, type, modification time and availability. Count physical lines only for recognised text and source types, including GDScript, C#, Python, Dart, C/C++ and Godot shaders. Binary, oversized, unreadable and unclassified files keep explicit unavailable measurements. Parent totals derive from descendants within the stated scope, without double counting.

Read the current checkout and its reachable history, up to the latest 1,000 commits; do not merge branches or worktrees into one history. The activity lens counts the commits in the selected range that touch a file or region, each commit once per region. Merge commits are excluded. Activity follows paths: a rename counts as a delete and an add, so a renamed file's earlier commits stay with its old path. Zero activity requires a complete, successful sample; a failed query is unavailable, not zero.

Git runs read-only with argument arrays: no shell interpolation, optional locks, repository-configured helpers, external diff drivers or text conversion. Never check out historical revisions. Load file content and diffs on demand, with limits for large or binary content. Render repository text as data.

A scan records root, HEAD, scope and time. The checkout can change mid-scan: detect relevant changes and schedule a refresh rather than implying an atomic snapshot. Cache, preferences and the HEAD each project was last seen at live in FloorPlan's data folder, keyed by canonical project path. Windows and agents' servers share them, so each change re-reads them under a lock that every FloorPlan process takes. Nothing is written to the inspected project.

### Meaning: the project description

An agent writes a versioned JSON description of the project; FloorPlan loads, validates and draws it. It can name:

- **Systems** with path or glob anchors and linked documentation or roadmap sections.
- **Data structures** with owner, readers/writers and lifetime, anchored to files or symbols.
- **Flows** for execution and data, as directed edges between systems and structures.
- **Direction:** migration boundaries, current phase, open questions.
- **Findings** with evidence, such as a document contradicting the code.

Every entry carries its basis and source anchors. FloorPlan checks anchors on each scan; missing paths are shown as stale, never silently dropped. Physical browsing never requires a description.

The description supplements the project's own documents rather than copying their plans, and doubles as a compact briefing for agents at the start of a session, so human and agent work from one model. A FloorPlan-owned prompt, usable as a Claude Code skill or as plain instructions for any agent, produces and refreshes it. It lives outside the project, in FloorPlan's own local data. Settle the filename and schema with the first working consumer, not by designing an ontology up front.

Documentation signals:

- **Observed links:** repository paths and symbol names mentioned inside documents link those documents to code automatically.
- **Review suggested:** linked code changed after the document's last commit. The signal shows its dates and links; it does not claim the document is wrong.
- **Contradictions** need reading, so they arrive as agent findings with evidence. A roadmap that says a part is missing and later describes it as built is the reference example.

### The shared board

`floorplan.exe mcp [folder]` is a stdio MCP server that the agent launches; one registration serves every project. Each window serves its own named pipe (current user only, one JSON line each way per call) and says which project it shows, so the server finds the open windows by listing the pipes. A call goes to the project it names (a folder name such as `my-game`, or a path), else to the project an absolute path points into, else to the window the human used last: agents often work in one folder while talking about another. Every reply names its project. The folder is where the agent works: the base of its relative paths and the default for `summary` and `open` when no window is involved. Tools:

- `get_selection(project?)`: what the human is looking at, so "this" has a referent. The selected file or folder with its measurements and last commit, when it was selected and, if HEAD has moved since, the HEAD at that time; the file in the reader with the lines in view, the selected lines and the selected text; the zoomed folder, the selected commit and the history range; the last five selections; how long since the human's own input in the window (not view changes, which refreshes and agents make too), with `stale` after ten minutes; what landed while the human was away (`sinceLastSeen`), while the window shows it; and the other open windows. In a diff, line numbers count in the newer version, named in the reply, and removed lines have none, so the selected text is what settles a selection. The human selects, then types, and by the time the agent asks the selection may have moved.
- `show(paths, caption, agent?, project?, reveal?)`: outlines files and folders on the map with a short caption instead of paragraphs of prose; `path:480-492` points at lines, which the reader highlights. One pointer per agent, named by the agent or after its MCP client, stays until the agent points again or the human dismisses it; every window on the project draws it and the reply counts them. Paths resolve against the scan (absolute, or relative to the root or the agent's folder). A path that exists but is not on the map (ignored by Git, untracked) outlines its nearest folder on the map and is reported with its reason; a path that does not exist is reported as unknown. `reveal` also raises the window, shows the paths on the map and opens the reader at the lines, for when the human asked to be shown. Without a window, `show` reports that nobody is looking, so the agent falls back to prose. At the end of a task, pointing at the files it changed is the agent's visual handoff.
- `summary(path?, project?)`: footprint, composition and activity of a folder or file from the model the human sees, counted over the human's history range; without a window, from a fresh read-only scan over the latest 40 commits. A folder's line total is `codeAndDocLines`, the Lines area; its composition adds data. File and folder replies carry `lastCommit`, measured as the Age lens measures it, so agents can talk about what the human sees under Age. A file's reply adds what changes with it and its tests by name (milestone 3). A summary of the whole project also carries `sinceLastSeen`: the window's briefing, or without a window, what landed since the human last used one on the project.
- `open(project?)`: raises the window showing a project, or starts one and waits until it has scanned. Neither takes the keyboard from the human, and a started window outlives the agent's session. Only for when the human wants to see something.

The server's instructions describe the map as the human sees it (area, fill, colours, outlines, where captions sit), so agents can talk about it in its own terms, and say how to treat stale selections, diffs and project choice. The view reports its theme.

Pointers are raspberry outlines with a hairline of the surface colour inside, checked in both themes against the warm ramp, the kind hues and tints, and the uncommitted violet. Captions sit in the row under the map, which has room for a caption's start. Clicking it opens the whole note over the foot of the map until closed, with the agent, the time and the paths as rows: a file reads at the lines pointed at, a folder is selected, and a path off the map shows what the agent named without its folder's measure. Per-agent colours wait until two agents point at once in practice; a history of pointers waits for a need.

The agent registers the server once, for example `claude mcp add --scope user floorplan -- <path>\floorplan.exe mcp`. User and local scopes keep the entry in the user's own configuration; project scope would write `.mcp.json` into the inspected repository.

Later, `propose(overlay)` adds a proposed change as outlined regions and edges, distinct from current code. After implementation the resulting diff lands in the same place.

## Milestones

Built in this order. Sizes are rough: S is about a session, M a couple, L several. [README.md](README.md) records what is done. On 17 October 2026 the order changed and the milestones were renumbered ([DISCOVERY.md](DISCOVERY.md)): earlier log entries use the old numbers, where 3 is now 5, 4 is split into 4 and 6, and 5 is now 7.

| Step | Content | Size |
|---|---|---|
| 1 | Real repository map: the trial, plus the steps below | |
| 1a | Live refresh; uncommitted changes outlined on the map and shown as a pending entry on the history strip | S |
| 1b | Reader pane: read-only source view with syntax colouring (CodeMirror), commit and working-change diffs; opens on click | M |
| 1c | Remembered zoom and range per project; optional untracked scope | S |
| 1d | Visual tweaks from the owner's use, collected as they come | S |
| 1e | Since I was last here: the history range opens at the last HEAD seen, with a one-line briefing | S |
| 1f | File finder (Ctrl+P) | S |
| 1g | A second, much larger case study; performance budgets set on both | S |
| 2 | Shared pointer (MCP) | S–M |
| 3 | Files that change together, with tests paired to code | S–M |
| 4 | Meaning on paths: the agent-written description, drawn as named regions | M |
| 5 | Inside files: skeletons and symbols | M–L |
| 6 | Meaning on symbols: data, flows and documentation signals | L |
| 7 | Proposals | M |
| — | Before a first release: automated build, release executable | S |

### 1. Real repository map

`floorplan.exe` opens a folder and draws the deep treemap of its actual files: lines or bytes as area, activity, age or kind as fill, the history strip with range selection, and an inspector for files, commits and diffs. `floorplan.exe scan` emits the same model as JSON. Includes refresh when files change, remembered project and view state, clear scan status, and graceful handling of plain folders and a missing Git executable.

Clicking a file reads it: the reader takes the side panel's place at the same width (drag the edge to resize), so the map never reflows. Within a selected commit or the uncommitted changes, clicking a file reads its diff.

Acceptance:

- Open the case study without modifying it. Selected totals and commit/diff results match independent filesystem and Git queries for the same scope.
- Switching lens keeps selection and placement. Switching footprint updates area and units coherently.
- Bad paths, deleted files, binary files and failed Git queries produce inspectable partial results, not fabricated zeros or a broken window.
- Scanning never freezes interaction. Time to first useful map, full scan time and idle memory are recorded on the case study, with budgets set from that baseline.
- Fixtures cover paths with spaces and Unicode, Windows path behaviour, parent aggregation, renames and commit counts.

#### 1e. Since I was last here

Coming back to a project is the problem FloorPlan exists for, so the history range starts where the human left off, whether they were away for a weekend or for five minutes while agents worked. FloorPlan remembers, per project, the HEAD that was current when the human was last present in a window on it. Presence is the human's own input: moving the pointer over the window, clicking, scrolling, typing, or bringing the window forward. View changes do not count, since refreshes and agents' reveals make them too.

Reopening the project opens the range at the first commit after that HEAD, and a chip in the history row says what landed since: when the human was last there, the number of commits, the top-level regions they touched (files at the top level count as one region), and who made them. Agents are named only where commits name them in co-author lines, since agents commit under the owner's name. Commits count as `git log <seen>..HEAD` counts them, without merges. The tooltip gives the counts per region and per person: the ten with most commits in each list, and how many more, since a large project brings well over a hundred authors.

An open window is a live view, and nothing in it switches by itself: updates never override what the human is looking at, and what an agent shows appears at once. When the human comes back to an open window after five minutes or more without input and commits have landed meanwhile, the chip appears and offers the commits since. The range moves only when the human clicks it, and a second click puts back the range before. A range since the human was last here keeps its start as commits land, so they join it. The briefing records what landed while the human was away and does not change after it is shown. If the remembered commit is gone (a rebase) or older than the loaded history, the briefing says so instead of guessing. Agents read the same briefing as `sinceLastSeen` in their replies rather than through a new tool, so they can brief the human unasked.

Acceptance: on the case study, after commits land while FloorPlan is closed, reopening shows exactly those commits in the range, with a briefing whose counts match `git log <last HEAD>..HEAD`; an agent reads the same through MCP; a window in use is never reset.

#### 1f. File finder

Ctrl+P, or the magnifier in the top bar, opens a fuzzy finder over the project's files and folders, answering at typing speed. A term matches when its characters appear in the path in order, case and accents aside; a whole name, the term found whole, runs that start a folder or word, and matches in the name rather than the folders rank first, and spaces separate terms that must all match. Among equal matches, those with area under the current footprint come first. Rows carry the price tag and the uncommitted mark, and rows without area are faded. Choosing a folder zooms the map into it; choosing a file reads it in its folder, as clicking it does. Something without area under the footprint is marked on its nearest folder that has area, as uncommitted changes and pointers are. It is invoked, not persistent, so the daily interface stays lean.

Acceptance: any file of the case study is reachable with Ctrl+P and a few characters, and the map shows it selected.

#### 1g. A second case study and the budgets

Everything so far is tuned on the Godot game, which is young and fits under the 1,000-commit cap. A second, very different case study tests the limits before they set into habits: a large, old open-source repository with far more history than FloorPlan reads. It is the Flutter SDK already on the owner's machine (15,352 files, 87,655 commits since 2015, Dart and C++), inspected read-only like the first. It checks the commit cap and Age's unknown ages, the test conventions of another ecosystem, scan and layout speed at that size, and the colours on a different shape of project. Milestone 1's performance budgets are then set from both baselines.

Acceptance: scan time, first map and memory recorded on both projects, with budgets in README; whatever breaks or misleads at that size is fixed or recorded.

### 2. Shared pointer

`floorplan.exe mcp` with `get_selection`, `show`, `summary` and `open`, registered once with Claude Code for every project.

Acceptance: in a Claude Code session on the case study, the human selects a region and asks about "this", and the agent identifies it correctly. The agent highlights the files a planned change would touch, and the human sees them on the map.

### 3. Files that change together

Selecting a file shows the files that change in the same commits. They are read from the whole loaded history rather than the range, since a relation needs evidence. They appear in three places:

- **The reader:** a line of names with their shared commits, since selecting a file opens the reader.
- **The inspector:** rows, each with its evidence in a tooltip ("changed together in 6 of this file's 9 commits and 6 of its 7") and a count that leads to their newest shared commit.
- **The map:** ink hairlines, fainter for weaker ties, since the relation belongs to the selection and needs no new hue.

Agents' commits are broad, so a commit counts fully while it relates the file to up to ten others and in proportion beyond, and a sweep over thousands of files relates almost nothing. A pair needs two shared commits worth at least one. Partners rank by how much of both histories they share, so a file that changes in nearly every commit, such as a roadmap, ranks low with each. The relation is "changes with", never "depends on".

Tests paired with a code file are listed apart, and labelled as exactly that: pairing is not coverage. They pair by name, with the conventions the test kind uses (`test_x` and `x`, `x_test` and `x`, `x.test` and `x`, `XTests` and `X`, with the same extension; where several files share the name, the one sharing most folders), or by changing together. A test lists the code it is named for. Agents read the same from the `summary` of a file (`changesWith`, `pairedByName`).

Acceptance: on the case study, the pairs for a few files match a direct count from `git log`; a broad commit does not relate everything to everything; a code file's tests are found by name where the conventions allow.

### 4. Meaning on paths

The agent-written description for the case study, anchored to paths and globs only, drawn on the map as named regions: its systems, migration boundaries, current direction and findings, each with its basis and linked documents. It lives outside the project, in FloorPlan's local data, found through a per-project setting, and is produced by a FloorPlan-owned prompt; it doubles as a briefing for agents. Anchors are checked on every scan. How the regions are drawn is settled with the first working description, within the colour grammar. This comes before skeletons because "where is what, and why" often cuts across folders, and it needs no parsing.

Acceptance: the user can locate the case study's main systems on the map and explain why they coexist. A broken anchor shows as stale after a rescan.

### 5. Inside files: skeletons and symbols

The map continues past a file: zooming into one shows its symbols (types, functions, data declarations, marked sections) as regions sized by lines, and the reader pane shows the skeleton as an outline whose bodies expand on demand. Parsing uses tree-sitter grammars and tag queries, read-only and on demand, so one engine provides colouring, skeletons and symbol identities. Symbols get stable IDs (path plus qualified name), which later milestones use as anchors. Relationships start with references by name and may later come from language servers or agent descriptions.

Acceptance: on the case study, a GDScript, a Python and a shader file show correct skeletons with line counts; a symbol's ID survives an unrelated edit to its file; the skeleton reads faster than the file for learning what it does.

### 6. Meaning on symbols

The description gains what needs symbol anchors: data structures with their owners, readers, writers and lifetime; a system view and a code/data flow schematic linked to the physical map; documentation signals (observed links, review suggested, contradictions as agent findings).

Acceptance: the user can trace one important state or resource path into its source files, and a contradiction between a document and the code arrives as a finding with its evidence.

### 7. Proposals

`propose` overlays and diffs landing in place. Exercise a migration in the case study: retained code, reusable logic, its destination, a missing piece and the linked documents.

Acceptance: both participants inspect the current and proposed flow, its costs and the unresolved dependency, then see the resulting diffs in the same place. Judge whether the board improved the next architectural question or decision.

## Deferred

Language-specific dependency parsing, automatic flow recovery, runtime RAM/VRAM ingestion, complexity and coverage metrics, a multi-project dashboard, a macOS/Linux host, code signing and release packaging all wait for a demonstrated need. The model must be able to hold their evidence without implementing them first.

The licence is MIT ([LICENSE](LICENSE)). Reference checkouts and the supplied Font Awesome Pro font stay local; a public build uses the bundled free icons.
