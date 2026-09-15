// Amount input helpers. Deliberately NOT type="number" (no spinner, keyboard
// entry only). Digits are comma-grouped as they are typed ("2,500,000.50") with
// the kobo portion entered after a single period.
export function sanitizeAmountInput(raw: string): string {
  let result = ''
  let seenDot = false
  for (const ch of raw) {
    if (ch >= '0' && ch <= '9') {
      result += ch
    } else if (ch === '.' && !seenDot) {
      seenDot = true
      result += ch
    }
  }
  const dotIndex = result.indexOf('.')
  if (dotIndex >= 0 && result.length - dotIndex - 1 > 2) {
    result = result.slice(0, dotIndex + 3)
  }
  return result
}

export function formatAmountInput(raw: string): string {
  const sanitized = sanitizeAmountInput(raw)
  const dotIndex = sanitized.indexOf('.')
  const whole = dotIndex >= 0 ? sanitized.slice(0, dotIndex) : sanitized
  const fraction = dotIndex >= 0 ? sanitized.slice(dotIndex) : ''
  const groupedWhole = whole ? Number(whole).toLocaleString('en-US') : ''
  return `${groupedWhole}${fraction}`
}

export function amountInputInvalid(raw: string, min = 1): boolean {
  const parsed = Number(raw)
  return !raw || Number.isNaN(parsed) || parsed < min
}