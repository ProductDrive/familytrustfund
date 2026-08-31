import { type CSSProperties, type ReactNode } from 'react'

export function Skeleton({
  className = '',
  style,
}: {
  className?: string
  style?: CSSProperties
}) {
  return <span aria-hidden="true" className={`skeleton ${className}`.trim()} style={style} />
}

export function SkeletonCard() {
  return <div className="card skeleton-card" aria-hidden="true" />
}

export function SkeletonText() {
  return <span aria-hidden="true" className="skeleton skeleton-text" />
}

export function LoadingState({
  label = 'Loading…',
  lines = 3,
}: {
  label?: string
  lines?: number
}) {
  return (
    <div aria-label={label}>
      {Array.from({ length: lines }).map((_, i) => (
        <Skeleton key={i} className="skeleton-text" />
      ))}
    </div>
  )
}

export function EmptyState({
  icon,
  title,
  description,
  action,
}: {
  icon?: ReactNode
  title: string
  description?: string
  action?: ReactNode
}) {
  return (
    <div className="empty-state">
      {icon && <div className="empty-state-icon">{icon}</div>}
      <h3>{title}</h3>
      {description && <p className="muted">{description}</p>}
      {action}
    </div>
  )
}