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

### 18 October 2026: the file finder, and agents' whole notes (Claude)

Step 1f, built, and a gap the owner found in the pointer row. Decided, with the reasons:

- **Matching is fuzzy over the whole path,** scored in the manner of fzf: a term's characters in order, case and accents aside, with more for contiguous runs, runs that start a folder or a word (after `/`, `_`, `-`, `.`, a space or a capital) and matches in the name. Two bonuses settle what alignment alone got wrong: a term that is the whole name or its stem, so `player` finds `player.gd` first, and a term found whole, so `multiplayer.gd` comes before `plain_layer.gd`, which matches as "p" plus "layer". Spaces separate terms that must all match, so `test player` finds `tests/test_players.gd` in either order; a backslash reads as a slash. Folding keeps one character per character, so the marked letters stay on the right ones in Unicode paths.
- **Area breaks ties.** Godot keeps a `.uid` file beside every script, with the same name plus a suffix; under Lines it has no area, and it tied with the scripts and shaders the human was after. Among equal matches, those with area under the current footprint come first. Rows without area are faded rather than hidden, since an image or a scene can be what the human wants.
- **Choosing does what clicking does.** A file is read in its own folder, so it is large enough to see among its neighbours, and within a selected commit that touched it the reader shows that commit's diff, as a click on the map does. A folder becomes the view. The map can draw no selection for something without area, such as a texture under Lines, so it is selected and its nearest folder with area is outlined, the rule uncommitted changes and pointers already follow. The footprint is never switched for the human.
- **An empty query shows nothing.** A list of recent files was considered and left until it is missed.
- **Typed letters are never lost.** The input takes focus before the first paint; the first version focused it after, and letters typed straight after Ctrl+P went nowhere. In a release build WebView2's browser keys are off, so Ctrl+P reaches the page; with them on in development, the page's handler cancels the key's default, which should keep the print dialog away.

The pointer row had room for a caption's start, and the tooltip listed the paths but not the caption, so a long note could not be read anywhere. Agents are asked for one short line and write paragraphs anyway; the board has to cope. Clicking the caption now opens the whole note over the foot of the map, with the agent, the time and the paths as rows: a file opens in the reader at the lines pointed at, a folder is selected, and the note stays open while the human reads through them, until Escape, a click elsewhere or the chip again. The locate button keeps bringing the pointer into view. A path off the map shows what the agent named without its outlined folder's measure, which would read as the path's own. The side panel was considered: it fits the commit and uncommitted panels, but the reader replaces it on the first file opened, which is what the human does next. The tooltip now carries the caption too, and the server's instructions and the `show` tool tell agents that the row shows a caption's start, so they lead with the point.

Measured in Node on a scan of the case study (2,955 files and folders): the slowest keystroke takes 2.8 ms to match, and building the index on first use 11 ms. A five-fold copy (14,776), about the size of the Godot clone planned for 1g, takes 10 ms for a single character, 1–6 ms for longer queries, and 42 ms for the index. Checked in headless Edge with a stand-in host that served a fresh read-only scan of the case study and its files: Ctrl+P, typing, the arrow keys, Enter and Escape; a folder, a script, a `.uid` file (outlined through `scenes`) and the image list; at 1600×1000 in the dark theme and 760×900 in the light one. Each keystroke's results were drawn by the next frame. An agent's note of four sentences and eleven paths read in full in both, and its first row opened the reader at lines 3–9 while the note stayed open. Ctrl+P was not tried in WebView2 itself.

### 18 October 2026: a second case study and the budgets (Claude)

Step 1g. The Godot engine had been proposed, but no copy was on the machine, and the owner asked for a look through `C:\Src` first. The Flutter SDK, installed there as a Git clone, meets every criterion with no download: 15,352 files (five times the Godot game), eleven years and 87,655 commits (87 times the cap), Dart with its own test layout, and a monorepo of packages and an engine rather than a game. Two others suit a later check of dense agent-driven history, where 1,000 commits cover only days: openclaw (20,285 commits in four months) and the owner's reels (3,762).

