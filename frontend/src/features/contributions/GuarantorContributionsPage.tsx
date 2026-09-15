import { useState } from 'react'
import { Check, BadgeCheck } from 'lucide-react'
import {
  useGuarantorPendingContributions,
  useConfirmContribution,
  useRejectContribution,
} from './contributionApi'
import { PageHeader } from '../../components/ui/PageHeader'
import { Card } from '../../components/ui/Card'
import { StatusBadge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { EmptyState, SkeletonCard } from '../../components/ui/State'
import { MoneyDisplay } from '../../lib/money'
import { formatShortDateTime } from '../../lib/formatDate'
import { GuarantorEvidenceView } from '../evidence/GuarantorEvidenceView'
import type { Contribution } from './contributionApi'

function ContributeDecision({
  contribution,
  onDone,
}: {
  contribution: Contribution
  onDone: () => void
}) {
  const confirm = useConfirmContribution()
  const reject = useRejectContribution()
  const [reason, setReason] = useState('')
  const [note, setNote] = useState('')
  const [mode, setMode] = useState<'confirm' | 'reject'>('confirm')

  function submit(e: React.FormEvent) {
    e.preventDefault()
    if (mode === 'confirm') {
      confirm.mutate(
        { contributionId: contribution.id, note: note.trim() || null },
        { onSuccess: onDone },
      )
    } else {
      reject.mutate(
        { contributionId: contribution.id, reason: reason.trim() || null },
        { onSuccess: onDone },
      )
    }
  }

  return (
    <form onSubmit={submit}>
      {mode === 'confirm' ? (
        <div className="field">
          <label htmlFor={`note-${contribution.id}`}>Confirmation note (optional)</label>
          <textarea
            id={`note-${contribution.id}`}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            rows={2}
            placeholder="Add a note to this confirmation, e.g. payment verified with the member."
          />
        </div>
      ) : (
        <div className="field">
          <label htmlFor={`reason-${contribution.id}`}>Rejection reason (optional)</label>
          <textarea
            id={`reason-${contribution.id}`}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            rows={2}
            placeholder="Why is this contribution being rejected?"
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
              : 'Confirm contribution'
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

export function GuarantorContributionsPage() {
  const { data: contributions, isLoading, error } = useGuarantorPendingContributions()
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
        title="Contribution Confirmations"
        subtitle="Verify and confirm contributions reported by your members."
      />

      {isLoading && <SkeletonCard />}
      {error && <p className="error-text">{(error as Error).message}</p>}

      {contributions && contributions.length > 0 ? (
        <div className="grid">
          {contributions.map((c) => {
            const open = expanded.has(c.id)
            return (
              <Card key={c.id}>
                <div className="card-title">
                  <h3>{c.memberDisplayName || 'Member'}</h3>
                  <StatusBadge status={c.status} withDot />
                </div>

                <p className="muted">{c.memberEmail}</p>

                <ul className="property-list" style={{ marginTop: 8 }}>
                  <li>
                    <span className="property-label">Fund</span>
                    <span className="property-value">{c.fundName}</span>
                  </li>
                  <li>
                    <span className="property-label">Amount</span>
                    <MoneyDisplay amount={c.amount} />
                  </li>
                  {c.reference && (
                    <li>
                      <span className="property-label">Reference</span>
                      <span className="property-value">{c.reference}</span>
                    </li>
                  )}
                  {c.note && (
                    <li>
                      <span className="property-label">Note</span>
                      <span className="property-value" style={{ fontWeight: 400 }}>
                        {c.note}
                      </span>
                    </li>
                  )}
                  <li>
                    <span className="property-label">Reported</span>
                    <span className="property-value">
                      {formatShortDateTime(c.reportedAtUtc)}
                    </span>
                  </li>
                </ul>

                <div style={{ marginTop: 16 }}>
                  <div style={{ marginBottom: 8 }}>
                    <GuarantorEvidenceView
                      kind="contribution"
                      resourceId={c.id}
                      hasEvidence={c.hasEvidence}
                    />
                  </div>
                  {!open ? (
                    <Button onClick={() => toggle(c.id)}>
                      <BadgeCheck size={16} /> Review
                    </Button>
                  ) : (
                    <ContributeDecision
                      contribution={c}
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
              title="No pending contributions"
              description="When a member reports a contribution, it will appear here for you to confirm or reject."
            />
          </Card>
        )
      )}
    </div>
  )
}
