import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { HandCoins, CircleDollarSign, X } from 'lucide-react'
import { useMyMemberships } from '../membership/membershipApi'
import { useMyLoans, useRequestLoan, useCancelLoan } from './loanApi'
import type { Loan } from './loanApi'
import { useMyLoanDisbursement } from '../payments/paymentApi'
import { MemberBankDetails } from '../payments/MemberBankDetails'
import { PageHeader } from '../../components/ui/PageHeader'
import { Card } from '../../components/ui/Card'
import { StatusBadge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { AmountInput } from '../../components/ui/AmountInput'
import { EmptyState, SkeletonCard } from '../../components/ui/State'
import { MoneyDisplay } from '../../lib/money'
import { formatShortDate } from '../../lib/formatDate'

const FREQUENCIES = ['Weekly', 'Biweekly', 'Monthly'] as const

function DisbursementCell({ loanId }: { loanId: string }) {
  const { data: disbursement, isLoading } = useMyLoanDisbursement(loanId)
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

function LoanRequestForm({ onDone }: { onDone: () => void }) {
  const requestLoan = useRequestLoan()
  const { data: memberships } = useMyMemberships()
  const activeMemberships = memberships?.filter((m) => m.status === 'Active') ?? []

  const [fundId, setFundId] = useState('')
  const [amount, setAmount] = useState('')
  const [frequency, setFrequency] = useState<string>('Monthly')
  const [purpose, setPurpose] = useState('')

  function submit(e: React.FormEvent) {
    e.preventDefault()
    const parsed = Number(amount)
    if (!fundId || !parsed || parsed <= 0) return
    requestLoan.mutate(
      {
        fundId,
        amount: parsed,
        frequency: frequency as 'Weekly' | 'Biweekly' | 'Monthly',
        purpose: purpose.trim() || null,
      },
      { onSuccess: onDone },
    )
  }

  return (
    <Card title="Request a loan">
      <p className="muted">
        You can request a loan from any fund you have a membership in. Repayment
        frequency is finalised at approval.
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
            The Guarantor may approve a lower amount than requested.
          </p>
        </div>

        <div className="field">
          <label htmlFor="loan-frequency">Repayment frequency</label>
          <select
            id="loan-frequency"
            value={frequency}
            onChange={(e) => setFrequency(e.target.value)}
          >
            {FREQUENCIES.map((f) => (
              <option key={f} value={f}>
                {f}
              </option>
            ))}
          </select>
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

        {requestLoan.isError && (
          <p className="form-error">{(requestLoan.error as Error).message}</p>
        )}

        <Button type="submit" disabled={requestLoan.isPending || activeMemberships.length === 0}>
          {requestLoan.isPending ? 'Submitting…' : 'Submit loan request'}
        </Button>
      </form>
    </Card>
  )
}

export function MemberLoansPage() {
  const navigate = useNavigate()
  const { data: loans, isLoading, error } = useMyLoans()
  const [showForm, setShowForm] = useState(false)

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
                    <MoneyDisplay amount={l.requestedAmount} />
                  </td>
                  <td>{l.approvedFrequency ?? l.requestedFrequency}</td>
                  <td>
                    <StatusBadge status={l.status} withDot />
                  </td>
                  <td>
                    <DisbursementCell loanId={l.id} />
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
