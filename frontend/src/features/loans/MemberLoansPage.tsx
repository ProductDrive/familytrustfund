import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { HandCoins, CircleDollarSign, X, AlertTriangle, Check, Paperclip } from 'lucide-react'
import { useMyMemberships } from '../membership/membershipApi'
import {
  useMyLoans,
  useRequestLoan,
  useCancelLoan,
  useLoanPreview,
  suggestedInstalments,
} from './loanApi'
import type { Loan, LoanPreview, RepaymentFrequency } from './loanApi'
import {
  MANUAL_DISBURSEMENT_METHOD_LABELS,
  useConfirmOfflineDisbursement,
  useDisputeOfflineDisbursement,
  useMyLoanDisbursement,
  useMyOfflineDisbursements,
  type ManualDisbursement,
} from '../payments/paymentApi'
import {
  memberEvidenceUrl,
  useMemberDisbursementEvidence,
} from '../evidence/evidenceApi'
import { MemberBankDetails } from '../payments/MemberBankDetails'
import { PageHeader } from '../../components/ui/PageHeader'
import { Card } from '../../components/ui/Card'
import { StatusBadge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { AmountInput } from '../../components/ui/AmountInput'
import { EmptyState, SkeletonCard } from '../../components/ui/State'
import { MoneyDisplay } from '../../lib/money'
import { formatShortDate } from '../../lib/formatDate'
import { useDebouncedValue } from '../../lib/useDebouncedValue'

const FREQUENCIES = ['Weekly', 'Biweekly', 'Monthly'] as const

const FREQUENCY_LABELS: Record<RepaymentFrequency, string> = {
  Weekly: 'weekly',
  Biweekly: 'every two weeks',
  Monthly: 'monthly',
}

function DisbursementCell({
  loanId,
  offline,
}: {
  loanId: string
  offline?: ManualDisbursement
}) {
  const { data: disbursement, isLoading } = useMyLoanDisbursement(loanId)
  // An offline report supersedes the provider view for this loan (ADR-056).
  if (offline) {
    return (
      <StatusBadge
        status={offline.status === 'Confirmed' ? 'Disbursed' : offline.status}
        withDot
      />
    )
  }
  if (isLoading) return <span className="cell-secondary">…</span>
  if (!disbursement) return <span className="cell-secondary">—</span>
  return <StatusBadge status={disbursement.status} withDot />
}

function CancelLoanButton({ loan }: { loan: Loan }) {
  const cancel = useCancelLoan()
  if (loan.status !== 'Pending') return null

  return (
    <Button
      variant="ghost"
      disabled={cancel.isPending}
      onClick={() => {
        if (
          window.confirm(
            'Cancel this loan request? It will be withdrawn before the Guarantor reviews it.',
          )
        ) {
          cancel.mutate({ loanId: loan.id })
        }
      }}
    >
      <X size={14} /> {cancel.isPending ? 'Cancelling…' : 'Cancel request'}
    </Button>
  )
}

function RepaymentSummary({
  preview,
  isLoading,
}: {
  preview?: LoanPreview
  isLoading: boolean
}) {
  if (isLoading && !preview) {
    return <p className="muted">Calculating your repayment plan…</p>
  }
  if (!preview) return null

  return (
    <section className="repayment-preview" aria-live="polite">
      <h3>Your repayment plan</h3>
      {preview.isValid ? (
        <>
          <p>
            You will make{' '}
            <strong>
              {preview.instalments} {FREQUENCY_LABELS[preview.frequency]} payments
            </strong>{' '}
            of about <MoneyDisplay amount={preview.instalmentAmount} /> over{' '}
            {preview.termMonths} month{preview.termMonths === 1 ? '' : 's'}.
          </p>
          {preview.finalInstalmentAmount !== preview.instalmentAmount && (
            <p>
              Your final payment will be{' '}
              <MoneyDisplay amount={preview.finalInstalmentAmount} />.
            </p>
          )}
          {preview.fundType === 'External' ? (
            <p>
              Principal <MoneyDisplay amount={preview.amount} /> + interest{' '}
              <MoneyDisplay amount={preview.interestAmount} /> = total repayable{' '}
              <MoneyDisplay amount={preview.totalRepayable} />.
            </p>
          ) : (
            <p>
              Total repayable <MoneyDisplay amount={preview.totalRepayable} /> with
              no interest.
            </p>
          )}
          {preview.firstDueDateUtc && preview.finalDueDateUtc && (
            <p className="muted">
              First payment around {formatShortDate(preview.firstDueDateUtc)}, final
              around {formatShortDate(preview.finalDueDateUtc)}. Dates are confirmed
              when the loan is disbursed.
            </p>
          )}
          {preview.familyBorrowingEntitlement != null && (
            <p className="muted">
              Borrowing entitlement:{' '}
              <MoneyDisplay amount={preview.familyBorrowingEntitlement} /> · Available
              lending capacity:{' '}
              <MoneyDisplay amount={preview.availableLendingCapacity} />
            </p>
          )}
        </>
      ) : (
        <ul className="preview-errors">
          {preview.errors.map((message) => (
            <li key={message}>
              <AlertTriangle size={14} aria-hidden /> {message}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function LoanRequestForm({ onDone }: { onDone: () => void }) {
  const requestLoan = useRequestLoan()
  const { data: memberships } = useMyMemberships()
  const activeMemberships = memberships?.filter((m) => m.status === 'Active') ?? []

  const [fundId, setFundId] = useState('')
  const [amount, setAmount] = useState('')
  const [frequency, setFrequency] = useState<RepaymentFrequency>('Monthly')
  const [termMonths, setTermMonths] = useState('6')
  const [instalments, setInstalments] = useState('6')
  const [purpose, setPurpose] = useState('')

  const selectedMembership = activeMemberships.find((m) => m.fundId === fundId)

  const debouncedAmount = useDebouncedValue(amount)
  const debouncedTerm = useDebouncedValue(termMonths)
  const debouncedInstalments = useDebouncedValue(instalments)

  const amountNumber = Number(debouncedAmount)
  const termNumber = Number(debouncedTerm)
  const instalmentsNumber = Number(debouncedInstalments)

  const previewInput =
    fundId && amountNumber > 0 && termNumber > 0 && instalmentsNumber > 0
      ? {
          fundId,
          amount: amountNumber,
          frequency,
          termMonths: termNumber,
          instalments: instalmentsNumber,
        }
      : null

  const preview = useLoanPreview(previewInput)
  const previewStale =
    amount !== debouncedAmount ||
    termMonths !== debouncedTerm ||
    instalments !== debouncedInstalments

  function changeFrequency(next: RepaymentFrequency) {
    setFrequency(next)
    const term = Number(termMonths)
    if (term > 0) setInstalments(String(suggestedInstalments(term, next)))
  }

  function changeTerm(next: string) {
    setTermMonths(next)
    const term = Number(next)
    if (term > 0) setInstalments(String(suggestedInstalments(term, frequency)))
  }

  function submit(e: React.FormEvent) {
    e.preventDefault()
    const parsed = Number(amount)
    const term = Number(termMonths)
    const count = Number(instalments)
    if (!fundId || !parsed || parsed <= 0 || term <= 0 || count <= 0) return
    if (previewStale || !preview.data?.isValid) return
    requestLoan.mutate(
      {
        fundId,
        amount: parsed,
        frequency,
        termMonths: term,
        instalments: count,
        purpose: purpose.trim() || null,
      },
      { onSuccess: onDone },
    )
  }

  const canSubmit =
    activeMemberships.length > 0 &&
    !!preview.data?.isValid &&
    !preview.isFetching &&
    !previewStale &&
    !requestLoan.isPending

  return (
    <Card title="Request a loan">
      <p className="muted">
        Choose how much you need, how often you can repay, and how long you want to
        take. The repayment plan updates as you adjust the form.
      </p>
      <form onSubmit={submit}>
        <div className="field">
          <label htmlFor="loan-fund">Fund</label>
          <select
            id="loan-fund"
            value={fundId}
            onChange={(e) => setFundId(e.target.value)}
            required
          >
            <option value="">Select a fund…</option>
            {activeMemberships.map((m) => (
              <option key={m.fundId} value={m.fundId}>
                {m.fundName} ({m.fundType})
              </option>
            ))}
          </select>
          {activeMemberships.length === 0 && (
            <p className="field-hint">
              You need an active membership in a fund to request a loan.
            </p>
          )}
        </div>

        <div className="field">
          <label htmlFor="loan-amount">Amount (₦)</label>
          <AmountInput
            id="loan-amount"
            value={amount}
            onChange={setAmount}
            placeholder="e.g. 50000"
            required
          />
          <p className="field-hint">
            The Guarantor may approve a different amount at approval.
          </p>
        </div>

        <div className="field">
          <label htmlFor="loan-frequency">Repayment frequency</label>
          <select
            id="loan-frequency"
            value={frequency}
            onChange={(e) => changeFrequency(e.target.value as RepaymentFrequency)}
          >
            {FREQUENCIES.map((f) => (
              <option key={f} value={f}>
                {f}
              </option>
            ))}
          </select>
        </div>

        <div
          className="grid"
          style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: 12 }}
        >
          <div className="field">
            <label htmlFor="loan-term">Repayment Term (months)</label>
            <input
              id="loan-term"
              type="number"
              min={selectedMembership?.minLoanTermMonths ?? 1}
              max={selectedMembership?.maxLoanTermMonths ?? 120}
              value={termMonths}
              onChange={(e) => changeTerm(e.target.value)}
              required
            />
            {selectedMembership && (
              <p className="field-hint">
                This fund allows {selectedMembership.minLoanTermMonths}–
                {selectedMembership.maxLoanTermMonths} months.
              </p>
            )}
          </div>

          <div className="field">
            <label htmlFor="loan-instalments">Number of instalments</label>
            <input
              id="loan-instalments"
              type="number"
              min={1}
              value={instalments}
              onChange={(e) => setInstalments(e.target.value)}
              required
            />
            <p className="field-hint">
              Weekly and two-weekly terms allow one payment either side of the
              calendar estimate.
            </p>
          </div>
        </div>

        <div className="field">
          <label htmlFor="loan-purpose">Purpose (optional)</label>
          <textarea
            id="loan-purpose"
            value={purpose}
            onChange={(e) => setPurpose(e.target.value)}
            rows={2}
            placeholder="What is the loan for?"
          />
        </div>

        {previewInput && (
          <RepaymentSummary
            preview={preview.data}
            isLoading={preview.isFetching}
          />
        )}

        {requestLoan.isError && (
          <p className="form-error">{(requestLoan.error as Error).message}</p>
        )}

        <Button type="submit" disabled={!canSubmit}>
          {requestLoan.isPending ? 'Submitting…' : 'Submit loan request'}
        </Button>
      </form>
    </Card>
  )
}

function OfflineDisbursementEvidence({ reportId }: { reportId: string }) {
  const { data: evidence, isLoading } = useMemberDisbursementEvidence(reportId)
  if (isLoading) return <span className="cell-secondary">…</span>
  if (!evidence || evidence.length === 0) return null
  return (
    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
      {evidence.map((e) => (
        <a
          key={e.id}
          className="btn btn--secondary btn--sm"
          href={memberEvidenceUrl(e.id)}
          target="_blank"
          rel="noreferrer"
        >
          <Paperclip size={14} /> {e.originalFileName}
        </a>
      ))}
    </div>
  )
}

/**
 * The member's view of an offline payment the Fund Originator reported. They
 * confirm receipt (which disburses the loan) or dispute it. Confirmation is the
 * only event that posts to the ledger (ADR-056).
 */
function OfflineDisbursementPrompt({ report }: { report: ManualDisbursement }) {
  const confirm = useConfirmOfflineDisbursement()
  const dispute = useDisputeOfflineDisbursement()
  const [showDispute, setShowDispute] = useState(false)
  const [reason, setReason] = useState('')

  const pending = report.status === 'PendingMemberConfirmation'
  const disputed = report.status === 'Disputed'

  return (
    <Card>
      <div className="card-title">
        <h2>{pending ? 'Payment awaiting your confirmation' : 'Payment you disputed'}</h2>
        <StatusBadge status={report.status} withDot />
      </div>

      <p className="muted">
        {pending
          ? 'The Fund Originator reports paying you outside the platform. Confirm only if the money has reached you — confirming marks your loan as disbursed.'
          : 'You told us you did not receive this payment. The Fund Originator must resolve it before your loan is disbursed.'}
      </p>

      <dl
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))',
          gap: 12,
          margin: '16px 0',
        }}
      >
        <div>
          <dt className="muted" style={{ fontSize: 12 }}>
            Amount
          </dt>
          <dd style={{ margin: 0, fontWeight: 600 }}>
            <MoneyDisplay amount={report.amount} />
          </dd>
        </div>
        <div>
          <dt className="muted" style={{ fontSize: 12 }}>
            Method
          </dt>
          <dd style={{ margin: 0 }}>{MANUAL_DISBURSEMENT_METHOD_LABELS[report.method]}</dd>
        </div>
        {report.destinationBankName && (
          <div>
            <dt className="muted" style={{ fontSize: 12 }}>
              Paid to
            </dt>
            <dd style={{ margin: 0 }}>
              {report.destinationBankName}
              {report.destinationAccountLast4
                ? ` ••••${report.destinationAccountLast4}`
                : ''}
            </dd>
          </div>
        )}
        {report.reference && (
          <div>
            <dt className="muted" style={{ fontSize: 12 }}>
              Reference
            </dt>
            <dd style={{ margin: 0 }}>{report.reference}</dd>
          </div>
        )}
        <div>
          <dt className="muted" style={{ fontSize: 12 }}>
            Reported
          </dt>
          <dd style={{ margin: 0 }}>{formatShortDate(report.reportedAtUtc)}</dd>
        </div>
      </dl>

      {report.note && (
        <p className="muted">From the Fund Originator: “{report.note}”</p>
      )}

      <div style={{ marginBottom: 16 }}>
        <OfflineDisbursementEvidence reportId={report.id} />
      </div>

      {disputed && report.memberNote && (
        <p className="muted">Your reason: “{report.memberNote}”</p>
      )}

      {pending && !showDispute && (
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          <Button
            disabled={confirm.isPending}
            onClick={() => confirm.mutate({ reportId: report.id })}
          >
            <Check size={16} /> {confirm.isPending ? 'Confirming…' : 'I received this payment'}
          </Button>
          <Button variant="ghost" onClick={() => setShowDispute(true)}>
            <AlertTriangle size={16} /> I did not receive this
          </Button>
        </div>
      )}

      {pending && showDispute && (
        <form
          onSubmit={(e) => {
            e.preventDefault()
            if (reason.trim()) {
              dispute.mutate({ reportId: report.id, reason: reason.trim() })
            }
          }}
        >
          <div className="field">
            <label htmlFor={`dispute-${report.id}`}>
              Why are you disputing this payment?
            </label>
            <textarea
              id={`dispute-${report.id}`}
              rows={2}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              required
            />
          </div>
          {dispute.isError && (
            <p className="form-error">{(dispute.error as Error).message}</p>
          )}
          <div style={{ display: 'flex', gap: 8 }}>
            <Button
              type="submit"
              variant="danger"
              disabled={!reason.trim() || dispute.isPending}
            >
              {dispute.isPending ? 'Submitting…' : 'Submit dispute'}
            </Button>
            <Button type="button" variant="secondary" onClick={() => setShowDispute(false)}>
              Cancel
            </Button>
          </div>
        </form>
      )}

      {confirm.isError && <p className="form-error">{(confirm.error as Error).message}</p>}
    </Card>
  )
}

export function MemberLoansPage() {
  const navigate = useNavigate()
  const { data: loans, isLoading, error } = useMyLoans()
  const { data: offlineReports } = useMyOfflineDisbursements()
  const [showForm, setShowForm] = useState(false)

  const pendingOffline =
    offlineReports?.filter(
      (r) => r.status === 'PendingMemberConfirmation' || r.status === 'Disputed',
    ) ?? []

  return (
    <div>
      <PageHeader
        title="Loans"
        subtitle="Request a loan and track the status of your loan requests."
        actions={
          !showForm ? (
            <Button onClick={() => setShowForm(true)}>
              <HandCoins size={16} /> Request a loan
            </Button>
          ) : undefined
        }
      />

      {isLoading && <SkeletonCard />}
      {error && <p className="error-text">{(error as Error).message}</p>}

      {pendingOffline.length > 0 && (
        <div style={{ marginBottom: 20, display: 'grid', gap: 16 }}>
          {pendingOffline.map((report) => (
            <OfflineDisbursementPrompt key={report.id} report={report} />
          ))}
        </div>
      )}

      <div style={{ marginBottom: 20 }}>
        <MemberBankDetails />
      </div>

      {showForm && (
        <div style={{ marginBottom: 16 }}>
          <LoanRequestForm onDone={() => setShowForm(false)} />
        </div>
      )}

      {loans && loans.length > 0 ? (
        <Card className="table-card">
          <div className="card-title">
            <h2>Your loan requests</h2>
          </div>
          <table className="data-table">
            <thead>
              <tr>
                <th>Fund</th>
                <th>Amount</th>
                <th>Frequency</th>
                <th>Status</th>
                <th>Disbursement</th>
                <th>Requested</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {loans.map((l) => (
                <tr key={l.id}>
                  <td>
                    <strong>{l.fundName}</strong>
                    <div className="cell-secondary">{l.fundType}</div>
                  </td>
                  <td>
                    <MoneyDisplay amount={l.approvedAmount ?? l.requestedAmount} />
                    {l.approvedAmount && l.approvedAmount !== l.requestedAmount && (
                      <div className="cell-secondary" style={{ fontSize: 11 }}>
                        Requested: <MoneyDisplay amount={l.requestedAmount} />
                      </div>
                    )}
                  </td>
                  <td>
                    {l.approvedFrequency ?? l.requestedFrequency}
                    <div className="cell-secondary" style={{ fontSize: 11 }}>
                      {(l.approvedTermMonths ?? l.requestedTermMonths) != null
                        ? `${l.approvedTermMonths ?? l.requestedTermMonths} months · `
                        : ''}
                      {l.approvedTermMonths != null
                        ? `${l.repaymentTerm} instalments`
                        : l.requestedInstalments != null
                          ? `${l.requestedInstalments} instalments`
                          : ''}
                    </div>
                  </td>
                  <td>
                    <StatusBadge status={l.status} withDot />
                  </td>
                  <td>
                    <DisbursementCell
                      loanId={l.id}
                      offline={offlineReports?.find((r) => r.loanId === l.id)}
                    />
                  </td>
                  <td className="cell-secondary">
                    {formatShortDate(l.requestedAtUtc)}
                  </td>
                  <td>
                    <CancelLoanButton loan={l} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      ) : (
        !isLoading &&
        !error && (
          <Card>
            <EmptyState
              icon={<CircleDollarSign size={24} />}
              title="No loans yet"
              description="When you request a loan it will appear here for the Guarantor to review."
              action={
                <Button onClick={() => navigate('/loans')}>
                  <HandCoins size={16} /> Request a loan
                </Button>
              }
            />
          </Card>
        )
      )}
    </div>
  )
}
