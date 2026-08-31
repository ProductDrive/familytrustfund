import type { ReactNode } from 'react'

export function Card({
  title,
  actions,
  children,
  className,
}: {
  title?: string
  actions?: ReactNode
  children: ReactNode
  className?: string
}) {
  return (
    <section className={className ? `card ${className}` : 'card'}>
      {title && (
        <div className="card-title">
          <h2>{title}</h2>
          {actions && <div className="card-actions">{actions}</div>}
        </div>
      )}
      {children}
    </section>
  )
}