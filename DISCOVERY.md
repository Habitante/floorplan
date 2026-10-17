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

### 17 October 2026: a colour grammar (Claude)

The owner found the map's colour weak: monochrome heat, regions hard to tell apart, and far less read from colour than it could give. An audit of the dark theme on the case study, with a short range, found:

- **One grey for everything.** Inactive files (`#383835`), folder frames (`#262624`, `#30302d`) and the gaps between them sat within 0.07 of each other in OKLCH lightness, so with few commits in range nearly the whole map was that grey.
- **Heat read as rust.** Three commits only reach steps 1 and 2–3 (`#925019`, `#b3621e`, chroma about 0.12, ΔE 8 apart, one hue), so the busiest tiles were the dullest colours on screen.
- **Each lens erased the other**, and **orange meant two things**: documentation in Kind (`#d95926`) was ΔE 6 from activity's 4–7 step (`#d67523`).
- **Regions did not separate:** every level shared the same one- or two-pixel gap, and the alternating frame greys were ΔE 3 apart.

Decided, with the reasons:

- **Warm means change.** Red through orange to yellow is reserved for time. Activity keeps its fixed steps on a multi-hue ramp, so adjacent steps differ in hue as well as lightness (ΔE ≥ 9.9 dark, ≥ 8.7 light, lightness monotone). Documentation leaves orange for a neutral: the one that contrasts most with the background, a light grey in the dark theme and a dark grey in the light one.
- **A basemap under the lens.** What Activity or Age leaves at zero keeps a dim tint of its kind instead of one grey. The selected question still owns the strong colours (step 1 is ΔE ≥ 25 from every tint in the dark theme, ≥ 12.9 in the light one), so this orients rather than answering a second question; it bends "fill answers one question" in that sense only. Per-folder region hues were rejected: position and labels already say which region a tile is in, and those hues would compete with kind.
- **Structure from gaps and darkness, not hue.** Gutters of 6, 3 and 1 px by depth below the focus, a 20 px label strip with a larger name for regions, and frames darker than the files on them. The gaps are part of the layout, so hit-testing and labels agree with what is drawn.
- **Tests as a kind.** The case study's tests folder is about an eighth of its code, and telling tests from code is a question the owner asks. Path conventions decide it in the classifier (a `test`, `tests`, `__tests__`, `spec` or `specs` folder; `test_x`, `x_test`, `x.test`, `x.spec`, `XTest`, `XTests`, `conftest`), only for files whose extension is code, so fixtures stay data and notes stay documentation. Scans gain a `tests` folder total, and the board's `codeAndDocLines` includes tests. On the case study: 28 files, 6,897 lines.
- **An Age lens.** Time since each file's last commit, on the same warm ramp: within a day, a week, 30 days, 90 days, a year, then the tint. It is measured at the history range's end (now while the range follows the newest commit), so the strip moves it through time. Every file has an age, so Age never leaves the map grey; on the case study, whose oldest commit is 354 days old, it lights everything, which is accurate.
- **Uncommitted violet lifted in the dark theme**, from `#9085e9` to `#bda8fc`: the old value was ΔE 3.7 from the code blue under deuteranopia simulation.

Checked in OKLab with Machado deuteranopia, protanopia and tritanopia simulation. Kind hues stay at least 10 apart for deutans and protans. Known weak pairs: tests and assets for tritans (3.1 dark, 5.7 light), which rarely meet because assets take no area under Lines; and in the dark theme, the raspberry pointer against the tests teal for deutans (3.4). The pointer keeps its surface hairline and width there, and no teal tried cleared that pair without breaking another (code for tritans, or documentation for protans).

The proposal was first drawn as a design canvas with the current and proposed palettes side by side on a mock of the case study, then built. On the case study, a Release build drew its first map in 1.15–1.19 s against 1.15–1.24 s for the previous build, both measured while other FloorPlan windows and servers were running.

Reviewed before commit by the main session, with the designing session agreeing:

