const integer = new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 })
const compactNumber = new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 })
const shortDate = new Intl.DateTimeFormat(undefined, { day: 'numeric', month: 'short' })
const longDate = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' })
const relative = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto', style: 'short' })

export const int = (n: number) => integer.format(n)
export const compact = (n: number) => (n < 1000 ? integer.format(n) : compactNumber.format(n))

const UNITS = ['B', 'KiB', 'MiB', 'GiB', 'TiB']

export function bytes(n: number): string {
  let value = n
  let unit = 0
  while (value >= 1024 && unit < UNITS.length - 1) {
    value /= 1024
    unit++
  }
  const digits = unit === 0 || value >= 100 ? 0 : 1
  return `${value.toFixed(digits)} ${UNITS[unit]}`
}

export const count = (n: number, short = false) => (short ? compact(n) : int(n))

export const dateShort = (d: Date) => shortDate.format(d)
export const dateLong = (d: Date) => longDate.format(d)

const clockTime = new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' })
const weekdayTime = new Intl.DateTimeFormat(undefined, { weekday: 'short', hour: 'numeric', minute: '2-digit' })
const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime()

/** A recent moment as briefly as stays clear: the time today, weekday and time this week, else the date. */
export function moment(d: Date, now = new Date()): string {
  const days = Math.round((startOfDay(now) - startOfDay(d)) / 86_400_000)
  if (days < 1) return clockTime.format(d)
  if (days < 7) return weekdayTime.format(d)
  return shortDate.format(d)
}

export function ago(d: Date, now = Date.now()): string {
  const seconds = (d.getTime() - now) / 1000
  const steps: [Intl.RelativeTimeFormatUnit, number][] = [
    ['second', 60], ['minute', 60], ['hour', 24], ['day', 7], ['week', 4.35], ['month', 12], ['year', Infinity],
  ]
  let value = seconds
  for (const [unit, size] of steps) {
    if (Math.abs(value) < size) return relative.format(Math.round(value), unit)
    value /= size
  }
  return relative.format(Math.round(value), 'year')
}

export const fromUnix = (seconds: number) => new Date(seconds * 1000)
