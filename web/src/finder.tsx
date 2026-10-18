import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'preact/hooks'
import { changeLabel, changeMark } from './changes'
import { find, type Found } from './find'
import * as format from './format'
import { Icon, Tag } from './icons'
import { weightOf, type Footprint, type Model } from './model'
import { price } from './render'
import { finding, footprint, jumpTo } from './state'

const LIMIT = 50
const PAGE = 8

/** Ctrl+P: finds a file or folder by name and shows it on the map. Invoked, never persistent. */
export function Finder({ model: m }: { model: Model }) {
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const panel = useRef<HTMLDivElement>(null)
  const input = useRef<HTMLInputElement>(null)
  const list = useRef<HTMLUListElement>(null)
  const fp = footprint.value
  const { found, total } = useMemo(() => find(m, query, LIMIT, weightOf(fp)), [m, query, fp])
  const current = Math.min(active, found.length - 1)
  const close = () => (finding.value = false)
  const choose = (f: Found) => {
    close()
    jumpTo(f.node)
  }

  // Before the first paint, so letters typed right after Ctrl+P land in the input.
  useLayoutEffect(() => {
    const before = document.activeElement
    input.current?.focus()
    const onClick = (e: MouseEvent) => {
      if (!panel.current?.contains(e.target as globalThis.Node)) close()
    }
    document.addEventListener('click', onClick)
    return () => {
      document.removeEventListener('click', onClick)
      if (before instanceof HTMLElement && before.isConnected) before.focus()
    }
  }, [])

  useEffect(() => {
    list.current?.children[current]?.scrollIntoView({ block: 'nearest' })
  }, [current, found])

  const move = (step: number, wrap: boolean) => {
    if (found.length === 0) return
    const next = current + step
    setActive(wrap ? (next + found.length) % found.length : Math.max(0, Math.min(found.length - 1, next)))
  }

  const onKey = (e: KeyboardEvent) => {
    switch (e.key) {
      case 'ArrowDown':
      case 'ArrowUp':
        move(e.key === 'ArrowDown' ? 1 : -1, true)
        break
      case 'PageDown':
      case 'PageUp':
        move(e.key === 'PageDown' ? PAGE : -PAGE, false)
        break
      case 'Enter':
        if (found[current]) choose(found[current])
        break
      case 'Escape':
        close()
        break
      case 'Tab':
        // Focus stays in the finder until it closes.
        break
      default:
        if (!e.ctrlKey || e.key.toLowerCase() !== 'p') return
        input.current?.select()
    }
    e.preventDefault()
    e.stopPropagation()
  }

  const searching = query.trim().length > 0
  return (
    <div class="finder" ref={panel} role="dialog" aria-label="Find a file or folder">
      <div class="finder-input">
        <Icon name="search" />
        <input
          ref={input}
          value={query}
          placeholder="Find a file or folder"
          role="combobox"
          aria-label="Find a file or folder"
          aria-expanded={found.length > 0}
          aria-controls="finder-results"
          aria-activedescendant={found.length > 0 ? `finder-${current}` : undefined}
          aria-autocomplete="list"
          autocomplete="off"
          spellcheck={false}
          onInput={e => {
            setQuery(e.currentTarget.value)
            setActive(0)
          }}
          onKeyDown={onKey}
        />
      </div>
      {found.length > 0 && (
        <ul class="finder-results" id="finder-results" role="listbox" aria-label="Matches" ref={list}>
          {found.map((f, i) => (
            <Row key={f.node.path} found={f} id={`finder-${i}`} active={i === current} footprint={fp} onHover={() => setActive(i)} onPick={() => choose(f)} />
          ))}
        </ul>
      )}
      {searching && found.length === 0 && <div class="finder-note">No match</div>}
      {total > found.length && <div class="finder-note num">+{format.int(total - found.length)} more</div>}
    </div>
  )
}

function Row(props: { found: Found; id: string; active: boolean; footprint: Footprint; onHover: () => void; onPick: () => void }) {
  const { node, positions } = props.found
  const nameAt = node.path.length - node.name.length
  const change = node.src.change
  // Under this footprint it has no area, so the map marks its nearest folder instead.
  const empty = weightOf(props.footprint)(node) === 0
  return (
    <li
      id={props.id}
      class={empty ? 'finder-row faded' : 'finder-row'}
      role="option"
      aria-selected={props.active}
      title={empty ? `No area under ${props.footprint === 'lines' ? 'Lines' : 'Bytes'}` : undefined}
      onMouseMove={() => !props.active && props.onHover()}
      onClick={props.onPick}
    >
      {change
        ? <span class="change-mark" role="img" aria-label={changeLabel(change)}>{changeMark(change)}</span>
        : <Icon name={node.isFile ? 'file' : 'folder'} />}
      <span class="finder-name"><Marked text={node.name} offset={nameAt} positions={positions} /></span>
      <span class="finder-folder"><Marked text={node.path.slice(0, Math.max(0, nameAt - 1))} offset={0} positions={positions} /></span>
      <span class="row-value num"><Tag tag={price(node, props.footprint, true)} /></span>
    </li>
  )
}

/** Text with the matched characters marked; positions count from the start of the path. */
function Marked({ text, offset, positions }: { text: string; offset: number; positions: readonly number[] }) {
  const at = new Set(positions)
  const pieces = []
  for (let i = 0; i < text.length;) {
    const on = at.has(offset + i)
    let j = i
    while (j < text.length && at.has(offset + j) === on) j++
    pieces.push(on ? <mark>{text.slice(i, j)}</mark> : text.slice(i, j))
    i = j
  }
  return <>{pieces}</>
}
