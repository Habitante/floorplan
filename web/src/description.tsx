import * as format from './format'
import { Icon, Tag } from './icons'
import { BASIS_LABEL, splitReference, systemCodes, systemOfFile, systemSlot, systemTotals } from './meaning'
import type { Model, Node } from './model'
import { description, footprint, openReader, readerFocus, reveal, selectedSystem } from './state'
import type { DescriptionReport, StaleAnchor } from './types'

// The project description beside the map: what its systems are, where they live and why they
// coexist, with the documents behind each claim. Picking a system lights its files on the map.

const swatch = (index: number) => <span class="swatch" style={{ background: `var(--sys-${systemSlot(index) + 1})` }} />
/** A system's code as the map prints it. */
const code = (text: string | undefined) => <span class="system-code">{text}</span>

/** Opens a referenced file at its lines, or shows a folder; a reference that names nothing is not offered. */
function Reference({ model: m, text, stale }: { model: Model; text: string; stale: ReadonlySet<string> }) {
  const ref = splitReference(text)
  const node = m.byPath.get(ref.path)
  const lines = ref.from !== undefined ? `:${ref.from}${ref.to !== ref.from ? `-${ref.to}` : ''}` : ''
  if (!node || stale.has(text)) {
    return <li class="row faded" title="No longer matches the project"><Icon name="file" /><span class="row-name">{text}</span><span class="row-value">stale</span></li>
  }
  const open = () => {
    if (!node.isFile) return reveal(node)
    if (ref.from !== undefined) readerFocus.value = { path: node.path, from: ref.from, to: ref.to ?? ref.from }
    openReader(node)
  }
  return (
    <li>
      <button class="row" title={text} onClick={open}>
        <Icon name={node.isFile ? 'file' : 'folder'} />
        <span class="row-name">{node.name}{lines}</span>
        <span class="row-value muted">{node.parent && node.parent.path ? node.parent.path : ''}</span>
      </button>
    </li>
  )
}

const staleSet = (report: DescriptionReport) => new Set((report.stale ?? []).map(s => s.anchor))

function Basis({ basis }: { basis?: string }) {
  return basis ? <span title={BASIS_LABEL[basis] ?? basis}>{basis}</span> : null
}

