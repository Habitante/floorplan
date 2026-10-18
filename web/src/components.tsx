import { useSignal } from '@preact/signals'
import { useEffect, useRef, useState } from 'preact/hooks'
import { ACTIVITY_STEP_LABELS, AGE_STEP_LABELS } from './activity'
import { describeItem, pointedNodes } from './board'
import { dismissPointer, goTo } from './board-controller'
import { send } from './bridge'
import { Finder } from './finder'
import * as format from './format'
import { HistoryController } from './history-controller'
import { Icon, Tag } from './icons'
import { MapController } from './map-controller'
import { ancestry, contains, displayName, KINDS, LINES_AREA, weightOf, type Model, type Node } from './model'
import { changeCounts, changeLabel, changeMark, changeWords, describeChange } from './changes'
import { price } from './render'
import { names, onSince, people, regionName, sinceRange } from './since'
import {
  activity, age, dismissSince, finding, focus, footprint, lens, model, openReader, pointers, range, reader, readerFocus, reveal, selected,
  selectedCommit, since, status, toggleSince, zoomTo, type Pointer,
} from './state'
import type { FileKind, PointerItem, Tally } from './types'

const KIND_LABEL: Record<FileKind, string> = { code: 'Code', test: 'Tests', docs: 'Docs', data: 'Data', asset: 'Assets', other: 'Other' }

export function App() {
  const m = model.value
  return (
    <div class={m ? 'app' : 'app empty'}>
      <TopBar />
      {m ? <><MapView /><Inspector /><History /><SideResizer /></> : <Welcome />}
    </div>
  )
}

function TopBar() {
  const m = model.value
  const s = status.value
  const f = focus.value
  const info = useSignal(false)
  const project = m?.scan.project

  return (
    <header class="top">
      <button class="icon-button" title="Open folder (Ctrl+O)" aria-label="Open folder" onClick={() => send({ type: 'open' })}>
        <Icon name="folderOpen" />
      </button>
      {m && (
        <button
          class="icon-button"
          title="Find a file or folder (Ctrl+P)"
          aria-label="Find a file or folder"
          aria-expanded={finding.value}
          onClick={e => {
            e.stopPropagation()
            finding.value = !finding.value
          }}
        >
          <Icon name="search" />
        </button>
      )}
      {m && finding.value && <Finder model={m} />}
      {m && f && (
        <nav class="crumbs" aria-label="Location">
          {ancestry(f).map((n, i) => (
            <>
              {i > 0 && <span class="crumb-sep">/</span>}
              <button class="crumb" aria-current={n === f ? 'location' : undefined} onClick={() => zoomTo(n)}>
                {displayName(m, n)}
              </button>
            </>
          ))}
        </nav>
      )}
      {project?.branch && (
        <span class="meta" title={project.head}>
          <Icon name="branch" />{project.branch}
        </span>
      )}
      <span class="spacer" />
      {m && (
        <>
          <Segmented
            label="Area"
            value={footprint.value}
            options={[['lines', 'Lines'], ['bytes', 'Bytes']]}
            onChange={v => (footprint.value = v)}
          />
          <Segmented
            label="Fill"
            value={lens.value}
            options={[['activity', 'Activity'], ['age', 'Age'], ['kind', 'Kind']]}
            onChange={v => (lens.value = v)}
          />
          <button class={s.refreshing ? 'icon-button busy' : 'icon-button'} title="Rescan (F5)" aria-label="Rescan" onClick={() => send({ type: 'rescan' })}>
            <Icon name="rotate" />
          </button>
          <button
            class="icon-button"
            aria-label="Map information"
            aria-expanded={info.value}
            onClick={e => {
              e.stopPropagation()
              info.value = !info.value
            }}
          >
            <Icon name="info" />
          </button>
          {info.value && <InfoPanel model={m} close={() => (info.value = false)} />}
        </>
      )}
      {s.state === 'scanning' && m && <Progress done={s.done} total={s.total} />}
    </header>
  )
}

const SIDE_WIDTH = 'map.sideWidth'

function applySideWidth(px: number) {
  document.documentElement.style.setProperty('--side-width', `${Math.round(px)}px`)
}

