# FloorPlan

A visual development board for understanding a project's code, data and current direction. Open a repository, see where its weight and activity are, and inspect the evidence behind architectural decisions.

The aim is to keep the human informed enough to ask better questions and choose useful work while collaborating with coding agents.

## Why

From the author's original note:

> Many of us were good software architects, but as we reach further and further with current AI coding agents, we are doing too much, too quick, often working on several big projects at once. And you know the toll project switching takes on humans. It's hard to keep the project as a whole in mind. So we sometimes feel relegated to being "dumb project leads", or almost "dumb clients", asking the agents basic questions about our own projects: how things work, where things are, what can be done or not. And that isn't good.
>
> I like the SpaceSniffer analogy because something that gave us (at least those of us who are good at visual processing and spatial reasoning) an instant snapshot of each project in development could "upload" the most essential information into our brains in an instant. Give us, viscerally, the project's map, state and vitals. So we could be a bit less "dumb clients", and more informed, expert project leads.

## Documents

- [DESIGN.md](DESIGN.md): the product and architecture contract, and the milestones.
- [DISCOVERY.md](DISCOVERY.md): research, the case study and a dated log of decisions with their reasons.
- [AGENTS.md](AGENTS.md): rules for agents working in this repository.
- [ref/README.md](ref/README.md): reference checkouts and their provenance.

## Status

Milestone 2 in use (16 October 2026). `floorplan.exe` opens a folder and draws every file as a nested treemap, and coding agents can ask what the human has selected, point at files and lines on it, and open it. Of milestone 1, steps 1a–1c are done; the owner's visual tweaks (1d) continue, so far a colour grammar with an Age lens and tests as their own kind, and new files and the kinds of change marked on the map, all on 17 October 2026. Of the steps added to the roadmap on 17 October, 1e ("since I was last here") is done the same day, and on 18 October 1f, the file finder, and 1g, a second case study (the Flutter SDK) with milestone 1's performance budgets set from both.

Working:

