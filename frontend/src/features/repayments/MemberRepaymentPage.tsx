import { useState } from 'react'
import { Wallet, HandCoins, ArrowDownCircle, BadgeCheck, ChevronLeft, ChevronRight } from 'lucide-react'
import { useMyLoans } from '../loans/loanApi'
import {
  useScheduleItems,
  useRepaymentSummary,
  useMyPendingRepayments,
  useMyRepaymentHistory,
  useDeclareRepayment,
} from './repaymentApi'
import type { RepaymentKind } from './repaymentApi'
import { PageHeader } from '../../components/ui/PageHeader'
import { Card } from '../../components/ui/Card'
import { KpiCard } from '../../components/ui/KpiCard'
import { Button } from '../../components/ui/Button'
import { StatusBadge } from '../../components/ui/Badge'
import { AmountInput } from '../../components/ui/AmountInput'
import { EmptyState, SkeletonCard } from '../../components/ui/State'
import { MoneyDisplay } from '../../lib/money'
import { formatShortDate, formatShortDateTime } from '../../lib/formatDate'
import { EvidenceUpload } from '../evidence/EvidenceUpload'

export function MemberRepaymentPage() {
  const { data: loans, isLoading } = useMyLoans()
  const activeLoans = loans?.filter((l) => l.status === 'Disbursed') ?? []
  const [loanId, setLoanId] = useState('')

  const selectedId = loanId || activeLoans[0]?.id

  const { data: schedule, isLoading: loadingSchedule } = useScheduleItems(selectedId ?? null)
  const { data: summary, isLoading: loadingSummary } = useRepaymentSummary(selectedId ?? null)
  const { data: pendingMine } = useMyPendingRepayments()
  const declare = useDeclareRepayment()

  const pendingForLoan = (pendingMine ?? []).filter(
    (p) => p.loanId === selectedId && p.status !== 'Confirmed',
  )

  const [amount, setAmount] = useState('')
  const [kind, setKind] = useState<RepaymentKind>('Scheduled')
  const [reference, setReference] = useState('')
  const [note, setNote] = useState('')

  const amountNumber = Number(amount) || 0

  function submit(e: React.FormEvent) {
    e.preventDefault()
    if (!selectedId || amountNumber <= 0) return
    declare.mutate({
      loanId: selectedId,
      amount: amountNumber,
      kind,
      reference: reference.trim() || null,
      note: note.trim() || null,
    })
  }

  return (
    <div>
      <PageHeader
        title="Repayments"
        subtitle="Report a payment so your Guarantor can confirm it against your loan."
      />

      {isLoading && <SkeletonCard />}

      {loans && activeLoans.length === 0 ? (
        <Card>
          <EmptyState
            icon={<HandCoins size={24} />}
            title="No active loans"
            description="You have no disbursed loans with an outstanding repayment schedule. Your past payments are listed below."
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

            {declare.error && <p className="error-text">{(declare.error as Error).message}</p>}

            {loadingSummary && <SkeletonCard />}

            {summary && (
              <div className="kpi-grid">
                <KpiCard
                  label="Total Outstanding"
                  value={<MoneyDisplay amount={summary.outstandingBalance} />}
                  hint={
                    summary.outstandingInterest > 0
                      ? `Includes ${summary.outstandingInterest.toLocaleString()} interest`
                      : 'Principal only'
                  }
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
                  <div className="table-scroll">
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
                            {formatShortDate(item.dueDateUtc)}
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
                  </div>
                ) : (
                  <EmptyState
                    icon={<HandCoins size={20} />}
                    title="No schedule"
                    description="A repayment schedule has not been generated yet."
                  />
                )}
              </Card>

              <div className="stack" style={{ gap: 20 }}>
                <Card title="Report a payment">
                  <p className="muted">
                    Report the payment you made to your Guarantor. Your repayment is
                    recorded only after they confirm it.
                  </p>
                  <form onSubmit={submit}>
                    <div className="field">
                      <label htmlFor="repay-kind">Payment type</label>
                      <select
                        id="repay-kind"
                        value={kind}
                        onChange={(e) => setKind(e.target.value as RepaymentKind)}
                      >
                        <option value="Scheduled">Instalment</option>
                        <option value="LumpSum">Lump sum</option>
                        <option value="FullSettlement">Full settlement</option>
                      </select>
                    </div>

                    <div className="field">
                      <label htmlFor="repay-amount">Amount (₦)</label>
                      <AmountInput
                        id="repay-amount"
                        value={amount}
                        onChange={setAmount}
                        placeholder={
                          kind === 'FullSettlement'
                            ? String(summary?.settlementQuote ?? 0)
                            : '0.00'
                        }
                        required
                      />
                      {kind === 'FullSettlement' && summary ? (
                        <p className="field-hint">
                          Current settlement quote (includes any remaining interest):{' '}
                          <MoneyDisplay amount={summary.settlementQuote} />
                        </p>
                      ) : null}
                    </div>

                    <div className="field">
                      <label htmlFor="repay-reference">Reference (optional)</label>
                      <input
                        id="repay-reference"
                        value={reference}
                        onChange={(e) => setReference(e.target.value)}
                        placeholder="e.g. bank transfer reference"
                      />
                    </div>

                    <div className="field">
                      <label htmlFor="repay-note">Note (optional)</label>
                      <textarea
                        id="repay-note"
                        value={note}
                        onChange={(e) => setNote(e.target.value)}
                        rows={2}
                        placeholder="Anything your Guarantor should know"
                      />
                    </div>

                    <Button type="submit" disabled={amountNumber <= 0 || !selectedId || declare.isPending}>
                      {declare.isPending ? 'Submitting…' : 'Report payment'}
                    </Button>
                  </form>
                </Card>

                <Card title="Awaiting confirmation">
                  {pendingForLoan.length > 0 ? (
                    <div className="stack">
                      {pendingForLoan.map((p) => (
                        <div key={p.id} className="card">
                          <p>
                            <strong>
                              <MoneyDisplay amount={p.amount} />
                            </strong>
                          </p>
                          <StatusBadge status={p.status} withDot />
                          <ul className="property-list" style={{ marginTop: 8 }}>
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
                            <li>
                              <span className="property-label">Reported</span>
                              <span className="property-value">
                                {formatShortDateTime(p.reportedAtUtc)}
                              </span>
                            </li>
                          </ul>
                          <div style={{ marginTop: 12 }}>
                            <EvidenceUpload
                              resourceType="repayment"
                              resourceId={p.id}
                              hasEvidence={p.hasEvidence}
                            />
                          </div>
                          {p.rejectionReason && (
                            <p className="form-error" style={{ marginTop: 8 }}>
                              Rejected: {p.rejectionReason}
                            </p>
                          )}
                        </div>
                      ))}
                    </div>
                  ) : (
                    <EmptyState
                      icon={<BadgeCheck size={20} />}
                      title="Nothing pending"
                      description="Report a payment above; your Guarantor will confirm it here."
                    />
                  )}
                </Card>
              </div>
            </div>
          </>
        )
      )}

      <div style={{ marginTop: 24 }}>
        <PaymentHistory />
      </div>
    </div>
  )
}