try {
  const saved = Number(localStorage.getItem(SIDE_WIDTH))
  if (saved > 0) applySideWidth(saved)
} catch {
  // A remembered width is a convenience.
}

/** Drag handle on the side panel's left edge. The map keeps its width while panels change. */
function SideResizer() {
  const onDown = (e: PointerEvent) => {
    const handle = e.currentTarget as HTMLElement
    handle.setPointerCapture(e.pointerId)
    let width = 0
    const move = (ev: PointerEvent) => {
      width = Math.min(innerWidth * 0.75, Math.max(260, innerWidth - ev.clientX))
      applySideWidth(width)
    }
    const up = () => {
      handle.removeEventListener('pointermove', move)
      try {
        if (width > 0) localStorage.setItem(SIDE_WIDTH, String(Math.round(width)))
      } catch {
        // Convenience only.
      }
    }
    handle.addEventListener('pointermove', move)
    handle.addEventListener('pointerup', up, { once: true })
  }
  return <div class="side-resizer" role="separator" aria-orientation="vertical" aria-label="Resize panel" onPointerDown={onDown} />
}

function Segmented<T extends string>(props: { label: string; value: T; options: [T, string][]; onChange: (v: T) => void }) {
  return (
    <div class="segmented" role="group" aria-label={props.label} title={props.label}>
      {props.options.map(([value, text]) => (
        <button aria-pressed={props.value === value} onClick={() => props.onChange(value)}>{text}</button>
      ))}
    </div>
  )
}

function Progress({ done, total }: { done?: number; total?: number }) {
  const known = total !== undefined && total > 0 && done !== undefined
  return (
    <div class={known ? 'progress' : 'progress indeterminate'} role="progressbar" aria-label="Scanning">
      <div style={known ? { width: `${Math.round((done! / total!) * 100)}%` } : undefined} />
    </div>
  )
}

