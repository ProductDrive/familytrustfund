import { useEffect, useState } from 'react'
import type { CSSProperties } from 'react'
import { useSearchParams } from 'react-router-dom'
import { ExternalLink, HandCoins, RefreshCw, Send, ShieldCheck, X } from 'lucide-react'
import { useFunds } from '../funds/fundApi'
import { useFundLoans, useGuarantorCancelLoan } from '../loans/loanApi'
import { useCurrentUser } from '../auth/authApi'
import {
  MANUAL_DISBURSEMENT_METHOD_LABELS,
  useCancelDisbursement,
  useCancelOfflineDisbursement,
  useCreateOfflineDisbursement,
  useDisburseLoan,
  useGuarantorLoanDisbursement,
  useGuarantorOfflineDisbursement,
  useSubmitOfflineDisbursement,
  useVerifyCapitalPayment,
  type Disbursement,
  type ManualDisbursement,
  type ManualDisbursementMethod,
} from './paymentApi'
import { EvidenceUpload } from '../evidence/EvidenceUpload'
import { PageHeader } from '../../components/ui/PageHeader'
import { Card } from '../../components/ui/Card'
import { StatusBadge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { Modal } from '../../components/ui/Modal'
import { EmptyState, SkeletonCard } from '../../components/ui/State'
import { MoneyDisplay } from '../../lib/money'
import { formatNaira } from '../../lib/formatNaira'
import type { Loan } from '../loans/loanApi'

const PENDING_KEY = 'ftf-disburse-pending'

const cellStack: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'flex-start',
  gap: 6,
}

const cellRow: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  flexWrap: 'wrap',
}

function PendingActions({
  disbursement,
  loan,
}: {
  disbursement: Disbursement
  loan: Loan
}) {
  const verify = useVerifyCapitalPayment(loan.fundId)

  return (
    <>
      <div style={cellRow}>
        {disbursement.authorizationUrl && (
          <a
            className="btn btn--secondary btn--sm"
            href={disbursement.authorizationUrl}
            target="_blank"
            rel="noreferrer"
          >
            <ExternalLink size={14} /> Pay with Paystack
          </a>
        )}
        <Button
          size="sm"
          onClick={() => verify.mutate(disbursement.providerReference)}
          disabled={verify.isPending}
        >
          <ShieldCheck size={14} /> {verify.isPending ? 'Verifying…' : 'Verify payment'}
        </Button>
      </div>
      {verify.isError && (
        <p className="form-error" style={{ margin: 0 }}>
          {(verify.error as Error).message}
        </p>
      )}
    </>
  )
}

const OFFLINE_METHODS: ManualDisbursementMethod[] = ['BankTransfer', 'Cash', 'Other']

/**
 * Modal for the Fund Originator to report an offline payment. It only creates a
 * draft: the ledger is untouched and the member is not notified until the report
 * is submitted (ADR-056).
 */