function PaymentHistory() {
  const pageSize = 10
  const [page, setPage] = useState(1)
  const { data, isLoading, error } = useMyRepaymentHistory(page, pageSize)

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1

  return (
    <Card title="Payment history">
      {isLoading && <SkeletonCard />}
      {error && <p className="error-text">{(error as Error).message}</p>}

      {data && data.totalCount > 0 ? (
        <>
          <div className="table-scroll">
            <table className="data-table">
              <thead>
                <tr>
                  <th>Date</th>
                  <th>Fund</th>
                  <th>Kind</th>
                  <th>Expected</th>
                  <th>Actual</th>
                  <th>Surplus</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((r) => (
                  <tr key={r.id}>
                    <td className="cell-secondary">{formatShortDateTime(r.paidAtUtc)}</td>
                    <td>{r.fundName || <span className="cell-secondary">—</span>}</td>
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
          </div>

          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: 12,
              flexWrap: 'wrap',
              marginTop: 12,
            }}
          >
            <p className="muted" style={{ fontSize: 13 }}>
              {data.totalCount} confirmed payment{data.totalCount === 1 ? '' : 's'}
            </p>
            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
              <Button
                size="sm"
                variant="secondary"
                disabled={page <= 1 || isLoading}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
              >
                <ChevronLeft size={14} /> Previous
              </Button>
              <span className="muted" style={{ fontSize: 13, minWidth: 72, textAlign: 'center' }}>
                Page {data.page} of {totalPages}
              </span>
              <Button
                size="sm"
                variant="secondary"
                disabled={page >= totalPages || isLoading}
                onClick={() => setPage((p) => p + 1)}
              >
                Next <ChevronRight size={14} />
              </Button>
            </div>
          </div>
        </>
      ) : (
        !isLoading &&
        !error && (
          <EmptyState
            icon={<Wallet size={20} />}
            title="No payments yet"
            description="Payments your Guarantor has confirmed will appear here."
          />
        )
      )}
    </Card>
  )
}