function InfoPanel({ model: m, close }: { model: Model; close: () => void }) {
  const panel = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const onDown = (e: MouseEvent) => {
      if (!panel.current?.contains(e.target as globalThis.Node)) close()
    }
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && close()
    document.addEventListener('click', onDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('click', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [])
  const p = m.scan.project
  // Age is measured from the range's newest commit.
  const newest = m.commits[range.value[1] - 1]
  const swatch = (name: string) => <span class="swatch" style={{ background: `var(${name})` }} />

  return (
    <div class="popover" ref={panel} role="dialog" aria-label="Map information">
      <h3>Area</h3>
      <p><Icon name="lines" /> <b>Lines</b>: physical lines in code, test and documentation files, blank lines and comments included.</p>
      <p><b>Bytes</b>: size on disk. Data, scenes and binary files only take area here.</p>
      <h3>Fill</h3>
      {lens.value === 'kind' ? (
        <>
          <div class="legend">
            {swatch('--kind-code')}<span>Code</span>
            {swatch('--kind-test')}<span>Tests</span>
            {swatch('--kind-docs')}<span>Docs</span>
            {swatch('--kind-asset')}<span>Assets</span>
            {swatch('--kind-other')}<span>Data and other</span>
          </div>
          <p>Tests: code in a test, tests, __tests__, spec or specs folder, or named like test_x, x_test, x.test, x.spec or XTests.</p>
        </>
      ) : p.git ? (
        <>
          {lens.value === 'activity'
            ? <p>Commits in the selected history range touching each file or folder. A commit counts once per folder.</p>
            : <p>Time from the last commit touching each file or folder to the newest commit in the selected range{newest ? `, ${format.dateShort(format.fromUnix(newest.time))}` : ''}. Within:</p>}
          <div class="legend">
            {(lens.value === 'activity' ? ACTIVITY_STEP_LABELS : AGE_STEP_LABELS).map((label, i) => (
              <>{swatch(i === 0 ? '--zero' : `--act-${i}`)}<span>{label}</span></>
            ))}
            {lens.value === 'age' && <><span class="swatch hatch" /><span>Unknown</span></>}
          </div>
          <p>Files at zero keep a dim tint of their kind.</p>
          {lens.value === 'age' && (
            <p>Unknown: no commit loaded up to that one, because the file came later, was never committed or is older than the loaded history.</p>
          )}
        </>
      ) : (
        <div class="legend"><span class="swatch hatch" /><span>No Git history</span></div>
      )}
      <h3>Outlines</h3>
      <div class="legend">
        {p.git && (
          <>
            <span class="swatch outline" /><span>Modified, uncommitted</span>
            <span class="swatch outline new" /><span>New, uncommitted (a renamed file is new at its new path)</span>
            <span class="counts">+ ~ −</span><span>New, modified and deleted files, counted in region headers</span>
          </>
        )}
        <span class="swatch outline pointer" /><span>An agent's pointer</span>
      </div>
      <h3>Scan</h3>
      <p>
        {p.scope === 'folder' ? 'All files in the folder' : p.scope === 'tracked' ? 'Files in the working tree, new files hidden' : 'Files in the working tree, new files included'}
        {' · '}{format.int(m.root.files)} files
        {p.head && <> · <span class="mono">{p.head.slice(0, 7)}</span></>}
      </p>
      {p.git && (
        <p>
          {format.int(m.commits.length)} commits{p.historyComplete ? '' : ` (latest ${format.int(p.commitLimit)})`}.
          Merges excluded; renames count as a delete and an add.
        </p>
      )}
      {p.git && (
        <label class="toggle" title="New files that Git does not track yet; staged ones always show">
          <input
            type="checkbox"
            checked={p.scope === 'tracked+untracked'}
            onChange={e => send({ type: 'scope', untracked: (e.target as HTMLInputElement).checked })}
          />
          Show new files{p.untrackedHidden > 0 ? ` (${format.int(p.untrackedHidden)} hidden)` : ''}
        </label>
      )}
      <p class="muted">{format.dateLong(new Date(p.scannedAt))} · {format.int(p.durationMs)} ms</p>
      {m.scan.issues.length > 0 && (
        <>
          <h3>Issues</h3>
          {m.scan.issues.slice(0, 20).map(i => <p>{i.path ? <span class="mono">{i.path}: </span> : null}{i.message}</p>)}
        </>
      )}
    </div>
  )
}

function Welcome() {
  const s = status.value
  return (
    <main class="welcome">
      {s.state === 'scanning' ? (
        <>
          <div>Scanning {s.path ? s.path.split(/[\\/]/).pop() : ''}{s.total ? ` · ${format.int(s.done ?? 0)} / ${format.int(s.total)}` : ''}</div>
          <div class="bar"><div style={{ width: s.total ? `${((s.done ?? 0) / s.total) * 100}%` : '15%' }} /></div>
        </>
      ) : (
        <>
          {s.state === 'error' && <div class="error">{s.message}</div>}
          <button class="open-big" onClick={() => send({ type: 'open' })}>
            <Icon name="folderOpen" />Open folder
          </button>
        </>
      )}
    </main>
  )
}

function MapView() {
  const host = useRef<HTMLDivElement>(null)
  const canvas = useRef<HTMLCanvasElement>(null)
  const tip = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const controller = new MapController(host.current!, canvas.current!, tip.current!)
    return () => controller.dispose()
  }, [])
  return (
    <div class="map" ref={host}>
      <canvas ref={canvas} tabIndex={0} aria-label="Project map: area shows size, colour shows the selected measure" />
      <div class="tip" ref={tip} hidden />
    </div>
  )
}

/**
 * Agents' pointers: who points and why, beside the map rather than over it, so no fill is
 * hidden. The row has room for the start of a caption; clicking it opens the whole note with the
 * outlined paths.
 */