/** The whole description: the summary, its systems as the map's legend, the direction and the findings. */
export function DescriptionPanel({ model: m, report }: { model: Model; report: DescriptionReport }) {
  const d = report.description
  const totals = systemTotals(m, report)
  const codes = systemCodes(report)
  const stale = staleSet(report)
  const fp = footprint.value
  return (
    <aside class="side" aria-label="Project description">
      <div class="side-head">
        <h2 class="side-title"><Icon name="info" />{d?.project ?? m.scan.project.name}</h2>
      </div>
      {d?.written && (
        <div class="side-path" title={report.file}>
          {[d.written.by, d.written.at, d.written.head && `at ${d.written.head}`].filter(Boolean).join(' · ')}
        </div>
      )}
      {(report.problems ?? []).map(p => <div class="note">{p}</div>)}
      {d?.summary && <p class="description-text">{d.summary}</p>}

      {d && d.systems.length > 0 && (
        <>
          <div class="section-title">Systems</div>
          <ul class="rows">
            {d.systems.map((s, i) => (
              <li>
                <button class="row system-row" title={s.role} aria-pressed={selectedSystem.value === i} onClick={() => (selectedSystem.value = selectedSystem.value === i ? null : i)}>
                  {swatch(i)}
                  <span class="row-name">{code(codes[i])} {s.name}{s.status && <span class="muted"> · {s.status}</span>}</span>
                  <span class="row-value num">
                    <Tag tag={fp === 'lines' ? { lines: true, text: format.compact(totals[i].lines) } : { lines: false, text: format.bytes(totals[i].bytes) }} />
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </>
      )}

      {d && d.direction.length > 0 && (
        <>
          <div class="section-title">Direction</div>
          {d.direction.map(x => (
            <div class="description-entry">
              <p class="description-text">{x.text}</p>
              <ul class="rows">{x.docs.map(doc => <Reference model={m} text={doc} stale={stale} />)}</ul>
            </div>
          ))}
        </>
      )}

      {d && d.findings.length > 0 && (
        <>
          <div class="section-title">Findings</div>
          {d.findings.map(f => (
            <div class="description-entry">
              <div class="description-title">{f.title} <span class="muted"><Basis basis={f.basis} /></span></div>
              {f.text && <p class="description-text">{f.text}</p>}
              <ul class="rows">{f.paths.map(p => <Reference model={m} text={p} stale={stale} />)}</ul>
            </div>
          ))}
        </>
      )}

      {report.unnamed && (
        <>
          <div class="section-title" title="Code and tests that no system names, largest first">Not in a system</div>
          <ul class="rows">
            {report.unnamed.slice(0, 6).map(u => {
              const node = m.byPath.get(u.path)
              return (
                <li>
                  <button class="row" title={u.path || m.scan.project.name} disabled={!node} onClick={() => node && reveal(node)}>
                    <Icon name={node?.isFile ? 'file' : 'folder'} />
                    <span class="row-name">{u.path || m.scan.project.name}</span>
                    <span class="row-value num"><Tag tag={{ lines: true, text: format.compact(u.lines) }} /></span>
                  </button>
                </li>
              )
            })}
          </ul>
        </>
      )}

      {report.stale && <StaleList stale={report.stale} />}
    </aside>
  )
}

/** A picked system: what it is for, how much of the project it holds, its documents and anchors. */
export function SystemPanel({ model: m, report, index }: { model: Model; report: DescriptionReport; index: number }) {
  const s = report.description!.systems[index]
  const totals = systemTotals(m, report)[index]
  const stale = staleSet(report)
  const mine = (report.stale ?? []).filter(x => x.where.startsWith(`${s.id} `))
  const heading = report.description!.direction.filter(x => x.systems.includes(s.id))
  return (
    <aside class="side" aria-label={s.name}>
      <div class="side-head">
        <h2 class="side-title">{swatch(index)} {code(systemCodes(report)[index])} {s.name}</h2>
        <button class="icon-button" aria-label="Back to the description" title="Back (Esc)" onClick={() => (selectedSystem.value = null)}><Icon name="close" /></button>
      </div>
      <div class="side-path">{[s.status, s.basis].filter(Boolean).join(' · ')}</div>
      <div class="price num">
        <span><Icon name="file" label="Files" />{format.int(totals.files)}</span>
        <Tag tag={{ lines: true, text: format.int(totals.lines) }} />
        <span>{format.bytes(totals.bytes)}</span>
      </div>
      {s.role && <p class="description-text">{s.role}</p>}
      {heading.map(x => <p class="description-text muted">{x.text}</p>)}
      {s.docs.length > 0 && (
        <>
          <div class="section-title">Documents</div>
          <ul class="rows">{s.docs.map(doc => <Reference model={m} text={doc} stale={stale} />)}</ul>
        </>
      )}
      <div class="section-title" title="Where the description says this system lives; the most specific anchor wins where they overlap">Anchors</div>
      <ul class="rows mono-rows">
        {s.paths.map(p => (
          <li class={stale.has(p) ? 'row faded' : 'row'} title={stale.has(p) ? 'Matches no file' : undefined}>
            <span /><span class="row-name mono">{p}</span><span class="row-value">{stale.has(p) ? 'stale' : ''}</span>
          </li>
        ))}
      </ul>
      {mine.length > 0 && <StaleList stale={mine} />}
    </aside>
  )
}

function StaleList({ stale }: { stale: readonly StaleAnchor[] }) {
  return (
    <>
      <div class="section-title" title="Anchors that name nothing in this scan: the description needs updating">Stale</div>
      <ul class="rows">
        {stale.map(x => (
          <li class="row faded" title={x.reason}>
            <span /><span class="row-name"><span class="mono">{x.anchor}</span> · {x.where}</span><span class="row-value">{x.reason}</span>
          </li>
        ))}
      </ul>
    </>
  )
}

/** The system a file belongs to, as a line in the inspector; picking it lights the system. */
export function FileSystem({ node }: { node: Node }) {
  const report = description.value
  const s = systemOfFile(report, node)
  if (!report?.description || !s) return null
  const index = report.description.systems.indexOf(s)
  return (
    <div class="note">
      <button class="system-link" title={s.role} onClick={() => (selectedSystem.value = index)}>{swatch(index)} {code(systemCodes(report)[index])} {s.name}</button>
    </div>
  )
}

/** The systems lens's legend for the info panel. */
export function SystemLegend({ report }: { report: DescriptionReport }) {
  const systems = report.description?.systems ?? []
  const codes = systemCodes(report)
  return (
    <>
      <div class="legend">
        {systems.map((s, i) => <>{swatch(i)}<span>{code(codes[i])} {s.name}</span></>)}
      </div>
      <p>Systems as the project description names them, by path. A region shows its system's code, and a folder or file shows its own where it differs from the folder around it. Files the description does not name keep a dim tint of their kind.</p>
    </>
  )
}