- **Unknown ages are hatched.** A scan keeps the latest 1,000 commits, so a file without a commit up to the range's end is older than a year only when the history was cut short, no loaded commit touches it, and the oldest loaded commit is more than a year before the range's end. Otherwise it came after the range's end, was never committed, or its last commit was not loaded, so it is hatched like any unmeasured value, and the Age legend shows the hatch.
- **Two more weak overlay pairs**, measured the same way: in the light theme, the pointer on the 16+ step (`#a32e21`) is ΔE 13.9 for normal vision, under the floor of 15, on the tiles agents point at most; in the dark theme, the pointer on the 2–3 and 4–7 steps is 5.5–5.6 for tritans. The violet stays at least 8.9 from every fill for deutans and protans and 7.2 for tritans. A retune would change the pointer and keep the ramp, whose steps are tied to their separation from the tints.
- **Agents read age.** File and folder replies carry `lastCommit`, measured as the Age lens measures it.
- The working rules for colour (validating and recording values, tokens only, what a new kind touches, keeping the agents' map description current) moved from the session's handoff note to AGENTS.md.

### 17 October 2026: the roadmap after the board and the colours (Claude)

Asked how the tool could be more useful, the designing session proposed five things and a process tip. The main session weighed each, and the owner agreed to all of them in this order:

- **Since I was last here (1e), first.** Coming back to a project is the problem FloorPlan exists for, and the history strip already holds such a range; what was missing is remembering the HEAD the human last saw and opening there, with one line on what changed. Two refinements: a window in use never has its range moved, and agents are named only where commits name them in co-author lines, because agents commit under the owner's name.
- **A file finder (1f).** The cheapest usability win on 2,900 files, and invoked rather than persistent, so the interface stays lean.
- **A second case study (1g), soon.** The Godot game is young and under the 1,000-commit cap, which is why the Age gap went unseen there; a large, old repository tests the cap, the test conventions, the speed and the colours before they set into habits, and is the right moment to set milestone 1's budgets. Proposed: the Godot engine, cloned with a little over 1,000 commits of history.
- **Files that change together (3), with guardrails.** It answers "if I touch this, what else moves?" from data already loaded, and shows ties the folders hide. Commits in agent-driven projects are broad, so large commits count for less, pairs need a minimum of shared commits, the evidence is shown, and the relation is called "changes with", never "depends on". **Tests paired with code** join it, by name or by changing together, labelled as pairing, not coverage.
- **Meaning before skeletons, split.** This settles the open question of 15 October without a separate experiment, since the path-only description is meaning's first slice anyway. "Where is what, and why" often cuts across folders (the case study's main parts) and needs no parsing, but data owners and flows need symbol anchors. So meaning on paths (4) comes before skeletons (5), and meaning on symbols (6) after them.

The milestones were renumbered to follow the build order: old 3 (skeletons) is now 5, old 4 (meaning) is split into 4 and 6, and old 5 (proposals) is now 7. Entries above keep the old numbers.

### 17 October 2026: since I was last here (Claude)

Step 1e, built. The plan offered a briefing when the human came back to an open window after an hour idle. The owner set five minutes instead. The board is most useful when it shows what the agents are doing almost in real time, so "since I was last here" has to cover a coffee break as well as a weekend. One mechanism then serves both, as the 15 October entry intended for the history range. Decided while building, with the reasons:

- **Presence is the human's own input:** pointer movement over the window, clicks, scrolling, keys, and bringing the window forward. The window's idle clock used to restart on every view report. A range that follows the newest commits changes whenever one lands, so with agents committing every few minutes a window never looked idle; agents' reveals restarted the clock too. That also kept `get_selection`'s `stale` from firing. Keyboard focus alone is not presence, since the human can leave with FloorPlan focused. Chromium repeats pointer events when content changes under a resting cursor, so the pointer counts only when it moves. Windows activates the next window when one closes, and that counts as bringing FloorPlan forward.
- **An open window stays live and never switches by itself.** The owner's model: a read-only, real-time view of what the agents do, in which updates never override what the human is poking at, and what an agent shows appears at once. A first version moved a range that only followed the newest commits to the commits since when the human came back. It was dropped: FloorPlan cannot tell watching from being away, and on a second monitor a touch after five minutes of watching would have switched the map's colouring under the human. The chip offers the commits since instead. An agent's reveal still changes the view at once, but it is not presence.
- **The chip is the toggle** the owner wondered about. It switches between the commits since and the range before, with no mode to remember.
- **The briefing is fixed when shown:** it records what landed while the human was away. A range since the human was last here keeps its start as commits land, instead of sliding like "latest N", so commits that land while they watch join it.
- **Counted by Git, described from the scan.** `git rev-list --no-merges <seen>..HEAD`, limited to the folder when it is inside a repository, gives the acceptance test's count. The strip's order would not give it: a HEAD seen as a merge commit is not on the strip, since merges are left out, and the commits from merged branches are not a suffix of it. `merge-base --is-ancestor` detects a rebase, reset or branch switch, which the briefing reports instead of counting. Commits beyond the loaded history are counted in full and described from the loaded ones.
- **Co-authors from trailers.** The log format reads Co-authored-by lines through Git's trailer parser. A Git too old for its options prints the placeholder, which names nobody. On the Godot game, 546 of 548 commits are by Habitante and 265 are co-authored by Claude Opus 5.5, with no other names. Any other agent working there stays unnamed, as the design expects.
- **Settings shared safely.** Each window read `settings.json` at start and wrote the whole file back, so two windows, or a window and an agent's server, undid each other's changes; the seen HEAD would have made that frequent. Each change now re-reads the file under a lock that every FloorPlan process takes, and the agent's server reads settings per call. Test windows never write them; `--seen <commit>` sets the starting point for a check.

