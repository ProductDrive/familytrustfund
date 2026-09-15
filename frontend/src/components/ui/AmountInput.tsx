import { sanitizeAmountInput, formatAmountInput } from '../../lib/amountInputFormat'

// Currency amount input. Deliberately NOT type="number" (no spinner, keyboard
// entry only). Digits are comma-grouped as they are typed ("2,500,000.50") with
// the kobo portion entered after a single period.
export function AmountInput({
  id,
  value,
  onChange,
  placeholder,
  required = false,
}: {
  id: string
  value: string
  onChange: (raw: string) => void
  placeholder?: string
  required?: boolean
}) {
  return (
    <input
      id={id}
      type="text"
      inputMode="decimal"
      autoComplete="off"
      spellCheck={false}
      value={formatAmountInput(value)}
      onChange={(e) => onChange(sanitizeAmountInput(e.target.value))}
      placeholder={placeholder}
      required={required}
    />
  )
}