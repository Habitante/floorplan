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

**The map is a deep treemap.** Nested down to individual files, as in SpaceSniffer: thousands of leaves, readable by size and colour before any label. Drill in and out with spatial continuity. Labels collapse with zoom instead of shrinking into noise. Tiny and zero-weight files stay reachable through drill-down or a list.

**Area and fill answer separate questions.** A footprint selector controls area: **Lines** (physical lines in code and documentation files) or **Bytes** (size on disk; the only footprint where data, scenes and assets take area). A lens controls fill: **Activity** or **Kind**. Switching lens keeps geometry, focus and selection. Changing footprint or receiving file changes may relayout; keep ordering deterministic and movement minimal.

**Fill colours are measured, not picked.** Activity uses discrete steps (0, 1, 2–3, 4–7, 8–15, 16+ commits) on one orange ramp per theme, validated for monotone lightness and contrast against each surface. Zero is a neutral, and a project without history is hatched, so unknown never looks like inactive. Kind uses three categorical hues (code, docs, assets), the most that stay distinguishable when any two can be neighbours; data and other files share a neutral.

**Activity is a range on the history strip.** The strip along the bottom is a commit timeline. Dragging a range sets which commits the activity lens counts, so one control covers "this week", "since I was last here" and "the last commit". Default: the latest 40 commits. Selecting a commit keeps its files at full strength and dims the rest; drilling down reaches the diff.

**Other relationships get other drawings.** Treemap for containment and footprint, a graph for system relationships, directed schematics for execution and data flow. Views share entity identities and selection. Graph nodes carry a small cost marker rather than distorting the layout.

**Every leaf has a price tag:** lines, bytes, file count or measured complexity, with detail on inspection. Line counts carry the lines glyph (Font Awesome `bars-staggered`) instead of a unit word; sizes keep their units. Disk bytes, source lines and resident memory never share a unit.

**Lean daily interface:** project and location, active view and metric, the map, a contextual inspector and the history strip. Glyphs replace repeated unit words, with full accessible names. Colour scales, definitions and caveats live behind an info control or tooltip. No persistent instructions, disclaimers or overall health scores.

**Overlays never erase measurements.** Selection, agent pointers and proposals use outlines and overlays so the fill underneath stays readable. A proposal is always distinguishable from existing code.

## Stack

| Part | Choice |
|---|---|
| Distribution | One self-contained Windows `.exe` (.NET single-file publish) with the front end embedded. |
| Core | C# on .NET 10 (LTS): inventory, Git, aggregation, project model, file watching. |
| Window | A minimal WinForms host for WebView2, the Edge runtime that ships with Windows 11. |
| Front end | TypeScript. The map is drawn on Canvas 2D with our own squarified layout. Panels use Preact, with signals for shared state such as selection. |
| Text views | CodeMirror 6 for files and diffs, markdown-it for documents. ELK.js for system and flow layout when those views arrive. |
| Bridge | WebView2 JSON messages between front end and core. No HTTP server, no listening port. |
| Agent access | MCP through the official C# SDK, stdio transport. |
| Git | The installed `git` executable. |
| Persistence | Versioned JSON under `%LOCALAPPDATA%\FloorPlan`, which also holds WebView2's user data. No database. |
| Build | `dotnet` for the core and executable; npm and Vite for the front end at build time only. |
| Checks | xUnit with fixture repositories for core behaviour, Vitest for layout and activity, smoke checks of the real window. |

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
   window mode          JSON to stdout          (planned) stdio         (Claude Code, Codex)
   WebView2 host +      (tests, agents)          MCP server
   TypeScript UI                                      |
      ^                                               |
      +---------------- named pipe -------------------+
             selection, pointers, proposals
```

- `FloorPlan.Core` references no UI, WebView2 or MCP packages, so the window, headless scans, tests and the MCP server share it unchanged.
- The front end is embedded in the executable and served to WebView2 from `https://floorplan.invalid/` with a restrictive content security policy. Navigation elsewhere and new windows are refused.
- The front end has no filesystem or process access. The bridge exposes a small set of validated operations (open project, rescan; later read file and read diff); every repository path is validated against the project root.
- Scans and Git queries run off the UI thread with bounded concurrency, report progress and can be cancelled.
- Layout calculation is separate from drawing and interaction.

```text
src/FloorPlan.Core/           Inventory, Git parsing, tree aggregation, scan model
src/FloorPlan.App/            floorplan.exe: window host, bridge, scan and smoke modes
web/                          Front end: canvas map, panels, layout and activity
tests/FloorPlan.Core.Tests/   Fixture repositories and behaviour checks
examples/                     Example project descriptions (planned)
```

### Inventory and history

For a Git project, start with tracked files present in the working tree; untracked, non-ignored files are an explicit extra scope. For a plain folder, inventory regular files with configurable exclusions. Do not traverse symlinks, junctions or nested repositories; show them as boundaries.

Record bytes, type, modification time and availability. Count physical lines only for recognised text and source types, including GDScript, C#, Python, Dart, C/C++ and Godot shaders. Binary, oversized, unreadable and unclassified files keep explicit unavailable measurements. Parent totals derive from descendants within the stated scope, without double counting.

