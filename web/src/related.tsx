import { useState } from 'preact/hooks'
import * as format from './format'
import { Icon } from './icons'
import type { Model, Node } from './model'
import { relatedGroups, relations, type Related } from './relations'
import { openReader, reader, selectedCommit } from './state'
import type { FileRelations } from './types'

const CHANGES_WITH = 'Changes with: files in the same commits over the loaded history; commits touching many files count for less. Changing together is not depending.'
const TESTS = 'Tests: paired by name or by changing together. Pairing is not coverage.'
const NAMED_FOR = 'Named for: the code this test is named after. Pairing is not coverage.'
const COMPACT_NAMES = 3

/**
 * The selected file's tests and the files that change with it: sections of rows in the
 * inspector, two short lines in the reader.
 */
export function RelatedFiles({ model: m, node, compact }: { model: Model; node: Node; compact: boolean }) {
  const r = relations.value
  const [expanded, setExpanded] = useState(false)
  const { paired, others } = relatedGroups(m, node, r)
  if (!r || (paired.length === 0 && others.length === 0)) return null
  const isTest = node.kind === 'test'
  const pairedTitle = isTest ? NAMED_FOR : TESTS

  if (compact) {
    const shown = expanded ? others : others.slice(0, COMPACT_NAMES)
    return (
      <div class="related">
        {paired.length > 0 && (
          <div class="related-line" title={pairedTitle}>
            <Icon name="test" label={isTest ? 'Named for' : 'Tests'} />
            <span class="related-items">{paired.map(x => <Name model={m} node={node} relations={r} related={x} />)}</span>
          </div>
        )}
        {others.length > 0 && (
          <div class="related-line" title={CHANGES_WITH}>
            <Icon name="related" label="Changes with" />
            <span class="related-items">
              {shown.map(x => <Name model={m} node={node} relations={r} related={x} />)}
              {others.length > COMPACT_NAMES && (
                <button class="related-more num" aria-expanded={expanded} onClick={() => setExpanded(!expanded)}>
                  {expanded ? 'fewer' : `+${format.int(others.length - COMPACT_NAMES)}`}
                </button>
              )}
            </span>
          </div>
        )}
      </div>
    )
  }

  const rows = (list: Related[]) => (
    <ul class="rows">
      {list.map(x => (
        <li class="related-row">
          <button class="row" title={evidence(r, x)} onClick={() => openReader(x.node)}>
            <Icon name="file" />
            <span class="row-name">{x.node.name}</span>
            <span class="row-value">{x.byName && !x.partner ? 'by name' : ''}</span>
          </button>
          {x.partner && <Count model={m} node={node} related={x} />}
        </li>
      ))}
    </ul>
  )
  return (
    <>
      {paired.length > 0 && <><div class="section-title" title={pairedTitle}>{isTest ? 'Named for' : 'Tests'}</div>{rows(paired)}</>}
      {others.length > 0 && <><div class="section-title" title={CHANGES_WITH}>Changes with</div>{rows(others)}</>}
    </>
  )
}

/** A related file's name in the reader's line, opening it, with how often they changed together. */
function Name({ model: m, node, relations: r, related: x }: { model: Model; node: Node; relations: FileRelations; related: Related }) {
  return (
    <span class="related-item">
      <button class="related-name" title={evidence(r, x)} onClick={() => openReader(x.node)}>{x.node.name}</button>
      {x.partner && <Count model={m} node={node} related={x} />}
    </span>
  )
}

/** How many commits they share; it leads to the newest of them. */
function Count({ model: m, node, related: x }: { model: Model; node: Node; related: Related }) {
  const p = x.partner!
  const newest = m.commits.findIndex(c => c.sha.startsWith(p.shared[0] ?? '\0'))
  const show = () => {
    if (newest < 0) return
    selectedCommit.value = newest
    if (reader.peek()?.node === node) reader.value = { node, commit: newest }
  }
  return (
    <button class="related-count num" title={`Show their newest shared commit, ${p.shared[0] ?? ''}`} aria-label={`Changed together in ${p.together} commits`} onClick={show}>
      <Icon name="commit" />{format.int(p.together)}
    </button>
  )
}

/** The evidence behind a relation, for its tooltip. */
function evidence(r: FileRelations, x: Related): string {
  const lines = [x.node.path]
  if (x.byName) lines.push('Paired by name')
  const p = x.partner
  if (p) {
    lines.push(`Changed together in ${format.int(p.together)} of this file's ${format.int(r.commits)} commits and ${format.int(p.together)} of its ${format.int(p.commits)}`)
    lines.push(`Shared: ${p.shared.join(', ')}${p.together > p.shared.length ? ', …' : ''}`)
  }
  return lines.join('\n')
}
