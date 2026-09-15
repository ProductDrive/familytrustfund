// UI date formatting. Dates render as "Nov 10, 2027"; datetimes additionally
// append a 12-hour time ("Nov 10, 2027, 3:30 PM").
function toDate(value: string | Date | null | undefined): Date | null {
  if (value == null) return null
  const d = value instanceof Date ? value : new Date(value)
  if (Number.isNaN(d.getTime())) return null
  return d
}

export function formatShortDate(value: string | Date | null | undefined): string {
  const d = toDate(value)
  if (!d) return '—'
  return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

export function formatShortDateTime(value: string | Date | null | undefined): string {
  const d = toDate(value)
  if (!d) return '—'
  const date = d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
  const time = d.toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' })
  return `${date}, ${time}`
}