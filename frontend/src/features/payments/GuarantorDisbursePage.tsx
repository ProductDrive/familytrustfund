import { useState } from 'react'
import { Send, HandCoins } from 'lucide-react'
import { useFunds } from '../funds/fundApi'
import { useFundLoans } from '../loans/loanApi'
import { useDisburseLoan, useGuarantorLoanDisbursement } from './paymentApi'
import { PageHeader } from '../../components/ui/PageHeader'
import { Card } from '../../components/ui/Card'
import { StatusBadge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { EmptyState, SkeletonCard } from '../../components/ui/State'
import { MoneyDisplay } from '../../lib/money'
import type { Loan } from '../loans/loanApi'

function DisburseButton({ loan }: { loan: Loan }) {
  const disburse = useDisburseLoan()
  const { data: disbursement, isLoading } = useGuarantorLoanDisbursement(loan.id)

  if (isLoading) {
    return <span className="cell-secondary">…</span>
  }

  if (disbursement) {
    return (
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
        <StatusBadge status={disbursement.status} withDot />
        {disbursement.failureReason && (
          <span className="muted" style={{ fontSize: 12 }}>
            {disbursement.failureReason}
          </span>
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
      <Button
        size="sm"
        onClick={() => disburse.mutate({ loanId: loan.id })}
        disabled={disburse.isPending}
      >
        <Send size={14} /> {disburse.isPending ? 'Initiating…' : 'Disburse'}
      </Button>
      {error && <p className="form-error" style={{ marginTop: 4 }}>{error.message}</p>}
    </div>
  )
}

export function GuarantorDisbursePage() {
  const { data: funds, isLoading: loadingFunds } = useFunds()
  const [fundId, setFundId] = useState('')
  const selectedFundId = fundId || funds?.[0]?.id
  const { data: loans, isLoading: loadingLoans } = useFundLoans(selectedFundId ?? null)

  return (
    <div>
      <PageHeader
        title="Loan Disbursements"
        subtitle="Initiate disbursement of approved loans to members' verified bank accounts."
      />

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
                    <tr key={l.id}>
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