function OfflineReportForm({ loan, onClose }: { loan: Loan; onClose: () => void }) {
  const create = useCreateOfflineDisbursement()
  const approvedAmount = loan.approvedAmount ?? loan.requestedAmount
  const [method, setMethod] = useState<ManualDisbursementMethod>('BankTransfer')
  const [bankName, setBankName] = useState('')
  const [last4, setLast4] = useState('')
  const [reference, setReference] = useState('')
  const [note, setNote] = useState('')

  const isTransfer = method === 'BankTransfer'

  function submit(e: React.FormEvent) {
    e.preventDefault()
    create.mutate(
      {
        loanId: loan.id,
        amount: approvedAmount,
        method,
        destinationBankName: isTransfer ? bankName.trim() || null : null,
        destinationAccountLast4: isTransfer ? last4.trim() || null : null,
        reference: reference.trim() || null,
        note: note.trim() || null,
      },
      { onSuccess: onClose },
    )
  }

  return (
    <Modal open onClose={onClose} titleId="offline-disbursement-title" maxWidth={520}>
      <h2 id="offline-disbursement-title">Record an offline payment</h2>
      <p className="muted">
        You paid {loan.memberDisplayName || 'the member'} outside the platform. The
        member must confirm receipt before the loan is marked disbursed. Nothing is
        recorded in the ledger until they confirm.
      </p>
      <form onSubmit={submit}>
        <div className="field">
          <label>Amount</label>
          <p className="money" style={{ margin: 0 }}>
            {formatNaira(approvedAmount)}
          </p>
          <p className="field-hint">
            The member receives exactly the approved amount. There is no provider fee
            on an offline payment.
          </p>
        </div>

        <div className="field">
          <label htmlFor="offline-method">Payment method</label>
          <select
            id="offline-method"
            value={method}
            onChange={(e) => setMethod(e.target.value as ManualDisbursementMethod)}
          >
            {OFFLINE_METHODS.map((m) => (
              <option key={m} value={m}>
                {MANUAL_DISBURSEMENT_METHOD_LABELS[m]}
              </option>
            ))}
          </select>
        </div>

        {isTransfer && (
          <div
            className="grid"
            style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: 12 }}
          >
            <div className="field">
              <label htmlFor="offline-bank">Destination bank (optional)</label>
              <input
                id="offline-bank"
                value={bankName}
                onChange={(e) => setBankName(e.target.value)}
                placeholder="e.g. GTBank"
              />
            </div>
            <div className="field">
              <label htmlFor="offline-last4">Account last 4 digits (optional)</label>
              <input
                id="offline-last4"
                value={last4}
                onChange={(e) => setLast4(e.target.value.replace(/\D/g, '').slice(0, 4))}
                inputMode="numeric"
                maxLength={4}
                placeholder="e.g. 6789"
              />
            </div>
          </div>
        )}

        <div className="field">
          <label htmlFor="offline-reference">Reference (optional)</label>
          <input
            id="offline-reference"
            value={reference}
            onChange={(e) => setReference(e.target.value)}
            placeholder="e.g. transfer reference"
          />
        </div>

        <div className="field">
          <label htmlFor="offline-note">Note for the member (optional)</label>
          <textarea
            id="offline-note"
            rows={2}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="Anything the member should know about this payment."
          />
        </div>

        {create.isError && <p className="form-error">{(create.error as Error).message}</p>}

        <div style={cellRow}>
          <Button type="submit" disabled={create.isPending}>
            {create.isPending ? 'Saving…' : 'Save draft'}
          </Button>
          <Button type="button" variant="secondary" onClick={onClose}>
            Cancel
          </Button>
        </div>
      </form>
    </Modal>
  )
}

/**
 * The Fund Originator's view of the latest offline report for a loan: upload
 * proof and submit while it is a draft, watch it while the member decides, or
 * cancel it to retry (the only fallback when the member never responds).
 */
function OfflineReportPanel({
  report,
  loan,
}: {
  report: ManualDisbursement
  loan: Loan
}) {
  const submit = useSubmitOfflineDisbursement()
  const cancel = useCancelOfflineDisbursement()

  if (report.status === 'Confirmed') {
    return (
      <div style={cellStack}>
        <div style={cellRow}>
          <StatusBadge status="Disbursed" withDot />
          <span className="cell-secondary">Confirmed by the member (offline)</span>
        </div>
        <span className="muted" style={{ fontSize: 12 }}>
          {MANUAL_DISBURSEMENT_METHOD_LABELS[report.method]} · {formatNaira(report.amount)}
        </span>
      </div>
    )
  }

  return (
    <div style={cellStack}>
      <div style={cellRow}>
        <StatusBadge status={report.status} withDot />
        <span className="cell-secondary">
          {report.status === 'Draft' && 'Not yet sent to the member'}
          {report.status === 'PendingMemberConfirmation' && 'Awaiting the member’s confirmation'}
          {report.status === 'Disputed' && 'The member says they did not receive this'}
        </span>
      </div>

      <span className="muted" style={{ fontSize: 12 }}>
        {MANUAL_DISBURSEMENT_METHOD_LABELS[report.method]} · {formatNaira(report.amount)}
        {report.reference ? ` · Ref ${report.reference}` : ''}
      </span>

      {report.status === 'Draft' && (
        <>
          <EvidenceUpload
            resourceType="disbursement"
            resourceId={report.id}
            hasEvidence={report.hasEvidence}
          />
          <Button
            size="sm"
            disabled={!report.hasEvidence || submit.isPending}
            onClick={() => submit.mutate(report.id)}
          >
            <Send size={14} />
            {submit.isPending ? 'Sending…' : 'Send to member for confirmation'}
          </Button>
          {!report.hasEvidence && (
            <span className="cell-secondary">Upload proof of payment before sending.</span>
          )}
        </>
      )}

      {report.status === 'Disputed' && report.memberNote && (
        <span className="muted" style={{ fontSize: 12 }}>
          Member: “{report.memberNote}”
        </span>
      )}

      {report.status === 'PendingMemberConfirmation' && (
        <span className="cell-secondary" style={{ fontSize: 12 }}>
          If the member does not respond, cancel this report and try another payment
          method.
        </span>
      )}

      <Button
        size="sm"
        variant="ghost"
        disabled={cancel.isPending}
        onClick={() => {
          if (
            window.confirm(
              `Cancel this offline report for ${loan.memberDisplayName || 'the member'}? The loan returns to Approved and no disbursement is recorded.`,
            )
          ) {
            cancel.mutate({ reportId: report.id, reason: 'Cancelled by Fund Originator' })
          }
        }}
      >
        <X size={14} /> {cancel.isPending ? 'Cancelling…' : 'Cancel report'}
      </Button>

      {submit.isError && (
        <p className="form-error" style={{ margin: 0 }}>
          {(submit.error as Error).message}
        </p>
      )}
      {cancel.isError && (
        <p className="form-error" style={{ margin: 0 }}>
          {(cancel.error as Error).message}
        </p>
      )}
    </div>
  )
}

