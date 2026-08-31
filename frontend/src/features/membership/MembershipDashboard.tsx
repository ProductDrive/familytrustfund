import { useState } from 'react'
import { HandCoins, KeyRound, Wallet, BadgeCheck, PiggyBank } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { useJoinFund, useMyMemberships, type Membership } from './membershipApi'
import { useLendingCapacity } from '../loans/loanApi'
import { PageHeader } from '../../components/ui/PageHeader'
import { Card } from '../../components/ui/Card'
import { Badge, StatusBadge } from '../../components/ui/Badge'
import { KpiCard } from '../../components/ui/KpiCard'
import { Button } from '../../components/ui/Button'
import { EmptyState, SkeletonCard } from '../../components/ui/State'
import { MoneyDisplay } from '../../lib/money'

const JoinFundForm = () => {
  const joinFund = useJoinFund()
  const [joinCode, setJoinCode] = useState('')

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    const code = joinCode.trim()
    if (!code) return
    joinFund.mutate(code, { onSuccess: () => setJoinCode('') })
  }

  return (
    <Card title="Join a fund">
      <p className="muted">
        Ask your Guarantor for the fund&apos;s join code to join a closed group.
      </p>
      <form onSubmit={handleSubmit}>
        <div className="field">
          <label htmlFor="join-code">Join code</label>
          <div style={{ display: 'flex', gap: 8 }}>
            <input
              id="join-code"
              value={joinCode}
              onChange={(e) => setJoinCode(e.target.value)}
              placeholder="e.g. ABCD1234"
              required
            />
            <Button type="submit" disabled={joinFund.isPending}>
              {joinFund.isPending ? 'Joining…' : 'Join fund'}
            </Button>
          </div>
        </div>
        {joinFund.isError && (
          <p className="form-error">{(joinFund.error as Error).message}</p>
        )}
      </form>
    </Card>
  )
}

function FundCard({ membership }: { membership: Membership }) {
  const navigate = useNavigate()
  const { data: capacity } = useLendingCapacity(membership.fundId)

  return (
    <Card>
      <div className="card-title">
        <h3>{membership.fundName}</h3>
        <StatusBadge status={membership.status} withDot />
      </div>

      <ul className="property-list">
        <li>
          <span className="property-label">Fund type</span>
          <Badge tone={membership.fundType === 'Family' ? 'info' : 'neutral'}>
            {membership.fundType}
          </Badge>
        </li>
        <li>
          <span className="property-label">Join code</span>
          <code>{membership.joinCode}</code>
        </li>
        <li>
          <span className="property-label">Fund status</span>
          <span className="property-value">{membership.fundStatus}</span>
        </li>
        {membership.fundType === 'Family' && (
          <>
            <li>
              <span className="property-label">Fund Credit</span>
              <MoneyDisplay amount={capacity?.fundCredit ?? 0} />
            </li>
            <li>
              <span className="property-label">Borrowing entitlement</span>
              <MoneyDisplay amount={capacity?.familyBorrowingEntitlement ?? 0} />
            </li>
          </>
        )}
        {membership.fundType === 'External' && membership.interestRate != null && (
          <li>
            <span className="property-label">Interest rate</span>
            <span className="property-value">{membership.interestRate}%</span>
          </li>
        )}
      </ul>

      {membership.howItWorks && (
        <p className="muted" style={{ marginTop: 12 }}>
          <strong>How it works:</strong> {membership.howItWorks}
        </p>
      )}

      <div style={{ marginTop: 16, display: 'flex', gap: 8, flexWrap: 'wrap' }}>
        {membership.fundType === 'Family' && (
          <Button onClick={() => navigate('/contributions')}>
            <PiggyBank size={16} /> Contribute
          </Button>
        )}
        <Button variant="secondary" onClick={() => navigate('/loans')}>
          <HandCoins size={16} /> Request a loan
        </Button>
      </div>
    </Card>
  )
}

export function MembershipDashboard() {
  const navigate = useNavigate()
  const { data: memberships, isLoading, error } = useMyMemberships()
  const [showAllFunds, setShowAllFunds] = useState(false)

  const activeCount = memberships?.filter((m) => m.status === 'Active').length ?? 0
  const pausedCount = (memberships?.length ?? 0) - activeCount
  const activeMemberships = memberships?.filter((m) => m.status === 'Active') ?? []

  return (
    <div>
      <PageHeader
        title="Dashboard"
        subtitle="Your memberships, Fund Credit and loan overview."
        actions={
          activeMemberships.length > 0 ? (
            <Button onClick={() => navigate('/loans')}>
              <HandCoins size={16} /> Request a loan
            </Button>
          ) : undefined
        }
      />

      {isLoading && (
        <div className="kpi-grid">
          <SkeletonCard />
          <SkeletonCard />
          <SkeletonCard />
        </div>
      )}
      {error && <p className="error-text">{(error as Error).message}</p>}

      {activeMemberships.length > 0 && (
        <div className="kpi-grid">
          <KpiCard
            label="Funds joined"
            value={memberships?.length ?? 0}
            icon={<Wallet size={20} />}
            tone="info"
          />
          <KpiCard
            label="Active memberships"
            value={activeCount}
            icon={<BadgeCheck size={20} />}
            tone="success"
          />
        </div>
      )}

      {activeMemberships.length > 0 && (
        <div className="grid grid--funds">
          {activeMemberships.map((m) => (
            <FundCard key={m.fundId} membership={m} />
          ))}
        </div>
      )}

      {pausedCount > 0 && (
        <Card>
          <div className="card-title">
            <h3>Other memberships</h3>
            <Button variant="ghost" size="sm" onClick={() => setShowAllFunds((v) => !v)}>
              {showAllFunds ? 'Hide' : 'Show'} ({pausedCount})
            </Button>
          </div>
          {showAllFunds && (
            <div className="grid grid--funds">
              {memberships
                ?.filter((m) => m.status !== 'Active')
                .map((m) => <FundCard key={m.fundId} membership={m} />)}
            </div>
          )}
        </Card>
      )}

      {memberships && memberships.length === 0 && !isLoading && (
        <Card>
          <EmptyState
            icon={<KeyRound size={24} />}
            title="You haven't joined a fund yet"
            description="Enter your Guarantor's join code to start contributing and accessing lending within a trusted group."
          />
          <JoinFundForm />
        </Card>
      )}

      {memberships && memberships.length > 0 && (
        <div id="join-fund" style={{ marginTop: 16 }}>
          <JoinFundForm />
        </div>
      )}
    </div>
  )
}