- **Scan:** files in the working tree that Git tracks, plus new files it does not ignore (or every file in a plain folder), with size, physical lines, kind (code, tests, docs, data, assets, other) and up to 1,000 commits of history. Read-only: Git runs without optional locks or repository-configured helpers, and a test checks that `.git` is left untouched.
- **Map:** canvas treemap down to individual files, with gutters that widen toward the top-level regions. Area is Lines or Bytes; fill is Activity (commits in range), Age (how long before the range's newest commit each file was last committed) or Kind. Activity and Age share one warm ramp, and what they leave at zero keeps a dim tint of its kind; an age the loaded history cannot tell is hatched. Zoom with double-click, the mouse wheel, the breadcrumb or Backspace. Tooltips show each item's measurements.
- **Inspector:** measurements, composition by kind, contents (including files with no weight under the current area), and recent commits for the selection.
- **File finder:** Ctrl+P (or the magnifier) finds files and folders by a few characters of their path, best matches first, with price tags and uncommitted marks. Choosing a folder zooms into it; choosing a file reads it in its folder. Something without area under the current footprint, such as an image under Lines, is outlined through its nearest folder that has area.
- **History strip:** drag to choose the commits counted by Activity; click a commit to see the files it touched.
- **Live refresh:** the map rescans when project files change or a commit lands, ignoring Git-ignored output and editors' index updates. Uncommitted changes are outlined in violet and collected as a pending entry at the end of the history strip. New files that Git does not track yet are on the map too; the info panel can hide them per project. New and renamed files also get a filled violet corner and pulse once when they arrive, and region headers count new, modified and deleted files (`+2 ~1 −1`).
- **Reader:** click a file to read it with syntax colouring, in the side panel (drag its edge to resize; the map keeps its layout). Chips switch between the file, its uncommitted diff and the diffs of recent commits that touched it. Inside a selected commit, clicking a file opens that commit's diff.
- **Remembered view:** each project reopens at its last zoom and history range; a range ending at the newest commit keeps following new commits.
- **Since I was last here:** FloorPlan remembers the HEAD each project had when the human was last present in a window on it (their own input: pointer, keys, bringing the window forward). Reopening the project opens the range at the commits since, and a chip in the history row says when the human was last there, how many commits landed, the top-level regions they touched and who made them, naming agents from co-author lines (the ten with most commits in each list, and how many more). Back at an open window after five minutes without input, the chip appears again and offers the new commits; an open window never switches by itself. The chip toggles between the commits since and the range before. A rebased-away or unloaded starting point is reported, not guessed.
- **Shared board:** one `floorplan mcp` server serves every open window, choosing by the project a call names, an absolute path, or the window the human used last. `get_selection` returns the selected file or folder with when it was chosen and at which HEAD, the reader's file with the lines in view, the selected lines and text, the zoom, the history range, recent selections, the time since the human's last input in the window, what landed while they were away (`sinceLastSeen`) and the other open windows. `show` outlines files, folders and line ranges with a caption in every window on the project (one pointer per agent, listed under the map, where clicking a caption opens the whole note with its paths); paths off the map outline their nearest folder, and `reveal` brings it all into view. `summary` reports footprint and activity from the model on screen, and file and folder replies carry their last commit as Age shows it; a summary of the whole project adds `sinceLastSeen`, with or without a window. `open` raises or starts a window without taking the keyboard. Without a window, `get_selection` and `show` say nobody is looking and `summary` scans the project.
- **Executable modes:** window, `scan` (JSON on stdout), `mcp` (MCP server on stdio) and `--smoke` (capture plus measurements).

In the agent's second round on the case study, mouse-selected text reached the agent, clearing a pointer worked, and `open` raised a window; whether a window an agent opened outlives the agent's session is not yet tried.

Next: milestone 3, files that change together. See the [roadmap](DESIGN.md#milestones).

### Baselines and budgets

Two case studies: a private Godot game (2,973 files, 561 commits), and the Flutter SDK (15,352 files, 3,530 folders, the latest 1,000 of 87,655 commits), both inspected read-only. Measured on 18 October 2026 with the published executable in a 1600×1000 window, while other FloorPlan windows and servers ran. A change that takes a measure past its budget is fixed, or its reason recorded in [DISCOVERY.md](DISCOVERY.md).

| Measure | Godot game | Flutter | Budget (Godot game / Flutter) |
|---|---|---|---|
| Scan | 0.42 s | 0.82–0.86 s; 2.2 s with a cold file cache | 0.75 s / 1.5 s |
| First map after launch | 1.05–1.06 s | 1.51–1.62 s | 1.5 s / 2.5 s |
| Memory, private, host | 116–117 MiB | 154–160 MiB | 160 / 220 MiB |
| Memory, private, WebView2 (5 processes) | 177–179 MiB | 206–212 MiB | 250 / 300 MiB |
| Lens, area or zoom: input to paint | | 16–64 ms | 100 ms |
| Redraw: hover, zoom animation | | 3–7 ms | 16 ms |
| File finder, per keystroke | 2.8 ms or less to match | 6–21 ms to results; 61 ms for the first, which builds the index | 50 ms; 100 ms for the first |
| Board call with a window open | 2–14 ms | 3–4 ms `get_selection`, 16–40 ms `summary`, 33–54 ms `show` of 12 paths | 100 ms |
| `summary` without a window (a fresh scan) | | 1.1 s | 2 s |

Interaction was measured on Flutter, the harder case, in headless Edge running the same front end. The Godot game's board calls are from 16 October, as are `floorplan mcp` starting in 0.22 s and `open` starting a window and scanning in 0.6 s. With the reader open, WebView2 used 245 MiB on the Godot game. The executable is 53.1 MB, single file. The briefing on opening a project takes 51–58 ms for ten commits on the Godot game and is part of its first map.

The scan's file set, byte and line totals, and per-file commit counts for the latest 40 commits matched independent Git and filesystem queries on the Godot game. On Flutter, the briefing's count of 1,500 commits since a chosen starting point matched `git rev-list --no-merges --count`. Smoke runs keep their own WebView2 data folder, so an open window does not share their browser processes or skew these figures.

## Build and run

Requires the .NET 10 SDK, Node 24 and Git. WebView2 ships with Windows 11.

```bash
npm ci --prefix web
dotnet build FloorPlan.slnx
dotnet run --project src/FloorPlan.App -- path/to/a/repository
```

The build runs the front-end build and embeds it in the executable.

In VS Code, press F5 with one of the launch configurations: **FloorPlan** (last project), **FloorPlan: open folder…**, or **FloorPlan: live front end**. The last one starts the Vite dev server and loads it inside the window, so front-end edits reload in place and F12 opens DevTools. Ctrl+Shift+B builds; the default test task runs both suites.

Checks:

```bash
dotnet test tests/FloorPlan.Core.Tests
npm test --prefix web
npm run typecheck --prefix web
```

Single-file executable at `publish/floorplan.exe`:

```bash
dotnet publish src/FloorPlan.App -c Release -r win-x64 -o publish
```

Executable usage:

- `floorplan [folder] [--open file[@commit|@working]] [--theme light|dark] [--seen <commit>]` opens the window, on the last project when no folder is given, optionally with a file (or its diff) in the reader, a theme other than the system's, and a full commit hash to brief from instead of the HEAD last seen (for checks).
- `floorplan scan <folder> [--pretty] [--commits N]` writes the scan model as JSON to stdout.
- `floorplan mcp [folder]` serves the shared board to a coding agent. The folder (the current one by default) is where the agent works: the base of its relative paths and its default project when no window is involved.
- `floorplan <folder> --smoke out.png [--size 1600x1000] [--settle 3]` opens the window, waits the given seconds after the first map, then writes a capture and `out.json` with timings and memory before exiting. Smoke windows never write FloorPlan's settings.

Register the board with Claude Code once, for every project:

```bash
claude mcp add --scope user floorplan -- "C:\path\to\floorplan.exe" mcp
```

User scope keeps the entry in your own Claude configuration; `--scope project` would write `.mcp.json` into a repository. A running window or MCP server locks its executable, so publish elsewhere while either is in use.

Front-end work in a browser: save a scan with `floorplan scan <folder> > web/.dev/scan.json`, run `npm run dev --prefix web`, and open `http://localhost:5173` (add `?theme=dark` or `?theme=light` to force a theme). To use the dev server inside the real window instead, set `FLOORPLAN_DEV_URL=http://localhost:5173/` before starting the executable.

Icons: a licensed Font Awesome 6 font placed at `%LOCALAPPDATA%\FloorPlan\fonts\icons.otf` is used when present; otherwise the bundled free icons are used.

`web/.npmrc` sets `legacy-peer-deps` to avoid an npm 11.5 crash while resolving Vitest's optional peer packages. Every peer the project needs is listed explicitly.

FloorPlan stores settings, logs and WebView2 data in `%LOCALAPPDATA%\FloorPlan`. It writes nothing to the projects it inspects.