function PointerChips({ model: m }: { model: Model }) {
  const list = pointers.value
  const f = focus.value
  const reading = useSignal<string | null>(null)
  if (list.length === 0) return null
  const open = list.find(p => p.agent === reading.value)
  return (
    <>
      <ul class="pointers" aria-label="Agents pointing at the map">
        {list.map(p => {
          const nodes = pointedNodes(m, [p])
          const outside = f ? nodes.filter(n => !contains(f, n)).length : 0
          const detail = [
            `${p.agent} · ${format.dateLong(new Date(p.at))}`,
            p.caption,
            ...p.items.slice(0, 12).map(i => (i.note ? `${i.note} (not on the map; ${i.path} outlined)` : describeItem(i))),
            p.items.length > 12 ? `+${format.int(p.items.length - 12)} more` : '',
            outside > 0 ? `${format.int(outside)} outside this view` : '',
          ].filter(Boolean).join('\n')
          return (
            <li class="pointer-chip" key={p.agent}>
              <button
                class="pointer-main"
                title={detail}
                aria-expanded={p === open}
                onClick={e => {
                  e.stopPropagation()
                  reading.value = p === open ? null : p.agent
                }}
              >
                <Icon name="pointer" />
                <span class="pointer-agent">{p.agent}</span>
                {p.caption && <span class="pointer-caption">{p.caption}</span>}
              </button>
              <button class="icon-button" aria-label={`Show what ${p.agent} points at`} title="Show on the map" onClick={() => goTo(p)}>
                <Icon name="locate" />
              </button>
              <button class="icon-button" aria-label={`Dismiss ${p.agent}'s pointer`} title="Dismiss" onClick={() => dismissPointer(p.agent)}>
                <Icon name="close" />
              </button>
            </li>
          )
        })}
      </ul>
      {open && <PointerNote model={m} pointer={open} close={() => (reading.value = null)} />}
    </>
  )
}

/**
 * An agent's whole note, over the foot of the map until closed, with the paths it outlines:
 * clicking one reads it, at the lines pointed at, or selects a folder.
 */
function PointerNote({ model: m, pointer: p, close }: { model: Model; pointer: Pointer; close: () => void }) {
  const panel = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const onClick = (e: MouseEvent) => {
      if (!panel.current?.contains(e.target as globalThis.Node)) close()
    }
    // Ahead of the window's own Escape, which would close the reader.
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return
      e.stopPropagation()
      close()
    }
    document.addEventListener('click', onClick)
    addEventListener('keydown', onKey, { capture: true })
    return () => {
      document.removeEventListener('click', onClick)
      removeEventListener('keydown', onKey, { capture: true })
    }
  }, [])
  const showItem = (item: PointerItem, node: Node) => {
    if (!node.isFile) return reveal(node)
    if (item.from !== undefined) readerFocus.value = { path: item.path, from: item.from, to: item.to ?? item.from }
    openReader(node)
  }
  return (
    <div class="popover pointer-note" ref={panel} role="dialog" aria-label={`${p.agent}'s note`}>
      <div class="eyebrow">{p.agent} · {format.dateLong(new Date(p.at))}</div>
      {p.caption && <p class="pointer-text">{p.caption}</p>}
      <ul class="rows">
        {p.items.map(item => {
          const node = m.byPath.get(item.path)
          if (!node) return <li class="row faded"><Icon name="file" /><span class="row-name">{describeItem(item)}</span><span /></li>
          return (
            <li>
              <button
                class="row"
                title={item.note ? `Not on the map; ${item.path} outlined` : describeItem(item)}
                onClick={() => showItem(item, node)}
              >
                <Icon name={node.isFile ? 'file' : 'folder'} />
                <span class={item.note ? 'row-name muted' : 'row-name'}>{describeItem(item)}</span>
                {/* Off the map, the outlined folder's measure is not the named path's. */}
                <span class="row-value num">{!item.note && <Tag tag={price(node, footprint.value, true)} />}</span>
              </button>
            </li>
          )
        })}
      </ul>
    </div>
  )
}

/**
 * What landed while the human was away: when they were last here, how many commits, where and by
 * whom, with the counts in the tooltip. Pressed while the range shows those commits; pressing it
 * switches between them and the range before.
 */
