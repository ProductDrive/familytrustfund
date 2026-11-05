import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Check, X, BadgeCheck, AlertTriangle } from 'lucide-react'
import {
  useGuarantorPendingLoans,
  useApproveLoan,
  useRejectLoan,
  useGuarantorCancelLoan,
  useGuarantorLoanPreview,
  suggestedInstalments,
} from './loanApi'
import { PageHeader } from '../../components/ui/PageHeader'
import { Card } from '../../components/ui/Card'
import { Badge, StatusBadge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { AmountInput } from '../../components/ui/AmountInput'
import { EmptyState, SkeletonCard } from '../../components/ui/State'
import { MoneyDisplay } from '../../lib/money'
import { formatShortDate, formatShortDateTime } from '../../lib/formatDate'
import { useDebouncedValue } from '../../lib/useDebouncedValue'
import type {
  GuarantorLoanPreview,
  Loan,
  RepaymentFrequency,
} from './loanApi'

const FREQUENCIES = ['Weekly', 'Biweekly', 'Monthly'] as const

const FREQUENCY_LABELS: Record<RepaymentFrequency, string> = {
  Weekly: 'weekly',
  Biweekly: 'every two weeks',
  Monthly: 'monthly',
}

function ApprovalPreview({ preview }: { preview?: GuarantorLoanPreview }) {
  if (!preview) return null
  const plan = preview.preview

  return (
    <section className="repayment-preview" aria-live="polite">
      <h3>Repayment plan</h3>
      {plan.isValid ? (
        <>
          <p>
            <strong>
              {plan.instalments} {FREQUENCY_LABELS[plan.frequency]} payments
            </strong>{' '}
            of about <MoneyDisplay amount={plan.instalmentAmount} /> over{' '}
            {plan.termMonths} month{plan.termMonths === 1 ? '' : 's'}.
          </p>
          {plan.finalInstalmentAmount !== plan.instalmentAmount && (
            <p>
              Final payment: <MoneyDisplay amount={plan.finalInstalmentAmount} />.
            </p>
          )}
          {plan.fundType === 'External' ? (
            <p>
              Principal <MoneyDisplay amount={plan.amount} /> + interest{' '}
              <MoneyDisplay amount={plan.interestAmount} /> = total repayable{' '}
              <MoneyDisplay amount={plan.totalRepayable} />.
            </p>
          ) : (
            <p>
              Total repayable <MoneyDisplay amount={plan.totalRepayable} /> with no
              interest.
            </p>
          )}
          {plan.firstDueDateUtc && plan.finalDueDateUtc && (
            <p className="muted">
              First payment around {formatShortDate(plan.firstDueDateUtc)}, final
              around {formatShortDate(plan.finalDueDateUtc)}. Dates are confirmed at
              disbursement.
            </p>
          )}
        </>
      ) : (
        <ul className="preview-errors">
          {plan.errors.map((message) => (
            <li key={message}>
              <AlertTriangle size={14} aria-hidden /> {message}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function ApproveForm({ loan, onDone }: { loan: Loan; onDone: () => void }) {
  const approve = useApproveLoan()
  const reject = useRejectLoan()
  const navigate = useNavigate()

  const initialTerm = loan.requestedTermMonths ?? 6
  const initialFrequency = loan.requestedFrequency
  const initialInstalments =
    loan.requestedInstalments ??
    (loan.repaymentTerm > 0
      ? loan.repaymentTerm
      : suggestedInstalments(initialTerm, initialFrequency))

  const [amount, setAmount] = useState(String(loan.requestedAmount))
  const [frequency, setFrequency] = useState<RepaymentFrequency>(initialFrequency)
  const [termMonths, setTermMonths] = useState(String(initialTerm))
  const [instalments, setInstalments] = useState(String(initialInstalments))
  const [acknowledged, setAcknowledged] = useState(false)
  const [rejectReason, setRejectReason] = useState('')
  const [mode, setMode] = useState<'approve' | 'reject'>('approve')

  const debouncedAmount = useDebouncedValue(amount)
  const debouncedTerm = useDebouncedValue(termMonths)
  const debouncedInstalments = useDebouncedValue(instalments)

  const amountNumber = Number(debouncedAmount)
  const termNumber = Number(debouncedTerm)
  const instalmentsNumber = Number(debouncedInstalments)

  const previewInput =
    amountNumber > 0 && termNumber > 0 && instalmentsNumber > 0
      ? {
          loanId: loan.id,
          approvedAmount: amountNumber,
          approvedFrequency: frequency,
          approvedTermMonths: termNumber,
          approvedInstalments: instalmentsNumber,
        }
      : null

  const preview = useGuarantorLoanPreview(previewInput)
  const termsChanged = preview.data?.termsChanged ?? false
  const previewStale =
    amount !== debouncedAmount ||
    termMonths !== debouncedTerm ||
    instalments !== debouncedInstalments

  function changeFrequency(next: RepaymentFrequency) {
    setFrequency(next)
    setAcknowledged(false)
    const term = Number(termMonths)
    if (term > 0) setInstalments(String(suggestedInstalments(term, next)))
  }

  function changeTerm(next: string) {
    setTermMonths(next)
    setAcknowledged(false)
    const term = Number(next)
    if (term > 0) setInstalments(String(suggestedInstalments(term, frequency)))
  }

  function submit(e: React.FormEvent) {
    e.preventDefault()
    if (mode === 'approve') {
      const parsed = Number(amount)
      const term = Number(termMonths)
      const count = Number(instalments)
      if (!parsed || parsed <= 0 || term <= 0 || count <= 0) return
      if (previewStale || !preview.data?.preview.isValid) return
      if (preview.data.termsChanged && !acknowledged) return
      approve.mutate(
        {
          loanId: loan.id,
          approvedAmount: parsed,
          approvedFrequency: frequency,
          approvedTermMonths: term,
          approvedInstalments: count,
          confirmedWithMember: acknowledged,
        },
        {
          onSuccess: () => {
            onDone()
            // Send the Guarantor straight to the disbursement page for this
            // loan so the payout can be initiated immediately.
            navigate(`/guarantor/disburse?fund=${loan.fundId}&loan=${loan.id}`)
          },
        },
      )
    } else {
      reject.mutate({ loanId: loan.id, reason: rejectReason.trim() || null }, { onSuccess: onDone })
    }
  }

  const canApprove =
    !!preview.data?.preview.isValid &&
    !preview.isFetching &&
    !previewStale &&
    !approve.isPending &&
    (!termsChanged || acknowledged)

  return (
    <form onSubmit={submit}>
      {mode === 'approve' ? (
        <>
          <div className="field">
            <label htmlFor={`amount-${loan.id}`}>Approved amount (₦)</label>
            <AmountInput
              id={`amount-${loan.id}`}
              value={amount}
              onChange={(value) => {
                setAmount(value)
                setAcknowledged(false)
              }}
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
              onChange={(e) => changeFrequency(e.target.value as RepaymentFrequency)}
            >
              {FREQUENCIES.map((f) => (
                <option key={f} value={f}>
                  {f}
                </option>
              ))}
            </select>
            <p className="field-hint">
              Requested: {loan.requestedFrequency}. You may adjust the repayment
              frequency at approval.
            </p>
          </div>

          <div
            className="grid"
            style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: 12 }}
          >
            <div className="field">
              <label htmlFor={`term-${loan.id}`}>Term (months)</label>
              <input
                id={`term-${loan.id}`}
                type="number"
                min={preview.data?.preview.minTermMonths ?? 1}
                max={preview.data?.preview.maxTermMonths ?? 120}
                value={termMonths}
                onChange={(e) => changeTerm(e.target.value)}
                required
              />
              <p className="field-hint">
                Requested: {loan.requestedTermMonths ?? '—'} month(s).
              </p>
            </div>

            <div className="field">
              <label htmlFor={`instalments-${loan.id}`}>Number of instalments</label>
              <input
                id={`instalments-${loan.id}`}
                type="number"
                min={1}
                value={instalments}
                onChange={(e) => {
                  setInstalments(e.target.value)
                  setAcknowledged(false)
                }}
                required
              />
              <p className="field-hint">
                Requested: {loan.requestedInstalments ?? '—'}.
              </p>
            </div>
          </div>

          {previewInput && <ApprovalPreview preview={preview.data} />}

          {termsChanged && preview.data && (
            <section className="term-change-warning" role="alert">
              <h3>
                <AlertTriangle size={16} aria-hidden /> These terms differ from the
                member&apos;s request
              </h3>
              <p>
                Confirm the new arrangement with the borrower before approving.
                Approved terms become the loan&apos;s historical terms.
              </p>
              <ul>
                {preview.data.changes.map((change) => (
                  <li key={change.field}>
                    <strong>{change.field}:</strong> {change.requested} →{' '}
                    {change.approved}
                  </li>
                ))}
              </ul>
              <label className="checkbox-row">
                <input
                  type="checkbox"
                  checked={acknowledged}
                  onChange={(e) => setAcknowledged(e.target.checked)}
                />
                I have confirmed the new arrangement with the borrower.
              </label>
            </section>
          )}
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
          disabled={
            approve.isPending ||
            reject.isPending ||
            (mode === 'approve' && !canApprove)
          }
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
                    <span className="property-label">Term</span>
                    <span className="property-value">
                      {l.requestedTermMonths != null
                        ? `${l.requestedTermMonths} month(s) · `
                        : ''}
                      {l.requestedInstalments != null
                        ? `${l.requestedInstalments} instalments`
                        : 'Not specified'}
                    </span>
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
