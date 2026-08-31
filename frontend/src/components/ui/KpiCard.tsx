import type { ReactNode } from 'react'

export function KpiCard({
  label,
  value,
  hint,
  icon,
  tone = 'info',
}: {
  label: string
  value: ReactNode
  hint?: string
  icon?: ReactNode
  tone?: 'info' | 'success' | 'warning' | 'danger' | 'neutral' | 'pending'
}) {
  return (
    <div className="kpi">
      {icon && (
        <div className={`kpi-icon kpi-icon--${tone}`}>{icon}</div>
      )}
      <div>
        <div className="kpi-label">{label}</div>
        <div className="kpi-value">{value}</div>
        {hint && <div className="kpi-hint">{hint}</div>}
      </div>
    </div>
  )
}