Checked, besides fixture tests for the counts against `rev-list`, a reset, a merge seen as HEAD, a subfolder and more commits than loaded:

- On the case study, a test window started with the seen HEAD ten commits back opened at those ten, with a briefing that matched an independent count from Git: scenes 6, tests 6, tools 4, shaders 2, docs 1, and Habitante with Claude Opus 5.5 on all ten. An agent read the same through `get_selection`.
- A real window on a scratch repository, moved off-screen so no input could reach it, recorded a visit, refreshed on two commits, and five minutes later came back to a briefing of exactly those two, and the stored HEAD advanced. Two earlier runs were spoiled by the test itself: identical file contents produced no commits, and closing another test window activated this one.
- Computing the briefing takes 51–58 ms on the case study (one round of Git calls), within the first map's run-to-run noise: 1.07–1.28 s with or without a briefing, Release build, 2,906 files, 551 commits.

Known limit: watching the board without touching it counts as being away, so after a stretch of only watching, the next touch shows the chip, though it moves nothing. If the chip proves noisy, a board in view while the human uses the computer could count as presence, which needs a check of the windows covering it.

### 17 October 2026: new files on the map (Claude)

Shown the board on this repository, the owner was surprised that a new file was not on it. Untracked files had been an extra scope, off by default, since step 1c, with no recorded reason. For a live view of what agents do, that hid exactly the files they create before committing them. Untracked files that Git does not ignore are now shown by default, outlined as uncommitted, in the window, in agents' summaries and in `floorplan scan`; the info panel hides them per project. Ignored files stay off the map. The stored `IncludeUntracked` values were defaults written for every scanned project, not choices, so they are ignored rather than migrated. At the time: 4 untracked files in this repository (this session's new files), 8 in the Godot game (screenshots).

A switch to include files that Git ignores was considered and left out, with the owner's agreement. In both projects they outnumber the files on the map: 8,868 against 82 here, mostly `web/node_modules` and build output, and 8,968 against 2,906 in the Godot game, mostly Godot's import cache and 3,915 renders. Nearly all of it is generated: it would bury the map, slow every scan, and make every build trigger rescans. When an agent points at an ignored file, its nearest folder on the map is outlined. Naming one ignored folder per project, such as the renders, waits until one is missed.

### 17 October 2026: kinds of change on the map (Claude, with the designing session)

Pointed at its new files, the owner noticed that only the pointer told them apart: new and modified files had the same violet outline. The designing session proposed separating them by shape, since the overlay hues are all taken, and the main session agreed. Decided, with the reasons:

- **Violet stays the one colour for "not committed yet",** and shape tells the kind. Shape also holds for colour-blind viewers.
- **New files get a filled corner** of up to 14 px on top of the outline. A new file changes the project's shape, so it gets more weight than an edit, and a folded corner reads as "not in the history yet". The leaf label leaves room for it.
- **A rename is new at its new path,** as Activity counts a rename as a delete and an add. Git reports staged renames with their old path, which the scan now keeps for the tooltip and the inspector.
- **Deleted files are counted in the region headers,** with new and modified ones: `+2 ~1 −1` in semibold violet, only the counts that are not zero. A colour per kind (green for new, red for deleted) was set aside with the owner: red and orange already mean recent change and green means assets, red against green is the hardest pair for the commonest colour blindness, and one colour keeps "uncommitted" a single meaning. Deleted files take no area, so the map showed them nowhere before. Counts are measurements, not prose. A region whose files were all deleted has no area and no header left; its deletions show only in the inspector and the pending entry on the history strip. The violet reads as text on the headers at 5.66:1 in the light theme and 8.27:1 in the dark one, above the 4.5:1 for small text; the grey price tags beside them are 2.38:1 in the light theme.
- **A new file pulses once,** for two seconds, when a refresh brings it, the way a pointer does on arrival, and not at all under reduced motion. That answers the owner's wish to see agents' work almost in real time.
- **Dashed outlines are kept for proposals** (milestone 7), where a dashed line means "does not exist yet".
- **Staged and unstaged stay off the map, and are deferred.** Agents usually stage and commit in one go, so the state rarely lasts, and telling them apart means keeping both of Git's status columns in the model.
- **One vocabulary,** at the owner's request: the interface says New, Modified and Deleted, with N, M and D in the uncommitted list and `+ ~ −` in the headers, all under the one word "uncommitted". "Not committed", "untracked", "added" and "renamed" left the interface; a rename reads "New, uncommitted, renamed from …", and the scope switch is "Show new files". Git's terms stay in the scan model and in agents' replies, since agents work with Git.
- **Files at the top level count as one region** in the "since" note. On this repository the note had listed `.gitignore`, `DESIGN.md` and `DISCOVERY.md` one by one, crowding out the folders. Agents read the region as `""`, the name the board already gives the project's root.
