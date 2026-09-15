import type { ReactNode } from 'react'
import { toneForStatusString } from './statusTone'
import { formatEnumLabel } from '../../lib/formatLabel'

export type BadgeTone =
  | 'success'
  | 'warning'
  | 'danger'
  | 'info'
  | 'pending'
  | 'neutral'

export function Badge({
  tone,
  children,
  withDot = false,
}: {
  tone: BadgeTone
  children: ReactNode
  withDot?: boolean
}) {
  return (
    <span className={`badge badge--${tone}`}>
      {withDot && <span className="badge-dot" aria-hidden="true" />}
      {children}
    </span>
  )
}

export function StatusBadge({
  status,
  withDot = false,
}: {
  status: string
  withDot?: boolean
}) {
  return (
    <Badge tone={toneForStatusString(status)} withDot={withDot}>
      {formatEnumLabel(status)}
    </Badge>
  )
}