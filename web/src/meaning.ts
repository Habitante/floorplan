import { LINES_AREA, type Footprint, type Model, type Node } from './model'
import type { DescriptionReport, SystemEntry } from './types'

// Meaning on paths: the project description an agent wrote and the host checked against the scan.
// Each file belongs to at most one system; a folder is shown as the system that carries most of
// its weight. Pure functions; state.ts holds the report.

/**
 * The system of each node by index, -1 for none: a file's own, and a folder's when one system holds
 * more than half of its weight under the footprint, so a code on a folder never claims files that
 * the description leaves unnamed.
 */
export function systemsOf(m: Model, report: DescriptionReport | null, footprint: Footprint): Int16Array {
  const of = new Int16Array(m.nodes.length).fill(-1)
  const count = report?.description?.systems.length ?? 0
  if (!report?.systemOf || count === 0) return of
  const weights = new Float64Array(m.nodes.length * count)
  for (const node of m.nodes) {
    if (!node.isFile) continue
    const s = report.systemOf[node.src.id!] ?? -1
    if (s < 0 || s >= count) continue
    of[node.index] = s
    const w = footprint === 'lines' ? (node.kind && LINES_AREA.has(node.kind) ? node.lines : 0) : node.bytes
    for (let n = node.parent; n; n = n.parent) weights[n.index * count + s] += w
  }
  for (const node of m.nodes) {
    if (node.isFile) continue
    let best = -1
    let weight = 0
    for (let s = 0; s < count; s++) {
      const w = weights[node.index * count + s]
      if (w > weight) [best, weight] = [s, w]
    }
    const total = footprint === 'lines' ? node.lines : node.bytes
    of[node.index] = weight > total / 2 ? best : -1
  }
  return of
}

/** Marks a system's files and the folders holding them, for dimming the rest. */
export function systemMarks(m: Model, report: DescriptionReport | null, system: number | null): Uint8Array | null {
  if (system === null || !report?.systemOf) return null
  const marks = new Uint8Array(m.nodes.length)
  for (const node of m.nodes) {
    if (!node.isFile || report.systemOf[node.src.id!] !== system) continue
    for (let n: Node | null = node; n && !marks[n.index]; n = n.parent) marks[n.index] = 1
  }
  return marks
}

export interface SystemTotals {
  files: number
  /** Lines in code, tests and documentation, the Lines area. */
  lines: number
  bytes: number
}

/** Files, lines and bytes in each system. */
export function systemTotals(m: Model, report: DescriptionReport | null): SystemTotals[] {
  const count = report?.description?.systems.length ?? 0
  const totals = Array.from({ length: count }, () => ({ files: 0, lines: 0, bytes: 0 }))
  if (!report?.systemOf) return totals
  for (const node of m.nodes) {
    if (!node.isFile) continue
    const s = report.systemOf[node.src.id!] ?? -1
    if (s < 0 || s >= count) continue
    totals[s].files++
    totals[s].lines += node.lines
    totals[s].bytes += node.bytes
  }
  return totals
}

/** The system a file belongs to, or null. */
export function systemOfFile(report: DescriptionReport | null, node: Node): SystemEntry | null {
  if (!node.isFile || !report?.systemOf || !report.description) return null
  const s = report.systemOf[node.src.id!] ?? -1
  return s >= 0 ? report.description.systems[s] ?? null : null
}

/** A reference as agents write them, "path" or "path:from-to", split for the reader. */
export function splitReference(text: string): { path: string; from?: number; to?: number } {
  const m = /^(.+?):(\d+)(?:-(\d+))?$/.exec(text.trim())
  if (!m) return { path: text.trim().replace(/\\/g, '/') }
  const from = Number(m[2])
  return { path: m[1].replace(/\\/g, '/'), from, to: m[3] ? Number(m[3]) : from }
}

/**
 * The code the map prints for each system: the description's own, else one derived from the name
 * (a short first word such as "3D" or "UI", else the initials of its first two words). Codes stay
 * unique by a number.
 */
export function systemCodes(report: DescriptionReport | null): string[] {
  const used = new Set<string>()
  return (report?.description?.systems ?? []).map(s => {
    let code = s.code?.trim().slice(0, 3) || derivedCode(s.name)
    if (used.has(code.toUpperCase())) {
      const base = code.slice(0, 2)
      for (let n = 2; used.has(code.toUpperCase()); n++) code = `${base}${n}`
    }
    used.add(code.toUpperCase())
    return code
  })
}

function derivedCode(name: string): string {
  const words = name.split(/\s+/).filter(w => w && !/^(the|a|an)$/i.test(w))
  if (words.length === 0) return '?'
  if (words[0].length <= 3) return words[0].toUpperCase()
  return words.slice(0, 2).map(w => w[0]).join('').toUpperCase()
}

/**
 * The code to print on a map item under the systems lens, or -1: a region shows its system, and a
 * deeper folder or a file shows its own where it differs from the folder around it, so every file
 * reads as its own code or the nearest one above it.
 */
export function codeShown(systems: Int16Array, node: Node, depth: number): number {
  const s = systems[node.index]
  if (s < 0) return -1
  const around = depth <= 1 || !node.parent ? -1 : systems[node.parent.index]
  return s === around ? -1 : s
}

/** Colour slots for systems: five that stay apart; a sixth or later system shares the neutral fourth, and its code tells it apart. */
export const SYSTEM_SLOTS = 5
export const systemSlot = (system: number) => (system < SYSTEM_SLOTS ? system : 3)

export const BASIS_LABEL: Record<string, string> = {
  observed: 'Observed in the files',
  declared: "Declared in the project's documents",
  inferred: "Inferred by the description's writer",
}
