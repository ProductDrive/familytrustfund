import { formatNaira } from './formatNaira'

export function MoneyDisplay({
  amount,
  className,
}: {
  amount: number | null | undefined
  className?: string
}) {
  return (
    <span className={className ? `${className} money` : 'money'}>
      {formatNaira(amount)}
    </span>
  )
}