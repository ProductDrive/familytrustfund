import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  ArrowRight,
  BadgeCheck,
  HandCoins,
  Inbox,
  Landmark,
  PiggyBank,
  Wallet,
} from 'lucide-react'
import { useFunds, useFundTransitionStatus, type Fund } from '../funds/fundApi'
import {
  useGuarantorLendingCapacity,
  useGuarantorPendingLoans,
} from '../loans/loanApi'
import { useGuarantorPendingContributions } from '../contributions/contributionApi'
import { useGuarantorPendingRepayments } from '../repayments/repaymentApi'
import { PageHeader } from '../../components/ui/PageHeader'
import { Card } from '../../components/ui/Card'
import { KpiCard } from '../../components/ui/KpiCard'
import { Badge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { EmptyState, SkeletonCard } from '../../components/ui/State'
import { MoneyDisplay } from '../../lib/money'

interface PortfolioStat {
  committedCapital: number
  activeDisbursedLoans: number
  availableCapacity: number
  familyContributions: number
}

const emptyTotals: PortfolioStat = {
  committedCapital: 0,
  activeDisbursedLoans: 0,
  availableCapacity: 0,
  familyContributions: 0,
}

function FundStatCard({
  fund,
  onReport,
}: {
  fund: Fund
  onReport: (fundId: string, stat: PortfolioStat) => void
}) {
  const navigate = useNavigate()
  const { data: capacity } = useGuarantorLendingCapacity(fund.id)
  const { data: transition } = useFundTransitionStatus(
    fund.type === 'Family' ? fund.id : null,
  )

  useEffect(() => {
    if (!capacity) return
    onReport(fund.id, {
      committedCapital: capacity.committedCapital,
      activeDisbursedLoans: capacity.activeDisbursedLoans,
      availableCapacity: capacity.availableLendingCapacity,
      familyContributions:
        fund.type === 'Family' ? transition?.totalFamilyContributions ?? 0 : 0,
    })
  }, [capacity, transition?.totalFamilyContributions, fund.id, fund.type, onReport])

  const familyContributions =
    fund.type === 'Family' ? transition?.totalFamilyContributions ?? 0 : 0
  const transitionEligible =
    fund.type === 'Family' &&
    transition?.isTransitioned === false &&
    transition?.isEligible === true

  return (
    <Card>
      <div className="card-title">
        <h3>{fund.name}</h3>
        <Badge tone={fund.type === 'Family' ? 'info' : 'neutral'}>{fund.type}</Badge>
      </div>
      <p className="muted">
        Join code <code>{fund.joinCode}</code>
      </p>

      <ul className="property-list" style={{ marginTop: 8 }}>
        <li>
          <span className="property-label">Committed Capital</span>
          <MoneyDisplay amount={fund.committedCapital} />
        </li>
        <li>
          <span className="property-label">Active Disbursed Loans</span>
          <MoneyDisplay amount={capacity?.activeDisbursedLoans ?? 0} />
        </li>
        <li>
          <span className="property-label">Available Lending Capacity</span>
          <MoneyDisplay amount={capacity?.availableLendingCapacity ?? 0} />
        </li>
        {fund.type === 'Family' && (
          <li>
            <span className="property-label">Family Contributions</span>
            <MoneyDisplay amount={familyContributions} />
          </li>
        )}
      </ul>

      {transitionEligible && (
        <div className="transition-alert" style={{ marginTop: 12 }}>
          <h3>Family Capital Available</h3>
          <p>
            Your Family Contributions have reached your committed capital. This fund
            is eligible to transition to Family Capital — it will never happen
            automatically.
          </p>
        </div>
      )}

      <div style={{ marginTop: 16, display: 'flex', gap: 8, flexWrap: 'wrap' }}>
        <Button size="sm" onClick={() => navigate(`/guarantor/disburse?fund=${fund.id}`)}>
          <HandCoins size={14} /> Disburse
        </Button>
        <Button
          size="sm"
          variant="secondary"
          onClick={() => navigate('/funds')}
        >
          Manage fund
        </Button>
      </div>
    </Card>
  )
}

function PendingActionCard({
  to,
  icon,
  label,
  description,
  count,
}: {
  to: string
  icon: React.ReactNode
  label: string
  description: string
  count: number
}) {
  const navigate = useNavigate()
  return (
    <Card>
      <div className="card-title">
        <h3 style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          {icon} {label}
        </h3>
        <Badge tone="pending">{count}</Badge>
      </div>
      <p className="muted">{description}</p>
      <Button size="sm" variant="secondary" onClick={() => navigate(to)}>
        Review <ArrowRight size={14} />
      </Button>
    </Card>
  )
}

export function GuarantorDashboardPage() {
  const navigate = useNavigate()
  const { data: funds = [], isLoading: loadingFunds } = useFunds()
  const { data: pendingLoans = [] } = useGuarantorPendingLoans()
  const { data: pendingContributions = [] } = useGuarantorPendingContributions()
  const { data: pendingRepayments = [] } = useGuarantorPendingRepayments()
  const [stats, setStats] = useState<Record<string, PortfolioStat>>({})
  const reportStats = useCallback((fundId: string, stat: PortfolioStat) => {
    setStats((prev) => ({ ...prev, [fundId]: stat }))
  }, [])

  const totals = Object.values(stats).reduce(
    (acc, s) => ({
      committedCapital: acc.committedCapital + s.committedCapital,
      activeDisbursedLoans: acc.activeDisbursedLoans + s.activeDisbursedLoans,
      availableCapacity: acc.availableCapacity + s.availableCapacity,
      familyContributions: acc.familyContributions + s.familyContributions,
    }),
    emptyTotals,
  )

  const hasStats = Object.keys(stats).length > 0

  return (
    <div>
      <PageHeader
        title="Guarantor Dashboard"
        subtitle="Your committed capital, current lending capacity and pending decisions across your funds."
        actions={
          funds.length > 0 ? (
            <Button onClick={() => navigate('/guarantor/loans')}>
              <Inbox size={16} /> Review loan requests
            </Button>
          ) : undefined
        }
      />

      {loadingFunds && (
        <div className="kpi-grid">
          <SkeletonCard />
          <SkeletonCard />
          <SkeletonCard />
          <SkeletonCard />
        </div>
      )}

      {hasStats && (
        <div className="kpi-grid">
          <KpiCard
            label="Family Contributions"
            value={<MoneyDisplay amount={totals.familyContributions} />}
            hint="Member stakes, separate from your capital"
            icon={<PiggyBank size={20} />}
            tone="info"
          />
          <KpiCard
            label="Committed Capital"
            value={<MoneyDisplay amount={totals.committedCapital} />}
            hint="Capital you are willing to lend"
            icon={<Wallet size={20} />}
            tone="neutral"
          />
          <KpiCard
            label="Active Disbursed Loans"
            value={<MoneyDisplay amount={totals.activeDisbursedLoans} />}
            hint="Disbursed, not merely approved"
            icon={<HandCoins size={20} />}
            tone="warning"
          />
          <KpiCard
            label="Available Lending Capacity"
            value={<MoneyDisplay amount={totals.availableCapacity} />}
            hint="Committed capital − disbursed loans"
            icon={<Wallet size={20} />}
            tone="success"
          />
        </div>
      )}

      {funds.length > 0 && (
        <div className="grid grid--funds" style={{ marginTop: 24 }}>
          <PendingActionCard
            to="/guarantor/loans"
            icon={<Inbox size={16} />}
            label="Loan requests"
            description="Loans waiting for your approval."
            count={pendingLoans.length}
          />
          <PendingActionCard
            to="/guarantor/contributions"
            icon={<PiggyBank size={16} />}
            label="Contribution confirmations"
            description="Member contributions awaiting confirmation."
            count={pendingContributions.length}
          />
          <PendingActionCard
            to="/guarantor/repayments"
            icon={<BadgeCheck size={16} />}
            label="Repayment confirmations"
            description="Reported repayments awaiting confirmation."
            count={pendingRepayments.length}
          />
        </div>
      )}

      <h2 className="section-heading">Your funds</h2>

      {loadingFunds && <SkeletonCard />}

      {funds.length > 0 ? (
        <div className="grid grid--funds" style={{ marginTop: 16 }}>
          {funds.map((f) => (
            <FundStatCard key={f.id} fund={f} onReport={reportStats} />
          ))}
        </div>
      ) : (
        !loadingFunds && (
          <div style={{ marginTop: 12 }}>
            <Card>
              <EmptyState
                icon={<Landmark size={24} />}
                title="No funds yet"
                description="Create your first fund to commit capital and start inviting members."
                action={
                  <Button onClick={() => navigate('/funds')}>
                    <Landmark size={16} /> Create a fund
                  </Button>
                }
              />
            </Card>
          </div>
        )
      )}
    </div>
  )
}