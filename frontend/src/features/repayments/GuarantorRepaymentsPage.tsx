import { useState } from 'react'
import { Check, BadgeCheck } from 'lucide-react'
import {
  useGuarantorPendingRepayments,
  useConfirmPendingRepayment,
  useRejectPendingRepayment,
} from './repaymentApi'
import type { PendingRepayment } from './repaymentApi'
import { PageHeader } from '../../components/ui/PageHeader'
import { Card } from '../../components/ui/Card'
import { StatusBadge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { EmptyState, SkeletonCard } from '../../components/ui/State'
import { MoneyDisplay } from '../../lib/money'
import { formatShortDateTime } from '../../lib/formatDate'
import { GuarantorEvidenceView } from '../evidence/GuarantorEvidenceView'

function RepaymentDecision({
  pending,
  onDone,
}: {
  pending: PendingRepayment
  onDone: () => void
}) {
  const confirm = useConfirmPendingRepayment()
  const reject = useRejectPendingRepayment()
  const [reason, setReason] = useState('')
  const [note, setNote] = useState('')
  const [mode, setMode] = useState<'confirm' | 'reject'>('confirm')

  function submit(e: React.FormEvent) {
    e.preventDefault()
    if (mode === 'confirm') {
      confirm.mutate(
        { pendingRepaymentId: pending.id, note: note.trim() || null },
        { onSuccess: onDone },
      )
    } else {
      reject.mutate(
        { pendingRepaymentId: pending.id, reason: reason.trim() || null },
        { onSuccess: onDone },
      )
    }
  }

  return (
    <form onSubmit={submit}>
      {mode === 'confirm' ? (
        <div className="field">
          <label htmlFor={`repay-note-${pending.id}`}>Confirmation note (optional)</label>
          <textarea
            id={`repay-note-${pending.id}`}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            rows={2}
            placeholder="Add a note to this confirmation, e.g. payment verified with the member."
          />
        </div>
      ) : (
        <div className="field">
          <label htmlFor={`repay-reason-${pending.id}`}>Rejection reason (optional)</label>
          <textarea
            id={`repay-reason-${pending.id}`}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            rows={2}
            placeholder="Why is this repayment being rejected?"
          />
        </div>
      )}

      {(confirm.isError || reject.isError) && (
        <p className="form-error">
          {((confirm.error ?? reject.error) as Error).message}
        </p>
      )}

      <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
        <Button type="submit" variant={mode === 'confirm' ? 'primary' : 'danger'}>
          {mode === 'confirm'
            ? confirm.isPending
              ? 'Confirming…'
              : 'Confirm repayment'
            : reject.isPending
              ? 'Rejecting…'
              : 'Confirm rejection'}
        </Button>
        <Button
          type="button"
          variant="ghost"
          onClick={() => setMode(mode === 'confirm' ? 'reject' : 'confirm')}
          disabled={confirm.isPending || reject.isPending}
        >
          {mode === 'confirm' ? 'Reject instead' : 'Confirm instead'}
        </Button>
      </div>
    </form>
  )
}

export function GuarantorRepaymentsPage() {
  const { data: pending, isLoading, error } = useGuarantorPendingRepayments()
  const [expanded, setExpanded] = useState<Set<string>>(new Set())

  function toggle(id: string) {
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  return (
    <div>
      <PageHeader
        title="Repayment Confirmations"
        subtitle="Verify and confirm repayments reported by your members. Only confirmation posts to the ledger."
      />

      {isLoading && <SkeletonCard />}
      {error && <p className="error-text">{(error as Error).message}</p>}

      {pending && pending.length > 0 ? (
        <div className="grid">
          {pending.map((p) => {
            const open = expanded.has(p.id)
            return (
              <Card key={p.id}>
                <div className="card-title">
                  <h3>{p.memberDisplayName || 'Member'}</h3>
                  <StatusBadge status={p.status} withDot />
                </div>

                <p className="muted">{p.memberEmail}</p>

                <ul className="property-list" style={{ marginTop: 8 }}>
                  <li>
                    <span className="property-label">Fund</span>
                    <span className="property-value">{p.fundName}</span>
                  </li>
                  <li>
                    <span className="property-label">Amount</span>
                    <MoneyDisplay amount={p.amount} />
                  </li>
                  <li>
                    <span className="property-label">Type</span>
                    <span className="property-value">{p.kind}</span>
                  </li>
                  {p.reference && (
                    <li>
                      <span className="property-label">Reference</span>
                      <span className="property-value">{p.reference}</span>
                    </li>
                  )}
                  {p.note && (
                    <li>
                      <span className="property-label">Note</span>
                      <span className="property-value" style={{ fontWeight: 400 }}>
                        {p.note}
                      </span>
                    </li>
                  )}
                  <li>
                    <span className="property-label">Reported</span>
                    <span className="property-value">
                      {formatShortDateTime(p.reportedAtUtc)}
                    </span>
                  </li>
                </ul>

                <div style={{ marginTop: 16 }}>
                  <div style={{ marginBottom: 8 }}>
                    <GuarantorEvidenceView
                      kind="repayment"
                      resourceId={p.id}
                      hasEvidence={p.hasEvidence}
                    />
                  </div>
                  {!open ? (
                    <Button onClick={() => toggle(p.id)}>
                      <BadgeCheck size={16} /> Review
                    </Button>
                  ) : (
                    <RepaymentDecision
                      pending={p}
                      onDone={() => setExpanded(new Set())}
                    />
                  )}
                </div>
              </Card>
            )
          })}
        </div>
      ) : (
        !isLoading &&
        !error && (
          <Card>
            <EmptyState
              icon={<Check size={24} />}
              title="No pending repayments"
              description="When a member reports a payment, it will appear here for you to confirm or reject."
            />
          </Card>
        )
      )}
    </div>
  )
}