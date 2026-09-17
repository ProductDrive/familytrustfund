import { useEffect, useState } from 'react'
import type { CSSProperties } from 'react'
import { useSearchParams } from 'react-router-dom'
import { ExternalLink, HandCoins, RefreshCw, Send, ShieldCheck, X } from 'lucide-react'
import { useFunds } from '../funds/fundApi'
import { useFundLoans, useGuarantorCancelLoan } from '../loans/loanApi'
import {
  useDisburseLoan,
  useGuarantorLoanDisbursement,
  useVerifyCapitalPayment,
  type Disbursement,
} from './paymentApi'
import { PageHeader } from '../../components/ui/PageHeader'
import { Card } from '../../components/ui/Card'
import { StatusBadge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
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

function DisburseButton({ loan }: { loan: Loan }) {
  const disburse = useDisburseLoan()
  const cancel = useGuarantorCancelLoan()
  const { data: disbursement, isLoading } = useGuarantorLoanDisbursement(loan.id)

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

  if (isLoading) {
    return <span className="cell-secondary">…</span>
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
          <Button
            size="sm"
            onClick={initiate}
            disabled={disburse.isPending}
          >
            <RefreshCw size={14} /> {disburse.isPending ? 'Initiating…' : 'Retry payment'}
          </Button>
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
  const [searchParams, setSearchParams] = useSearchParams()
  const [fundId, setFundId] = useState(() => searchParams.get('fund') ?? '')
  const selectedFundId = fundId || (funds && funds.length > 0 ? funds[0].id : '')
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
                        <DisburseButton loan={l} />
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