function DisburseButton({ loan, isOriginator }: { loan: Loan; isOriginator: boolean }) {
  const disburse = useDisburseLoan()
  const cancel = useGuarantorCancelLoan()
  const cancelDisbursement = useCancelDisbursement()
  const { data: disbursement, isLoading } = useGuarantorLoanDisbursement(loan.id)
  const { data: offline, isLoading: loadingOffline } = useGuarantorOfflineDisbursement(loan.id)
  const [showOfflineForm, setShowOfflineForm] = useState(false)

  function initiate() {
    const callbackUrl = `${window.location.origin}/guarantor/disburse`
    disburse.mutate(
      { loanId: loan.id, callbackUrl },
      {
        onSuccess: (result) => {
          if (result.authorizationUrl) {
            sessionStorage.setItem(
              PENDING_KEY,
              JSON.stringify({ loanId: loan.id, fundId: loan.fundId }),
            )
            window.location.assign(result.authorizationUrl)
          }
        },
      },
    )
  }

  if (isLoading || loadingOffline) {
    return <span className="cell-secondary">…</span>
  }

  const liveOffline = offline && offline.status !== 'Cancelled' ? offline : null

  if (!isOriginator) {
    if (liveOffline) {
      return (
        <div style={cellStack}>
          <div style={cellRow}>
            <StatusBadge
              status={liveOffline.status === 'Confirmed' ? 'Disbursed' : liveOffline.status}
              withDot
            />
            <span className="cell-secondary">
              {liveOffline.status === 'Confirmed'
                ? 'Confirmed by the member (offline)'
                : 'Offline payment reported by the Fund Originator'}
            </span>
          </div>
        </div>
      )
    }
    if (loan.status === 'Approved' || loan.status === 'DisbursementPending') {
      return (
        <span className="cell-secondary">
          Only the Fund Originator can disburse this loan.
        </span>
      )
    }
    return <span className="cell-secondary">—</span>
  }

  // A live or confirmed offline report takes precedence over the provider path
  // so a loan can never be double-disbursed (ADR-056).
  if (liveOffline) {
    return <OfflineReportPanel report={liveOffline} loan={loan} />
  }

  if (disbursement) {
    if (disbursement.status === 'Failed') {
      return (
        <div style={cellStack}>
          <div style={cellRow}>
            <StatusBadge status={disbursement.status} withDot />
          </div>
          {disbursement.failureReason && (
            <span className="muted" style={{ fontSize: 12 }}>
              {disbursement.failureReason}
            </span>
          )}
          <div style={cellRow}>
            <Button
              size="sm"
              onClick={initiate}
              disabled={disburse.isPending}
            >
              <RefreshCw size={14} /> {disburse.isPending ? 'Initiating…' : 'Retry payment'}
            </Button>
            <Button
              size="sm"
              variant="ghost"
              disabled={cancelDisbursement.isPending}
              onClick={() => {
                if (
                  window.confirm(
                    `Cancel this disbursement for ${loan.memberDisplayName || 'the member'}?`,
                  )
                ) {
                  cancelDisbursement.mutate({ loanId: loan.id })
                }
              }}
            >
              <X size={14} /> {cancelDisbursement.isPending ? 'Cancelling…' : 'Cancel'}
            </Button>
          </div>
        </div>
      )
    }

    return (
      <div style={cellStack}>
        <div style={cellRow}>
          <StatusBadge status={disbursement.status} withDot />
          {disbursement.status === 'Pending' && (
            <span className="cell-secondary">Payment not yet confirmed</span>
          )}
        </div>

        {disbursement.status === 'Pending' && disbursement.grossAmount != null && (
          <p className="cell-secondary" style={{ margin: 0 }}>
            You pay {formatNaira(disbursement.grossAmount)} in total
            {disbursement.estimatedFee != null
              ? ` (fee ${formatNaira(disbursement.estimatedFee)})`
              : ''}.
          </p>
        )}

        {disbursement.failureReason && (
          <span className="muted" style={{ fontSize: 12 }}>
            {disbursement.failureReason}
          </span>
        )}

        {disbursement.status === 'Pending' && (
          <PendingActions disbursement={disbursement} loan={loan} />
        )}

        {disbursement.status === 'Pending' && (
          <Button
            size="sm"
            variant="ghost"
            disabled={cancelDisbursement.isPending}
            onClick={() => {
              if (
                window.confirm(
                  `Cancel this disbursement for ${loan.memberDisplayName || 'the member'}? The loan will be cancelled and you won't need to complete payment.`,
                )
              ) {
                cancelDisbursement.mutate({ loanId: loan.id })
              }
            }}
          >
            <X size={14} /> {cancelDisbursement.isPending ? 'Cancelling…' : 'Cancel disbursement'}
          </Button>
        )}
      </div>
    )
  }

  if (loan.status !== 'Approved') {
    return <span className="cell-secondary">—</span>
  }

  const error = disburse.error as Error | null

  return (
    <div>
      <div style={cellRow}>
        <Button size="sm" onClick={initiate} disabled={disburse.isPending}>
          <Send size={14} /> {disburse.isPending ? 'Initiating…' : 'Disburse'}
        </Button>
        <Button
          size="sm"
          variant="secondary"
          onClick={() => setShowOfflineForm(true)}
        >
          <HandCoins size={14} /> Record offline payment
        </Button>
        <Button
          size="sm"
          variant="ghost"
          disabled={cancel.isPending}
          onClick={() => {
            if (
              window.confirm(
                `Cancel this approved loan for ${loan.memberDisplayName || 'the member'}? It has not been disbursed yet.`,
              )
            ) {
              cancel.mutate({ loanId: loan.id })
            }
          }}
        >
          <X size={14} /> {cancel.isPending ? 'Cancelling…' : 'Cancel'}
        </Button>
      </div>
      {error && <p className="form-error" style={{ marginTop: 4 }}>{error.message}</p>}
      {showOfflineForm && (
        <OfflineReportForm loan={loan} onClose={() => setShowOfflineForm(false)} />
      )}
    </div>
  )
}