function SinceChip({ model: m }: { model: Model }) {
  const s = since.value
  if (!s) return null
  const target = sinceRange(m, s)
  const on = onSince(m, s, range.value)
  const seenAt = new Date(s.seenAt)
  const line = [names(s.regions, 3, regionName, s.moreRegions), people(s)].filter(Boolean).join(' · ')
  const tally = (list?: Tally[], more?: number) =>
    [...(list ?? []).map(t => `${regionName(t.name)} ${format.int(t.commits)}`), ...(more ? [`+${format.int(more)} more`] : [])].join(', ')
  const detail = [
    `Since you were last here: ${format.dateLong(seenAt)}, ${s.seenHead}`,
    s.problem ?? `${format.int(s.commits)} commits${s.beyondHistory ? ', some older than the loaded history' : ''}`,
    tally(s.regions, s.moreRegions),
    tally(s.authors, s.moreAuthors),
    s.coAuthors ? `Co-authors: ${tally(s.coAuthors, s.moreCoAuthors)}` : '',
  ].filter(Boolean).join('\n')
  const dismiss = () => {
    dismissSince()
    send({ type: 'sinceDismissed' })
  }
  return (
    <div class="since-chip">
      <button
        class="since-main num"
        aria-pressed={on}
        aria-disabled={!target}
        aria-label={detail.replaceAll('\n', '. ')}
        title={detail}
        onClick={toggleSince}
      >
        <Icon name="since" />
        <span>{format.moment(seenAt)}</span>
        {s.problem
          ? <span class="since-text">not in this history</span>
          : <><span><Icon name="commit" />{format.int(s.commits)}</span>{line && <span class="since-text">{line}</span>}</>}
      </button>
      <button class="icon-button" aria-label="Dismiss what landed since you were last here" title="Dismiss" onClick={dismiss}>
        <Icon name="close" />
      </button>
    </div>
  )
}

function History() {
  const m = model.value
  const host = useRef<HTMLDivElement>(null)
  const canvas = useRef<HTMLCanvasElement>(null)
  const tip = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!host.current) return
    const controller = new HistoryController(host.current, canvas.current!, tip.current!)
    return () => controller.dispose()
  }, [m !== null && m.commits.length > 0])
  const history = m !== null && m.commits.length > 0
  if (!m || (!history && pointers.value.length === 0)) return null

  const [from, to] = range.value
  const first = m.commits[from]
  const last = m.commits[to - 1]
  return (
    <section class="history" aria-label="History">
      <div class="history-head num">
        {history && <span><Icon name="commit" />{format.int(to - from)} / {format.int(m.commits.length)}</span>}
        {history && m.changed.length > 0 && (
          <button
            class="pending num"
            title="Uncommitted changes"
            aria-pressed={selectedCommit.value === 'working'}
            onClick={() => (selectedCommit.value = selectedCommit.value === 'working' ? null : 'working')}
          >
            <Icon name="change" label="Uncommitted changes" />{format.int(m.changed.length)}
          </button>
        )}
        {first && last && <span>{format.dateShort(format.fromUnix(first.time))} – {format.dateShort(format.fromUnix(last.time))}</span>}
        <SinceChip model={m} />
        <PointerChips model={m} />
      </div>
      {history && (
        <div class="history-strip" ref={host}>
          <canvas ref={canvas} aria-label="Commit timeline: drag to choose the commits counted by Activity" />
          <div class="tip" ref={tip} hidden />
        </div>
      )}
    </section>
  )
}

type ReaderComponent = typeof import('./reader').Reader
let loadedReader: ReaderComponent | null = null

/** The reader (and its editor) loads on first use, keeping it out of the startup bundle. */
function LazyReader(props: Parameters<ReaderComponent>[0]) {
  const [Reader, setReader] = useState<ReaderComponent | null>(() => loadedReader)
  useEffect(() => {
    if (!Reader) import('./reader').then(module => setReader(() => (loadedReader = module.Reader)))
  }, [])
  return Reader ? <Reader {...props} /> : <aside class="side reader" aria-label="Reader" />
}

function Inspector() {
  const m = model.value
  if (!m) return null
  const open = reader.value
  if (open) return <LazyReader model={m} target={open} />
  const c = selectedCommit.value
  if (c === 'working') return <WorkingPanel model={m} />
  if (c !== null && m.commits[c]) return <CommitPanel model={m} index={c} />
  return <NodePanel model={m} node={selected.value ?? focus.value ?? m.root} />
}