What held at that size:
- **Speed:** scan 0.82–0.86 s warm, first map 1.51–1.62 s, and about 40 MiB more in the host and 30 MiB more in WebView2 than the Godot game. Redraws take 3–7 ms, input to paint 16–64 ms, board calls 54 ms at most, finder keystrokes 21 ms at most.
- **The map:** regions and labels hold, and Kind shows the test tree, 1.26 million of 3.3 million lines, at a glance.
- **Tests:** Dart's conventions match the classifier's. All 2,626 `_test.dart` files and every Dart file in a test folder are tests.
- **The briefing past the cap:** from a starting point 1,500 commits back, it counted 1,500 as Git does and said some are older than the loaded history.

Fixed:
- **The briefing listed every author and co-author:** 124 and 74 on Flutter, about 8 KB in each agent reply and in the chip's tooltip. Each list now keeps the ten with most commits and counts the rest (`moreRegions`, `moreAuthors`, `moreCoAuthors`), and the chip's "+N" includes them. The briefing is now 1.3 KB.
- **The history row wrapped** its commit count and dates onto two lines when the briefing and a pointer chip were both present. They keep their line; the chips truncate.

Recorded, for the owner to decide:
- **Age on an old checkout.** Flutter's newest commit is 247 days old, and Age measures from now while the range ends at the newest commit. Every file with a loaded last commit therefore falls in one step, within a year, and the rest are hatched: true, and nearly blank. Moving the range's end back one commit measures from that commit and spreads the steps, but nothing tells the human so. Options: measure from the newest commit when it is old, or say in the Age legend what ages are measured from. Not changed, since "from now" is right for the owner's live projects.
- **Broad mechanical commits.** Two lint sweeps on 26 November touch 2,494 and 1,004 files. Without them, 1,695 files would have a loaded last commit instead of 4,723, so most of Age's known ages, and any Activity range that includes those commits, come from one mechanical change. Agent-driven projects make such commits too (renames, formatting). Milestone 3 already counts broad commits for less; Age and Activity could follow, for example by marking large commits on the strip or letting the human leave them out. Not changed: it changes what the lenses mean.
- **Unclassified files:** 1,993 of Flutter's files, 13%, have no kind and no line count. They are GN build files (`.gn`, `.gni`, 380), localisation bundles (`.arb`, 349, JSON), templates (`.tmpl`, 298), and Xcode and Gradle project files. Under Lines they take no area. GN is a build language as CMake is, and `.arb` is JSON, so both could join the classifier. Every Flutter app has the Xcode and Android project files, so the owner's own Flutter projects have them too.
- **Hatching** covers 69% of Flutter's map under Age. On 2 November the loaded history's oldest commit turns a year old, and those files become "older" instead; both are correct.

Budgets ([README.md](README.md)) leave headroom of roughly 1.4 to 1.6 times over both baselines, so run-to-run noise and other FloorPlan processes do not trip them. Interaction budgets come from what a person notices: 16 ms per redraw (one frame at 60 Hz), 100 ms from input to paint, which reads as immediate, and 50 ms per finder keystroke. For context, the Godot game's first baseline (16 October, 2,862 files) drew its first map in 0.87 s; it takes 1.06 s now, after the colour grammar, tests as a kind and the briefing.

Method: smoke windows for scan, first map and memory, and a Node MCP client against a smoke window on Flutter, which no window of the owner's showed. Interaction was timed in headless Edge with a stand-in host and the same front end, recording each animation frame and the browser's input-to-paint timing. Headless Edge sometimes marks a page hidden and stops animation frames, which hung the first runs; keeping the page in front fixed it.

### 18 October 2026: ages from the newest commit (Claude, with the owner)

Told that Age read nearly blank on Flutter, the owner suggested measuring ages from the most recent commit or change, everywhere. Decided:

