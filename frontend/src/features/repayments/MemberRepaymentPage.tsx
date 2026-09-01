import { useState } from 'react'
import { Wallet, HandCoins, ArrowDownCircle, BadgeCheck } from 'lucide-react'
import { useMyLoans } from '../loans/loanApi'
import {
  useScheduleItems,
  useRepaymentSummary,
  useRepaymentHistory,
  useMakeScheduledPayment,
  useMakeLumpSum,
  useSettleLoan,
} from './repaymentApi'
import { PageHeader } from '../../components/ui/PageHeader'
import { Card } from '../../components/ui/Card'
import { KpiCard } from '../../components/ui/KpiCard'
import { Button } from '../../components/ui/Button'
import { StatusBadge } from '../../components/ui/Badge'
import { EmptyState, SkeletonCard } from '../../components/ui/State'
import { MoneyDisplay } from '../../lib/money'

export function MemberRepaymentPage() {
  const { data: loans, isLoading } = useMyLoans()
  const activeLoans = loans?.filter((l) => l.status === 'Disbursed') ?? []
  const [loanId, setLoanId] = useState('')

  const selectedId = loanId || activeLoans[0]?.id

  const { data: schedule, isLoading: loadingSchedule } = useScheduleItems(selectedId ?? null)
  const { data: summary, isLoading: loadingSummary } = useRepaymentSummary(selectedId ?? null)
  const { data: history } = useRepaymentHistory(selectedId ?? null)

  const [amount, setAmount] = useState('')
  const pay = useMakeScheduledPayment()
  const lump = useMakeLumpSum()
  const settle = useSettleLoan()

  const amountNumber = Number(amount) || 0
  const loan = loans?.find((l) => l.id === selectedId)

  const selectedLoanError =
    pay.error || lump.error || settle.error
      ? ((pay.error || lump.error || settle.error) as Error).message
      : null

  return (
    <div>
      <PageHeader
        title="Repayments"
        subtitle="View your repayment schedule, make payments, or settle your loan early."
      />

      {isLoading && <SkeletonCard />}

      {loans && activeLoans.length === 0 ? (
        <Card>
          <EmptyState
            icon={<HandCoins size={24} />}
            title="No active loans"
            description="You have no disbursed loans with an outstanding repayment schedule."
          />
        </Card>
      ) : (
        loans && (
          <>
            <div className="field" style={{ maxWidth: 360, marginBottom: 20 }}>
              <label htmlFor="repay-loan">Active loan</label>
              <select
                id="repay-loan"
                value={selectedId}
                onChange={(e) => {
                  setLoanId(e.target.value)
                  setAmount('')
                }}
              >
                {activeLoans.map((l) => (
                  <option key={l.id} value={l.id}>
                    {l.fundName}
                  </option>
                ))}
              </select>
            </div>

            {selectedLoanError && <p className="error-text">{selectedLoanError}</p>}

            {loadingSummary && <SkeletonCard />}

            {summary && (
              <div className="kpi-grid">
                <KpiCard
                  label="Outstanding Balance"
                  value={<MoneyDisplay amount={summary.outstandingBalance} />}
                  icon={<Wallet size={18} />}
                  tone="info"
                />
                <KpiCard
                  label="Settlement Quote"
                  value={<MoneyDisplay amount={summary.settlementQuote} />}
                  hint="To settle now"
                  icon={<BadgeCheck size={18} />}
                  tone="success"
                />
                <KpiCard
                  label="Overdue"
                  value={`${summary.overdueItems} item${summary.overdueItems === 1 ? '' : 's'}`}
                  hint={<MoneyDisplay amount={summary.overdueAmount} />}
                  icon={<ArrowDownCircle size={18} />}
                  tone={summary.overdueItems > 0 ? 'danger' : 'neutral'}
                />
                <KpiCard
                  label="Repayment Surplus"
                  value={<MoneyDisplay amount={summary.totalSurplus} />}
                  icon={<HandCoins size={18} />}
                  tone={summary.totalSurplus >= 0 ? 'success' : 'neutral'}
                />
              </div>
            )}

            <div
              className="grid"
              style={{
                gridTemplateColumns: 'repeat(auto-fit, minmax(320px, 1fr))',
                gap: 24,
                marginTop: 24,
              }}
            >
              <Card title="Repayment Schedule">
                {loadingSchedule ? (
                  <SkeletonCard />
                ) : schedule && schedule.length > 0 ? (
                  <table className="data-table">
                    <thead>
                      <tr>
                        <th>#</th>
                        <th>Due</th>
                        <th>Expected</th>
                        <th>Paid</th>
                        <th>Status</th>
                      </tr>
                    </thead>
                    <tbody>
                      {schedule.map((item) => (
                        <tr key={item.id}>
                          <td>{item.sequence}</td>
                          <td className="cell-secondary">
                            {new Date(item.dueDateUtc).toLocaleDateString()}
                          </td>
                          <td>
                            <MoneyDisplay amount={item.expectedAmount} />
                          </td>
                          <td className="cell-secondary">
                            <MoneyDisplay amount={item.paidAmount} />
                          </td>
                          <td>
                            <StatusBadge status={item.status} withDot />
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                ) : (
                  <EmptyState
                    icon={<HandCoins size={20} />}
                    title="No schedule"
                    description="A repayment schedule has not been generated yet."
                  />
                )}
              </Card>

              <div className="stack" style={{ gap: 20 }}>
                <Card title="Make a payment">
                  <div className="field">
                    <label htmlFor="repay-amount">Amount (₦)</label>
                    <input
                      id="repay-amount"
                      type="number"
                      min="0"
                      value={amount}
                      onChange={(e) => setAmount(e.target.value)}
                      placeholder="0.00"
                    />
                  </div>
                  <div className="row" style={{ gap: 12, marginTop: 8 }}>
                    <Button
                      disabled={amountNumber <= 0 || !selectedId}
                      onClick={() =>
                        selectedId &&
                        pay.mutate({ loanId: selectedId, amount: amountNumber })
                      }
                    >
                      Pay Instalment
                    </Button>
                    <Button
                      variant="secondary"
                      disabled={amountNumber <= 0 || !selectedId}
                      onClick={() =>
                        selectedId &&
                        lump.mutate({ loanId: selectedId, amount: amountNumber })
                      }
                    >
                      Lump Sum
                    </Button>
                  </div>
                  {pay.isPending || lump.isPending ? (
                    <p className="muted" style={{ marginTop: 8 }}>
                      Processing…
                    </p>
                  ) : null}
                </Card>

                <Card title="Settle loan">
                  <p className="muted">
                    Settling pays the full outstanding balance in one payment and
                    completes your loan. If you pay more than the quote, the
                    difference is recorded as repayment surplus.
                  </p>
                  <div className="row" style={{ gap: 12, marginTop: 12 }}>
                    <Button
                      variant="secondary"
                      disabled={!selectedId}
                      onClick={() =>
                        selectedId && summary &&
                        settle.mutate({ loanId: selectedId, amount: summary.settlementQuote })
                      }
                    >
                      Settle now
                    </Button>
                  </div>
                  {loan?.status === 'Disbursed' && summary ? (
                    <p className="muted" style={{ marginTop: 8 }}>
                      Quote: <MoneyDisplay amount={summary.settlementQuote} />
                    </p>
                  ) : null}
                </Card>
              </div>
            </div>

            <div style={{ marginTop: 24 }}>
              <Card title="Payment history">
                {history && history.length > 0 ? (
                  <table className="data-table">
                    <thead>
                      <tr>
                        <th>Date</th>
                        <th>Kind</th>
                        <th>Expected</th>
                        <th>Actual</th>
                        <th>Surplus</th>
                      </tr>
                    </thead>
                    <tbody>
                      {history.map((r) => (
                        <tr key={r.id}>
                          <td className="cell-secondary">
                            {new Date(r.paidAtUtc).toLocaleString()}
                          </td>
                          <td>
                            <StatusBadge status={r.kind} withDot />
                          </td>
                          <td>
                            <MoneyDisplay amount={r.expectedAmount} />
                          </td>
                          <td>
                            <MoneyDisplay amount={r.actualAmount} />
                          </td>
                          <td>
                            <MoneyDisplay amount={r.surplus} />
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                ) : (
                  <EmptyState
                    icon={<Wallet size={20} />}
                    title="No payments yet"
                    description="Payments you make will appear here."
                  />
                )}
              </Card>
            </div>
          </>
        )
      )}
    </div>
  )
}
