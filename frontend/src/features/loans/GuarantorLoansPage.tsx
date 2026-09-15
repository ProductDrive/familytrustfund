import { useState } from 'react'
import { Check, X, BadgeCheck } from 'lucide-react'
import {
  useGuarantorPendingLoans,
  useApproveLoan,
  useRejectLoan,
  useGuarantorCancelLoan,
} from './loanApi'
import { PageHeader } from '../../components/ui/PageHeader'
import { Card } from '../../components/ui/Card'
import { Badge, StatusBadge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { AmountInput } from '../../components/ui/AmountInput'
import { EmptyState, SkeletonCard } from '../../components/ui/State'
import { MoneyDisplay } from '../../lib/money'
import { formatShortDateTime } from '../../lib/formatDate'
import type { Loan, RepaymentFrequency } from './loanApi'

const FREQUENCIES = ['Weekly', 'Biweekly', 'Monthly'] as const

function ApproveForm({ loan, onDone }: { loan: Loan; onDone: () => void }) {
  const approve = useApproveLoan()
  const reject = useRejectLoan()
  const [amount, setAmount] = useState(String(loan.requestedAmount))
  const [frequency, setFrequency] = useState<RepaymentFrequency>(
    loan.requestedFrequency,
  )
  const [rejectReason, setRejectReason] = useState('')
  const [mode, setMode] = useState<'approve' | 'reject'>('approve')

  function submit(e: React.FormEvent) {
    e.preventDefault()
    if (mode === 'approve') {
      const parsed = Number(amount)
      if (!parsed || parsed <= 0) return
      approve.mutate(
        { loanId: loan.id, approvedAmount: parsed, approvedFrequency: frequency },
        { onSuccess: onDone },
      )
    } else {
      reject.mutate({ loanId: loan.id, reason: rejectReason.trim() || null }, { onSuccess: onDone })
    }
  }

  return (
    <form onSubmit={submit}>
      {mode === 'approve' ? (
        <>
          <div className="field">
            <label htmlFor={`amount-${loan.id}`}>Approved amount (₦)</label>
            <AmountInput
              id={`amount-${loan.id}`}
              value={amount}
              onChange={setAmount}
              required
            />
            <p className="field-hint">
              Requested: <MoneyDisplay amount={loan.requestedAmount} />. You may approve a
              different amount.
            </p>
          </div>

          <div className="field">
            <label htmlFor={`freq-${loan.id}`}>Repayment frequency</label>
            <select
              id={`freq-${loan.id}`}
              value={frequency}
              onChange={(e) => setFrequency(e.target.value as RepaymentFrequency)}
            >
              {FREQUENCIES.map((f) => (
                <option key={f} value={f}>
                  {f}
                </option>
              ))}
            </select>
            <p className="field-hint">
              You may adjust the repayment frequency at approval. Approved terms become the
              loan&apos;s historical terms.
            </p>
          </div>
        </>
      ) : (
        <div className="field">
          <label htmlFor={`reason-${loan.id}`}>Rejection reason (optional)</label>
          <textarea
            id={`reason-${loan.id}`}
            value={rejectReason}
            onChange={(e) => setRejectReason(e.target.value)}
            rows={2}
            placeholder="Why is this request being rejected?"
          />
        </div>
      )}

      {(approve.isError || reject.isError) && (
        <p className="form-error">
          {((approve.error ?? reject.error) as Error).message}
        </p>
      )}

      <div style={{ display: 'flex', gap: 8, alignItems: 'center', marginTop: 12 }}>
        <Button
          type="submit"
          variant={mode === 'approve' ? 'primary' : 'danger'}
          disabled={approve.isPending || reject.isPending}
        >
          {mode === 'approve'
            ? approve.isPending
              ? 'Approving…'
              : 'Confirm approval'
            : reject.isPending
              ? 'Rejecting…'
              : 'Confirm rejection'}
        </Button>
        <Button
          type="button"
          variant="ghost"
          onClick={() => setMode(mode === 'approve' ? 'reject' : 'approve')}
          disabled={approve.isPending || reject.isPending}
        >
          {mode === 'approve' ? 'Reject instead' : 'Approve instead'}
        </Button>
      </div>
    </form>
  )
}

export function GuarantorLoansPage() {
  const { data: loans, isLoading, error } = useGuarantorPendingLoans()
  const cancel = useGuarantorCancelLoan()
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
        title="Loan Requests"
        subtitle="Review and decide on pending loan requests from your members."
      />

      {isLoading && <SkeletonCard />}
      {error && <p className="error-text">{(error as Error).message}</p>}

      {loans && loans.length > 0 ? (
        <div className="grid">
          {loans.map((l) => {
            const open = expanded.has(l.id)
            return (
              <Card key={l.id}>
                <div className="card-title">
                  <h3>{l.memberDisplayName}</h3>
                  <Badge tone={l.fundingSource === 'FamilyCapital' ? 'info' : 'neutral'}>
                    {l.fundingSource === 'FamilyCapital' ? 'Family Capital' : 'Guarantor Capital'}
                  </Badge>
                </div>

                <p className="muted">{l.memberEmail}</p>

                <ul className="property-list" style={{ marginTop: 8 }}>
                  <li>
                    <span className="property-label">Fund</span>
                    <span className="property-value">{l.fundName}</span>
                  </li>
                  <li>
                    <span className="property-label">Requested amount</span>
                    <MoneyDisplay amount={l.requestedAmount} />
                  </li>
                  <li>
                    <span className="property-label">Frequency</span>
                    <span className="property-value">{l.requestedFrequency}</span>
                  </li>
                  <li>
                    <span className="property-label">Status</span>
                    <StatusBadge status={l.status} withDot />
                  </li>
                  {l.purpose && (
                    <li>
                      <span className="property-label">Purpose</span>
                      <span className="property-value" style={{ fontWeight: 400 }}>
                        {l.purpose}
                      </span>
                    </li>
                  )}
                  <li>
                    <span className="property-label">Requested</span>
                    <span className="property-value">
                      {formatShortDateTime(l.requestedAtUtc)}
                    </span>
                  </li>
                </ul>

                <div style={{ marginTop: 16 }}>
                  {!open ? (
                    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                      <Button onClick={() => toggle(l.id)}>
                        <BadgeCheck size={16} /> Review
                      </Button>
                      <Button
                        variant="danger"
                        onClick={() => toggle(l.id)}
                      >
                        <X size={16} /> Reject
                      </Button>
                      <Button
                        variant="ghost"
                        disabled={cancel.isPending}
                        onClick={() => {
                          if (
                            window.confirm(
                              `Cancel ${l.memberDisplayName}'s loan request for ${l.fundName}?`,
                            )
                          ) {
                            cancel.mutate({ loanId: l.id })
                          }
                        }}
                      >
                        <X size={16} /> {cancel.isPending ? 'Cancelling…' : 'Cancel'}
                      </Button>
                    </div>
                  ) : (
                    <ApproveForm loan={l} onDone={() => setExpanded(new Set())} />
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
              title="No pending loan requests"
              description="When a member requests a loan, it will appear here for you to approve or reject."
            />
          </Card>
        )
      )}
    </div>
  )
}