function NodePanel({ model: m, node }: { model: Model; node: Node }) {
  const counts = activity.value
  const fp = footprint.value
  const src = node.src
  const [from, to] = range.value
  const modified = src.modified ? new Date(src.modified) : null
  // The age lens measures the last commit, so the inspector shows that instead of the file date.
  const last = lens.value === 'age' ? age.value?.times[node.index] ?? 0 : 0
  const lastCommit = last > 0 ? format.fromUnix(last) : null

  const commits: number[] = []
  if (counts && counts[node.index] > 0) {
    const inside = (id: number) => {
      for (let n: Node | null = m.byFileId[id] ?? null; n; n = n.parent) if (n === node) return true
      return false
    }
    for (let i = to - 1; i >= from && commits.length < 10; i--) if (m.commits[i].files.some(inside)) commits.push(i)
  }

  const weight = weightOf(fp)
  const children = node.isFile ? [] : [...node.children].sort((a, b) => weight(b) - weight(a) || a.name.localeCompare(b.name))
  const shown = children.filter(child => weight(child) > 0).slice(0, 12)
  const hidden = children.filter(child => weight(child) === 0).length
  const table = fp === 'lines' ? m.kindLines : m.kindBytes
  const mix = KINDS.map((kind, k) => ({ kind, value: table[node.index * KINDS.length + k] })).filter(x => x.value > 0)

  return (
    <aside class="side" aria-label="Details">
      <div class="side-head">
        <h2 class="side-title"><Icon name={node.isFile ? 'file' : 'folder'} />{displayName(m, node)}</h2>
        {node.isFile && (
          <button class="icon-button" title="Read (double-click on the map)" aria-label="Read file" onClick={() => openReader(node)}>
            <Icon name="fileLines" />
          </button>
        )}
        {selected.value && (
          <button class="icon-button" aria-label="Clear selection" onClick={() => (selected.value = null)}><Icon name="close" /></button>
        )}
      </div>
      {node.path && <div class="side-path mono">{node.path}</div>}

      <div class="price num">
        {!node.isFile && <span><Icon name="file" label="Files" />{format.int(node.files)}</span>}
        {(node.lines > 0 || (src.kind && LINES_AREA.has(src.kind))) && <Tag tag={{ lines: true, text: format.int(node.lines) }} />}
        {src.kind === 'data' && src.lines !== undefined && <Tag tag={{ lines: true, text: format.int(src.lines) }} />}
        {src.bytes !== undefined && <span>{format.bytes(node.bytes)}</span>}
        {m.scan.project.git && counts && <span><Icon name="commit" label="Commits in range" />{format.int(counts[node.index])}</span>}
        {lastCommit
          ? <span title={format.dateLong(lastCommit)}><Icon name="clock" label="Last commit" />{format.ago(lastCommit)}</span>
          : modified && <span title={format.dateLong(modified)}><Icon name="clock" label="Modified" />{format.ago(modified)}</span>}
      </div>
      {src.note && <div class="note">{src.note}</div>}
      {src.change && <div class="note change"><Icon name="change" /> {describeChange(node)}</div>}
      {!node.isFile && <ChangedInside model={m} node={node} />}
      {!node.isFile && (src.unmeasured ?? 0) > 0 && <div class="note">{format.int(src.unmeasured!)} files without a measurement</div>}

      {!node.isFile && mix.length > 1 && (
        <>
          <div class="section-title">Composition</div>
          <ul class="rows">
            {mix.map(x => (
              <li class="row">
                <span class="dot" style={{ background: `var(--kind-${x.kind === 'data' ? 'other' : x.kind})` }} />
                <span class="row-name">{KIND_LABEL[x.kind]}</span>
                <span class="row-value num">
                  <Tag tag={fp === 'lines' ? { lines: true, text: format.int(x.value) } : { lines: false, text: format.bytes(x.value) }} />
                </span>
              </li>
            ))}
          </ul>
        </>
      )}

      {shown.length > 0 && (
        <>
          <div class="section-title">Inside</div>
          <ul class="rows">
            {shown.map(child => (
              <li>
                <button class="row" onClick={() => (child.isFile ? openReader(child) : reveal(child))} onDblClick={() => !child.isFile && zoomTo(child)}>
                  <Icon name={child.isFile ? 'file' : 'folder'} />
                  <span class="row-name">{child.name}</span>
                  <span class="row-value num"><Tag tag={price(child, fp, false)} /></span>
                </button>
              </li>
            ))}
            {hidden > 0 && (
              <li class="row faded"><span /><span class="row-name">{format.int(hidden)} without {fp === 'lines' ? 'lines' : 'size'}</span><span /></li>
            )}
          </ul>
        </>
      )}

      {commits.length > 0 && (
        <>
          <div class="section-title">Recent commits</div>
          <ul class="rows">
            {commits.map(i => {
              const c = m.commits[i]
              return (
                <li>
                  <button class="row commit-row" onClick={() => (selectedCommit.value = i)} title={c.subject}>
                    <span class="sha mono">{c.sha.slice(0, 7)}</span>
                    <span class="row-name">{c.subject}</span>
                    <span class="row-value">{format.dateShort(format.fromUnix(c.time))}</span>
                  </button>
                </li>
              )
            })}
          </ul>
        </>
      )}
    </aside>
  )
}

