import { useEffect, useState } from 'preact/hooks'
import { request } from './bridge'
import * as format from './format'
import { Icon } from './icons'
import type { Model, Node } from './model'
import { BLOCKS } from './parts'
import { openReader, readerFocus, selectedPart, type PartChoice } from './state'

/** Where a name appears in the project's text (FloorPlan.Core's MentionReport). */
interface MentionReport {
  name: string
  /** Most mentions first: the file, how many lines mention the name, and the first of them. */
  files: { path: string; count: number; lines: number[] }[]
  filesNotListed?: number
}

const MENTIONS = 'Mentioned in: other files whose text has this name as a whole word. A mention is not a call, and a call by a built string is not a mention.'
const SHOWN = 3
/** Parts that have no name of their own to be mentioned by. */
const NAMELESS = new Set(['section', 'top', 'file', 'test', 'main', ...Object.values(BLOCKS)])

/** The name a part is mentioned by: its own name, the last segment of a qualified one. */
export function mentionName(part: Pick<PartChoice, 'id' | 'name' | 'kind'>): string | null {
  if (part.id === null || NAMELESS.has(part.kind)) return null
  const name = part.name.slice(part.name.lastIndexOf('.') + 1)
  return /^[\p{L}_$][\p{L}\p{N}_$]{1,99}$/u.test(name) ? name : null
}

/** For a part picked on the map inside the file the reader shows: the other files that mention its name. */
export function Mentions({ model: m, node }: { model: Model; node: Node }) {
  const part = selectedPart.value
  const name = part && part.path === node.path ? mentionName(part) : null
  const [report, setReport] = useState<MentionReport | null>(null)
  const [expanded, setExpanded] = useState(false)
  useEffect(() => {
    setReport(null)
    setExpanded(false)
    if (!name) return
    let live = true
    request<{ mentions?: MentionReport | null }>({ type: 'mentions', name, path: node.path })
      .then(r => live && setReport(r.mentions ?? null), () => {})
    return () => { live = false }
  }, [name, node.path, m])
  if (!name || !report || report.name !== name) return null

  const label = <Icon name="mentions" label={`Mentions of ${name}`} />
  if (report.files.length === 0) {
    return (
      <div class="related">
        <div class="related-line" title={MENTIONS}>{label}<span class="related-items related-none">no other file mentions {name}</span></div>
      </div>
    )
  }
  const shown = expanded ? report.files : report.files.slice(0, SHOWN)
  const hidden = report.files.length - shown.length
  const unlisted = report.filesNotListed ?? 0
  const open = (target: Node, line: number | undefined) => {
    if (line) readerFocus.value = { path: target.path, from: line, to: line }
    openReader(target)
  }
  return (
    <div class="related">
      <div class="related-line" title={MENTIONS}>
        {label}
        <span class="related-items">
          {shown.map(f => {
            const target = m.byPath.get(f.path)
            return (
              <span class="related-item">
                <button class="related-name" disabled={!target} title={`${f.path}: ${format.int(f.count)} lines, from line ${f.lines.join(', ')}`}
                  onClick={() => target && open(target, f.lines[0])}>
                  {target?.name ?? f.path}
                </button>
                <span class="related-count num" aria-label={`${format.int(f.count)} lines`}>{format.int(f.count)}</span>
              </span>
            )
          })}
          {hidden > 0 && (
            <button class="related-more num" aria-expanded={expanded} onClick={() => setExpanded(true)}>+{format.int(hidden + unlisted)}</button>
          )}
          {hidden === 0 && unlisted > 0 && <span class="related-more num">and {format.int(unlisted)} more</span>}
        </span>
      </div>
    </div>
  )
}