function DisbursementReturnBanner({
  fundId,
  reference,
  onDone,
}: {
  fundId: string
  reference: string
  onDone: () => void
}) {
  const verify = useVerifyCapitalPayment(fundId)
  const [outcome, setOutcome] = useState<'verifying' | 'confirmed' | 'failed'>('verifying')
  const [detail, setDetail] = useState('')

  useEffect(() => {
    verify.mutate(reference, {
      onSuccess: (tx) => {
        setOutcome(tx.status === 'Confirmed' ? 'confirmed' : 'failed')
        setDetail(
          tx.status === 'Confirmed'
            ? 'Your payment was confirmed and the loan has been disbursed to the member.'
            : tx.failureReason ?? 'The payment was not confirmed.',
        )
      },
      onError: (e) => {
        setOutcome('failed')
        setDetail((e as Error).message)
      },
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return (
    <section className={`capital-return capital-return--${outcome}`} aria-live="polite">
      <h3>
        {outcome === 'verifying' && 'Verifying your payment…'}
        {outcome === 'confirmed' && 'Loan disbursed'}
        {outcome === 'failed' && 'Payment could not be confirmed'}
      </h3>
      {outcome === 'verifying' && (
        <p className="muted">
          Contacting the payment provider. This is a server-confirmed transaction.
        </p>
      )}
      {outcome !== 'verifying' && <p className="muted">{detail}</p>}
      {outcome !== 'verifying' && (
        <Button type="button" variant="secondary" size="sm" onClick={onDone}>
          Dismiss
        </Button>
      )}
    </section>
  )
}

export function GuarantorDisbursePage() {
  const { data: funds, isLoading: loadingFunds } = useFunds()
  const { data: user } = useCurrentUser()
  const [searchParams, setSearchParams] = useSearchParams()
  const [fundId, setFundId] = useState(() => searchParams.get('fund') ?? '')
  const selectedFundId = fundId || (funds && funds.length > 0 ? funds[0].id : '')
  const selectedFund = funds?.find((f) => f.id === selectedFundId)
  const isOriginator = !!selectedFund && selectedFund.guarantorId === user?.id
  const { data: loans, isLoading: loadingLoans } = useFundLoans(selectedFundId ?? null)
  const [returnBanner, setReturnBanner] = useState<{ fundId: string; reference: string } | null>(null)
  const highlightLoanId = searchParams.get('loan')

  useEffect(() => {
    const pendingRaw = sessionStorage.getItem(PENDING_KEY)
    if (!pendingRaw) return
    const reference = new URLSearchParams(window.location.search).get('reference')
    if (!reference) return

    let pending: { loanId?: string; fundId?: string } | null = null
    try {
      pending = JSON.parse(pendingRaw) as { loanId?: string; fundId?: string }
    } catch {
      // ignore malformed stored state
    }

    if (!pending?.fundId) return

    sessionStorage.removeItem(PENDING_KEY)
    window.history.replaceState({}, '', window.location.pathname)
    // oxlint-disable-next-line react/set-state-in-effect
    setReturnBanner({ fundId: pending.fundId, reference })
  }, [])

  // When arriving straight from approval (?fund=&loan=), focus the just-approved
  // loan so the Guarantor can start the payout immediately.
  useEffect(() => {
    if (!highlightLoanId || !loans || loans.length === 0) return
    const id = `loan-row-${highlightLoanId}`
    if (!loans.some((l) => l.id === highlightLoanId)) return
    const el = document.getElementById(id)
    if (!el) return
    el.scrollIntoView({ block: 'center', behavior: 'smooth' })
    setSearchParams({}, { replace: true })
  }, [highlightLoanId, loans, setSearchParams])

  return (
    <div>
      <PageHeader
        title="Loan Disbursements"
        subtitle="Approve a member payout by paying at checkout — the approved amount plus the provider fee — after which the member is settled through their verified account."
      />

      {returnBanner && (
        <DisbursementReturnBanner
          fundId={returnBanner.fundId}
          reference={returnBanner.reference}
          onDone={() => setReturnBanner(null)}
        />
      )}

      {loadingFunds && <SkeletonCard />}

      {funds && funds.length > 0 ? (
        <>
          <div className="field" style={{ maxWidth: 360, marginBottom: 20 }}>
            <label htmlFor="disburse-fund">Fund</label>
            <select id="disburse-fund" value={selectedFundId} onChange={(e) => setFundId(e.target.value)}>
              {funds.map((f) => (
                <option key={f.id} value={f.id}>
                  {f.name}
                </option>
              ))}
            </select>
          </div>

          {loadingLoans && <SkeletonCard />}

          {loans && loans.length > 0 ? (
            <Card className="table-card">
              <div className="card-title">
                <h2>Loans in {funds.find((f) => f.id === selectedFundId)?.name}</h2>
              </div>
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Member</th>
                    <th>Amount</th>
                    <th>Status</th>
                    <th>Disbursement</th>
                  </tr>
                </thead>
                <tbody>
                  {loans.map((l) => (
                    <tr
                      key={l.id}
                      id={`loan-row-${l.id}`}
                      className={l.id === highlightLoanId ? 'row--highlight' : undefined}
                    >
                      <td>
                        <strong>{l.memberDisplayName || 'Member'}</strong>
                      </td>
                      <td>
                        <MoneyDisplay amount={l.approvedAmount ?? l.requestedAmount} />
                      </td>
                      <td>
                        <StatusBadge status={l.status} withDot />
                      </td>
                      <td>
                        <DisburseButton loan={l} isOriginator={isOriginator} />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </Card>
          ) : (
            !loadingLoans && (
              <Card>
                <EmptyState
                  icon={<HandCoins size={24} />}
                  title="No loans in this fund"
                  description="Approved loans will appear here ready for disbursement."
                />
              </Card>
            )
          )}
        </>
      ) : (
        !loadingFunds && (
          <Card>
            <EmptyState
              icon={<HandCoins size={24} />}
              title="No funds yet"
              description="Create a fund before disbursing loans."
            />
          </Card>
        )
      )}
    </div>
  )
}