Read the current checkout and its reachable history, up to the latest 1,000 commits; do not merge branches or worktrees into one history. The activity lens counts the commits in the selected range that touch a file or region, each commit once per region. Merge commits are excluded. Activity follows paths: a rename counts as a delete and an add, so a renamed file's earlier commits stay with its old path. Zero activity requires a complete, successful sample; a failed query is unavailable, not zero.

Git runs read-only with argument arrays: no shell interpolation, optional locks, repository-configured helpers, external diff drivers or text conversion. Never check out historical revisions. Load file content and diffs on demand, with limits for large or binary content. Render repository text as data.

A scan records root, HEAD, scope and time. The checkout can change mid-scan: detect relevant changes and schedule a refresh rather than implying an atomic snapshot. Cache and preferences live in FloorPlan's data folder, keyed by canonical project path. Nothing is written to the inspected project.

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

`floorplan.exe mcp` is a stdio MCP server that the agent launches. It relays to the open window over a named pipe and reads the core model directly when no window is open. Initial tools, names provisional:

- `get_selection()`: what the human is looking at, so "this" has a referent.
- `show(ids, caption)`: highlight files, regions or entities with a short caption instead of paragraphs of prose.
- `summary(scope)`: footprint and activity for a region, from the same model the human sees.

Later, `propose(overlay)` adds a proposed change as outlined regions and edges, distinct from current code. After implementation the resulting diff lands in the same place.

## Milestones

Built in this order. Sizes are rough: S is about a session, M a couple, L several. [README.md](README.md) records what is done.

| Step | Content | Size |
|---|---|---|
| 1 | Real repository map: the trial, plus the steps below | |
| 1a | Live refresh; uncommitted changes outlined on the map and shown as a pending entry on the history strip | S |
| 1b | Reader pane: read-only source view with syntax colouring (CodeMirror), commit and working-change diffs; opens on click | M |
| 1c | Remembered zoom and range per project; optional untracked scope | S |
| 1d | Polish: legibility and style | S |
| 2 | Shared pointer (MCP) | S–M |
| 3 | Inside files: skeletons and symbols | M–L |
| 4 | Meaning connected to files | L |
| 5 | Proposals | M |
| — | Before a first release: automated build, release executable | S |

### 1. Real repository map (in progress)

`floorplan.exe` opens a folder and draws the deep treemap of its actual files: lines or bytes as area, activity or kind as fill, the history strip with range selection, and an inspector for files, commits and diffs. `floorplan.exe scan` emits the same model as JSON. Includes refresh when files change, remembered project and view state, clear scan status, and graceful handling of plain folders and a missing Git executable.

Clicking a file reads it: the reader takes the side panel's place at the same width (drag the edge to resize), so the map never reflows. Within a selected commit or the uncommitted changes, clicking a file reads its diff.

Acceptance:

- Open the case study without modifying it. Selected totals and commit/diff results match independent filesystem and Git queries for the same scope.
- Switching lens keeps selection and placement. Switching footprint updates area and units coherently.
- Bad paths, deleted files, binary files and failed Git queries produce inspectable partial results, not fabricated zeros or a broken window.
- Scanning never freezes interaction. Time to first useful map, full scan time and idle memory are recorded on the case study, with budgets set from that baseline.
- Fixtures cover paths with spaces and Unicode, Windows path behaviour, parent aggregation, renames and commit counts.

### 2. Shared pointer

`floorplan.exe mcp` with `get_selection`, `show` and `summary`, registered with Claude Code for the case study.

Acceptance: in a Claude Code session on the case study, the human selects a region and asks about "this", and the agent identifies it correctly. The agent highlights the files a planned change would touch, and the human sees them on the map.

### 3. Inside files: skeletons and symbols

The map continues past a file: zooming into one shows its symbols (types, functions, data declarations, marked sections) as regions sized by lines, and the reader pane shows the skeleton as an outline whose bodies expand on demand. Parsing uses tree-sitter grammars and tag queries, read-only and on demand, so one engine provides colouring, skeletons and symbol identities. Symbols get stable IDs (path plus qualified name), which later milestones use as anchors. Relationships start with references by name and may later come from language servers or agent descriptions.

Acceptance: on the case study, a GDScript, a Python and a shader file show correct skeletons with line counts; a symbol's ID survives an unrelated edit to its file; the skeleton reads faster than the file for learning what it does.

### 4. Meaning connected to files

The agent-written description for the case study; a system view and a code/data flow schematic linked to the physical map; anchor validation; documentation signals.

Acceptance: the user can locate the case study's main systems, explain why they coexist, and trace one important state or resource path into its source files. A broken anchor shows as stale after a rescan.

### 5. Proposals

`propose` overlays and diffs landing in place. Exercise a migration in the case study: retained code, reusable logic, its destination, a missing piece and the linked documents.

Acceptance: both participants inspect the current and proposed flow, its costs and the unresolved dependency, then see the resulting diffs in the same place. Judge whether the board improved the next architectural question or decision.

## Deferred

Language-specific dependency parsing, automatic flow recovery, runtime RAM/VRAM ingestion, complexity and coverage metrics, a multi-project dashboard, a macOS/Linux host, code signing and release packaging all wait for a demonstrated need. The model must be able to hold their evidence without implementing them first.

The licence is MIT ([LICENSE](LICENSE)). Reference checkouts and the supplied Font Awesome Pro font stay local; a public build uses the bundled free icons.