function ChangedInside({ model: m, node }: { model: Model; node: Node }) {
  const inside = changeWords(changeCounts(m, node))
  if (!inside) return null
  return (
    <div class="note change">
      <Icon name="change" /> {inside}, uncommitted{' · '}
      <button class="link" onClick={() => (selectedCommit.value = 'working')}>show</button>
    </div>
  )
}

function WorkingPanel({ model: m }: { model: Model }) {
  const hidden = m.scan.project.untrackedHidden
  return (
    <aside class="side" aria-label="Uncommitted changes">
      <div class="side-head">
        <h2 class="side-title"><Icon name="change" />Uncommitted changes</h2>
        <button class="icon-button" aria-label="Close" onClick={() => (selectedCommit.value = null)}><Icon name="close" /></button>
      </div>
      <div class="price num">
        <span><Icon name="file" label="Files" />{format.int(m.changed.length)}</span>
        {hidden > 0 && (
          <button class="link" onClick={() => send({ type: 'scope', untracked: true })}>
            +{format.int(hidden)} new files hidden
          </button>
        )}
      </div>
      <ul class="rows" style={{ marginTop: '12px' }}>
        {m.changed.map(node => (
          <li>
            <button class="row" onClick={() => openReader(node, 'working')} title={`${node.path} · ${describeChange(node)}`}>
              <span class="change-mark" aria-label={changeLabel(node.src.change!)}>{changeMark(node.src.change!)}</span>
              <span class="row-name">{node.path}</span>
              <span class="row-value num"><Tag tag={price(node, footprint.value, true)} /></span>
            </button>
          </li>
        ))}
      </ul>
    </aside>
  )
}

function CommitPanel({ model: m, index }: { model: Model; index: number }) {
  const c = m.commits[index]
  const when = format.fromUnix(c.time)
  const files = c.files.map(id => m.byFileId[id]).filter(Boolean).sort((a, b) => a.path.localeCompare(b.path))
  return (
    <aside class="side" aria-label="Commit details">
      <div class="eyebrow"><span class="mono">{c.sha.slice(0, 7)}</span> · {format.dateLong(when)} · {c.author}</div>
      <div class="side-head">
        <h2 class="side-title">{c.subject}</h2>
        <button class="icon-button" aria-label="Close commit" onClick={() => (selectedCommit.value = null)}><Icon name="close" /></button>
      </div>
      <div class="price num">
        <span><Icon name="file" label="Files" />{format.int(files.length)}</span>
        {c.other > 0 && <span class="muted">+{format.int(c.other)} no longer present</span>}
      </div>
      <ul class="rows" style={{ marginTop: '12px' }}>
        {files.map(node => (
          <li>
            <button class="row" onClick={() => openReader(node, index)} title={node.path}>
              <Icon name="file" />
              <span class="row-name">{node.path}</span>
              <span class="row-value num"><Tag tag={price(node, footprint.value, true)} /></span>
            </button>
          </li>
        ))}
      </ul>
    </aside>
  )
}
