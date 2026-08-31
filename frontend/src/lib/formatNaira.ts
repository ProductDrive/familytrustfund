const naira = new Intl.NumberFormat('en-NG', {
  style: 'currency',
  currency: 'NGN',
  maximumFractionDigits: 2,
})

export function formatNaira(amount: number | null | undefined): string {
  if (amount == null) return '—'
  return naira.format(amount)
}