- **From the range's newest commit, always.** "From now" kept a live project current. But Flutter's newest commit was 247 days old, so it put all 4,723 files with a loaded last commit in one step. Measured from the newest commit, they spread: 56 within a day, 62 within 30 days, 4,408 within 90 days (mostly the lint sweep) and 195 within a year. Coming back after a pause, the map shows what was recent when work stopped, which is what the human returns for; how long ago that was is on the history strip and in the briefing. For a live project the two differ by minutes. The ten-minute clock that kept "from now" current in an open window is gone.
- **Not from uncommitted changes.** They have no commit time, and file times move with checkouts, builds and copies. Measuring from the latest save would also recolour the whole map each time an agent saved a file. The violet outlines already say "changed, not committed".
- **Dates stay dates.** The tooltip and the inspector still give a last commit as "3 days ago", counted from today, since that is a date and not a step. The Age legend names the commit it measures from, and the agents' map description says the same.

The owner also agreed that broad commits wait for milestone 3, which already counts them for less. More file kinds can come when wanted; the owner suggested at least counting the lines of rarer files that are text.

### 18 October 2026: files that change together (Claude)

Milestone 3, prototyped on both case studies' histories before it was built. Decided, with the reasons:

- **The whole loaded history, not the range.** A relation needs evidence, and the human's range is often a few dozen commits; it should also not change as the range is dragged.
- **Broad commits count for less.** A commit counts fully while it relates the file to up to ten others, and in proportion beyond. Commits touch a median of 4 files on the Godot game (90th percentile 16) and 1 on Flutter (90th percentile 9), so typical commits keep their full weight. The sweeps do not: one commit of 4,060 files on the Godot game, and Flutter's two lint sweeps of 2,494 and 1,004 files. Counted naively, Flutter's `editable_text.dart` shares two commits with 31 files through those sweeps alone; discounted, with none.
- **A pair needs two shared commits worth at least one.** Requiring them to be worth two dropped a test whose only two commits touched a dozen files each.
- **Ranked by how much of both histories they share** (the shared weight over the square root of the product of each file's own weight). Ranked by the share of one file's commits alone, files that change in most commits would top every list. Now a script that shares all three of another script's commits but changes in ten ranks below a shader that shares two and has only those two.
- **Shown where the human is.** Selecting a file opens the reader, which takes the inspector's place, so the reader carries a short line: tests, then the files that change with it, each with its shared commits and three names before "+N". The inspector lists rows. Evidence is in the tooltips, and a count leads to the pair's newest shared commit (in the reader, that file's diff in it).
- **Ink hairlines on the map,** at three opacities by strength. The relation belongs to the selection, so it takes the selection's ink and no new hue. A first version drew the strongest at 1.5 px in nearly full ink, and the selection was lost among them; partners are now 1 px and the selection keeps its 2 px.
- **Tests by name, as the test kind names them,** with the same extension. Where several files share the name, the one sharing most folder names wins, so Flutter's `material/text_field.dart` pairs with `test/material/text_field_test.dart` and not the Cupertino one. Plurals are no convention, so a test named in the plural does not pair by name; it is found by changing together instead.
- **One implementation, in Core.** The window asks its host for the selected file's relations, as the reader asks for a file's text, and agents read the same in a file's `summary`, with or without a window.

Checked: unit tests in Core, including counts against `git log`, a sweep of thirty files relating nothing, a file in most commits ranking below a close partner, and the material and Cupertino pairing. On the case studies, every pair shown for one script of the Godot game (5) and Flutter's `dropdown_menu.dart` (3) matched a direct count with `git log` over the same 1,000 commits. 256 of the Godot game's 2,965 files and 499 of Flutter's 15,352 have partners. The host answers in 9 to 11 ms for the first file of a scan, which builds the per-scan history, and then in at most 14 ms on the Godot game and 23 ms on Flutter, the slowest being files caught in the sweeps. Seen in real windows on both projects, and on the Godot game in both themes; an agent's `summary` of that script returned the same five partners with their shared commits.

### 20 October 2026: meaning on paths (Claude, with the owner)

Milestone 4. This session wrote the first description, of the Godot game, from its own README, architecture document and roadmap. It names five systems, the direction of the migration and two findings; one is the kind of contradiction DESIGN cites, a roadmap that calls a part missing and later describes it as built. Decided, with the reasons:

- **Format 1, settled with this first consumer:** a summary; systems with an id, a name, a code, a role, a status, a basis, path anchors and document references; direction; findings with the paths that show them. Anchors are files, folders or globs. Where they overlap, the most specific wins, so one file in a folder can belong to another system than the folder without every file being listed.
- **Private descriptions stay private.** The repository is public and the case study is not, and the owner ruled that the repository says nothing about it. Descriptions live in FloorPlan's local data, and `examples/` holds FloorPlan's own. On 18 October the whole history was rewritten to remove the case study's name and contents, and the owner force-pushed it.
- **Drawn as a fourth fill, Systems, with codes.** One colour per system gives the overview, but the grammar leaves few hues free: warm means time, and violet and raspberry are overlays. On a treemap any two systems can touch, so every pair must stay apart, and with five systems colour alone could not do that for colour-blind viewers. The owner asked for a cue beyond colour. Codes of one to three characters, printed where they name a region and inherited below it, read the same for any colour vision. Patterns and dashes were set aside: hatching already means an unknown age, and dashed outlines are kept for proposals. Picking a system lights its files; that works under any lens and for any number of systems.
- **A folder carries a code only when one system holds more than half of it.** The first version gave a folder its largest system, so a folder of new code the description did not cover yet carried the code of the one part it did.
- **Five colours, the same families in both themes.** They were searched rather than guessed: candidates from green through cyan to blue, and neutrals, checked in OKLab with Machado simulation. Six could not clear the floors in the dark theme (13.6 at best between two systems); five pass with margin. Light: `#137ece`, `#6cc185`, `#368223`, `#474e55`, `#90aae4`; dark: `#1a9be5`, `#65c36f`, `#1b6a00`, `#747474`, `#1f53aa`. Between systems, normal vision is at least 17.6 in the light theme and 17.9 in the dark one, deutan and protan at least 14.1 and 12.5, and tritan at least 4.3 and 4.7, the weak case, which the codes carry. Against tints and frames the colours are at least 15.4 and 15.0 apart; against the violet and raspberry outlines under deutan and protan at least 6.2 and 7.8; against the selection's ink at least 27.1 and 30.1. A first search allowed a lime at hue 125, too close to the activity ramp's yellow, so chromatic hues start at 140°. A sixth or later system shares the neutral, and the prompt suggests five at most. The provisional colours of the first mock failed, with teal and green 7.6 apart in the dark theme.
- **Agents read and write descriptions through the board.** A project's summary carries a digest, and a file's summary names its system. `get_description` gives the description in full with its check. `describe` checks it against a fresh scan and saves it outside the project: problems (the version, duplicate ids or codes) refuse it, while stale anchors and the largest unnamed code are reported. The server scans and writes itself, and windows watch the file, so no new messages between server and windows were needed, and a hand edit shows too.
- **One prompt, shipped in the executable.** `describe-project` is an MCP prompt, a slash command in Claude Code, and a file in the repository for any other agent.
- **Agents use the board on their own.** Asked how agents would know to read descriptions and when to write them, the owner set the stance: having the tools, an agent should treat FloorPlan as a visual window onto the repository shared with the human. It uses the window whenever it discusses the repository's shape, status, contents or changes, opens it when closed and tells the human to look, and keeps the description current without being asked, since asking each time, or giving permission each time, makes the tool much less useful. The server's instructions now say so. The rule that `open` was only for when the human asked to see something is dropped. The description changes FloorPlan's own data, never the project, so an agent refreshes it after a task that moved a part's files, keeps its ids, and says so when it hands over; it rewrites one wholesale only when asked. A project's summary carries `descriptionNote` when the description is missing or out of date: stale anchors, or 500 lines or more of code in one place that no system names, a size chosen so a stray script does not nag.

Checked: unit tests for anchors and their specificity, stale references (a deleted file, lines past a file's end), problems, unnamed code, the tools' save, refusal and read-back, and the summaries' digest. In real windows: the Godot game's systems with their codes in both themes, and FloorPlan's own description saved by `describe` and reloaded by an open window. `get_description` takes 0.34 s on the Godot game, a fresh scan included. Two days after it was written, the Godot game's description shows what the check is for: a design document it references had moved, so its anchor is stale, and three new folders of code, 11,800 lines, were in no system.
