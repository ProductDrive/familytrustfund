import { useState } from 'react'
import { PiggyBank, Plus, Wallet, BadgeCheck } from 'lucide-react'
import { useMyMemberships } from '../membership/membershipApi'
import {
  useMyContributions,
  useMyFundCredit,
  useReportContribution,
} from './contributionApi'
import { PageHeader } from '../../components/ui/PageHeader'
import { Card } from '../../components/ui/Card'
import { StatusBadge } from '../../components/ui/Badge'
import { KpiCard } from '../../components/ui/KpiCard'
import { Button } from '../../components/ui/Button'
import { EmptyState, SkeletonCard } from '../../components/ui/State'
import { MoneyDisplay } from '../../lib/money'

function ReportContributionForm({ onDone }: { onDone: () => void }) {
  const report = useReportContribution()
  const { data: memberships } = useMyMemberships()
  const activeFamilyMemberships =
    memberships?.filter((m) => m.status === 'Active' && m.fundType === 'Family') ?? []

  const [fundId, setFundId] = useState('')
  const [amount, setAmount] = useState('')
  const [reference, setReference] = useState('')
  const [note, setNote] = useState('')

  function submit(e: React.FormEvent) {
    e.preventDefault()
    const parsed = Number(amount)
    if (!fundId || !parsed || parsed <= 0) return
    report.mutate(
      {
        fundId,
        amount: parsed,
        reference: reference.trim() || null,
        note: note.trim() || null,
      },
      { onSuccess: onDone },
    )
  }

  return (
    <Card title="Report a contribution">
      <p className="muted">
        Contributions are made to your Guarantor and verified by them before
        they are added to your Fund Credit.
      </p>
      <form onSubmit={submit}>
        <div className="field">
          <label htmlFor="contribution-fund">Fund</label>
          <select
            id="contribution-fund"
            value={fundId}
            onChange={(e) => setFundId(e.target.value)}
            required
          >
            <option value="">Select a Family fund…</option>
            {activeFamilyMemberships.map((m) => (
              <option key={m.fundId} value={m.fundId}>
                {m.fundName}
              </option>
            ))}
          </select>
          {activeFamilyMemberships.length === 0 && (
            <p className="field-hint">
              You need an active membership in a Family fund to contribute.
            </p>
          )}
        </div>

        <div className="field">
          <label htmlFor="contribution-amount">Amount (₦)</label>
          <input
            id="contribution-amount"
            type="number"
            min="1"
            step="0.01"
            value={amount}
            onChange={(e) => setAmount(e.target.value)}
            placeholder="e.g. 50000"
            required
          />
        </div>

        <div className="field">
          <label htmlFor="contribution-reference">Reference (optional)</label>
          <input
            id="contribution-reference"
            value={reference}
            onChange={(e) => setReference(e.target.value)}
            placeholder="e.g. bank transfer reference"
          />
        </div>

        <div className="field">
          <label htmlFor="contribution-note">Note (optional)</label>
          <textarea
            id="contribution-note"
            value={note}
            onChange={(e) => setNote(e.target.value)}
            rows={2}
            placeholder="Anything you want your Guarantor to know"
          />
        </div>

        {report.isError && (
          <p className="form-error">{(report.error as Error).message}</p>
        )}

        <Button type="submit" disabled={report.isPending || activeFamilyMemberships.length === 0}>
          {report.isPending ? 'Submitting…' : 'Submit contribution'}
        </Button>
      </form>
    </Card>
  )
}

export function MemberContributionsPage() {
  const { data: summaries, isLoading: loadingSummary } = useMyFundCredit()
  const { data: contributions, isLoading, error } = useMyContributions()
  const [showForm, setShowForm] = useState(false)

  const totalFundCredit = summaries?.reduce((sum, s) => sum + s.fundCredit, 0) ?? 0
  const totalEntitlement =
    summaries?.reduce((sum, s) => sum + (s.borrowingEntitlement ?? 0), 0) ?? 0

  return (
    <div>
      <PageHeader
        title="Contributions"
        subtitle="Build your Fund Credit by contributing to Family funds you belong to."
        actions={
          !showForm ? (
            <Button onClick={() => setShowForm(true)}>
              <Plus size={16} /> Report a contribution
            </Button>
          ) : undefined
        }
      />

      {loadingSummary && (
        <div className="kpi-grid">
          <SkeletonCard />
          <SkeletonCard />
        </div>
      )}

      {summaries && summaries.length > 0 && (
        <div className="kpi-grid">
          <KpiCard
            label="Fund Credit"
            value={<MoneyDisplay amount={totalFundCredit} />}
            hint="Confirmed contributions held as your continuing stake"
            icon={<Wallet size={20} />}
            tone="success"
          />
          {totalEntitlement > 0 && (
            <KpiCard
              label="Borrowing entitlement"
              value={<MoneyDisplay amount={totalEntitlement} />}
              hint="Fund Credit × multiplier"
              icon={<BadgeCheck size={20} />}
              tone="info"
            />
          )}
        </div>
      )}

      {showForm && (
        <div style={{ marginBottom: 16 }}>
          <ReportContributionForm onDone={() => setShowForm(false)} />
        </div>
      )}

      {isLoading && <SkeletonCard />}
      {error && <p className="error-text">{(error as Error).message}</p>}

      {contributions && contributions.length > 0 ? (
        <Card className="table-card">
          <div className="card-title">
            <h2>Contribution history</h2>
          </div>
          <table className="data-table">
            <thead>
              <tr>
                <th>Fund</th>
                <th>Amount</th>
                <th>Status</th>
                <th>Reference</th>
                <th>Reported</th>
              </tr>
            </thead>
            <tbody>
              {contributions.map((c) => (
                <tr key={c.id}>
                  <td>
                    <strong>{c.fundName}</strong>
                  </td>
                  <td>
                    <MoneyDisplay amount={c.amount} />
                  </td>
                  <td>
                    <StatusBadge status={c.status} withDot />
                  </td>
                  <td className="cell-secondary">{c.reference ?? '—'}</td>
                  <td className="cell-secondary">
                    {new Date(c.reportedAtUtc).toLocaleDateString()}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      ) : (
        !isLoading &&
        !error &&
        !showForm && (
          <Card>
            <EmptyState
              icon={<PiggyBank size={24} />}
              title="No contributions yet"
              description="Report a contribution to start building your Fund Credit."
              action={
                <Button onClick={() => setShowForm(true)}>
                  <Plus size={16} /> Report a contribution
                </Button>
              }
            />
          </Card>
        )
      )}
    </div>
  )
}
