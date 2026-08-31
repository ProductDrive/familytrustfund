import { useState } from 'react'
import { Landmark, ChevronDown, ChevronUp, Plus } from 'lucide-react'
import {
  useCreateFund,
  useFunds,
  type Fund,
  type FundType,
} from './fundApi'
import { useFundMembers, useUpdateHowItWorks } from '../membership/membershipApi'
import { PageHeader } from '../../components/ui/PageHeader'
import { Card } from '../../components/ui/Card'
import { Badge, StatusBadge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { Avatar } from '../../components/ui/Avatar'
import { EmptyState, SkeletonCard } from '../../components/ui/State'
import { MoneyDisplay } from '../../lib/money'

function CreateFundForm() {
  const createFund = useCreateFund()
  const [name, setName] = useState('')
  const [type, setType] = useState<FundType>('Family')
  const [committedCapital, setCommittedCapital] = useState('')
  const [interestRate, setInterestRate] = useState('')

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    if (!name.trim() || !committedCapital) return
    if (type === 'External' && !interestRate) return
    createFund.mutate(
      {
        name: name.trim(),
        type,
        committedCapital: Number(committedCapital),
        interestRate: type === 'External' ? Number(interestRate) : null,
      },
      {
        onSuccess: () => {
          setName('')
          setCommittedCapital('')
          setInterestRate('')
        },
      },
    )
  }

  const isFamily = type === 'Family'

  return (
    <Card title="Create a fund">
      <form onSubmit={handleSubmit}>
        <div className="field">
          <label htmlFor="fund-name">Fund name</label>
          <input
            id="fund-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Adeyemi Family Pool"
            required
          />
        </div>
        <div className="field">
          <label htmlFor="fund-type">Fund type</label>
          <select
            id="fund-type"
            value={type}
            onChange={(e) => setType(e.target.value as FundType)}
          >
            <option value="Family">Family</option>
            <option value="External">External</option>
          </select>
          <p className="field-hint">
            {isFamily
              ? 'No interest. Members contribute, their stake is their Fund Credit.'
              : 'You set the interest rate and commit lending capital.'}
          </p>
        </div>
        <div className="field">
          <label htmlFor="fund-capital">Committed capital (NGN)</label>
          <input
            id="fund-capital"
            type="number"
            min="0"
            step="0.01"
            value={committedCapital}
            onChange={(e) => setCommittedCapital(e.target.value)}
            placeholder="e.g. 5,000,000"
            required
          />
        </div>
        {!isFamily && (
          <div className="field">
            <label htmlFor="fund-interest">Interest rate (% per loan)</label>
            <input
              id="fund-interest"
              type="number"
              min="0"
              step="0.01"
              value={interestRate}
              onChange={(e) => setInterestRate(e.target.value)}
              placeholder="e.g. 5"
              required
            />
          </div>
        )}
        {createFund.isError && (
          <p className="form-error">{(createFund.error as Error).message}</p>
        )}
        <Button type="submit" disabled={createFund.isPending}>
          {createFund.isPending ? 'Creating…' : 'Create fund'}
        </Button>
      </form>
    </Card>
  )
}

function FundCard({ fund }: { fund: Fund }) {
  const [showMembers, setShowMembers] = useState(false)
  const [howItWorks, setHowItWorks] = useState(fund.howItWorks ?? '')
  const members = useFundMembers(fund.id)
  const updateHowItWorks = useUpdateHowItWorks(fund.id)
  const isFamily = fund.type === 'Family'

  return (
    <Card>
      <div className="card-title">
        <h3>{fund.name}</h3>
        <StatusBadge status={fund.status} withDot />
      </div>

      <div style={{ display: 'flex', gap: 8, marginBottom: 12, flexWrap: 'wrap' }}>
        <Badge tone={isFamily ? 'info' : 'neutral'}>{fund.type}</Badge>
        <Badge tone="neutral">Code: {fund.joinCode}</Badge>
      </div>

      <ul className="property-list">
        <li>
          <span className="property-label">Committed capital</span>
          <MoneyDisplay amount={fund.committedCapital} />
        </li>
        {isFamily ? (
          <li>
            <span className="property-label">Contribution multiplier</span>
            <span className="property-value">×{fund.contributionMultiplier}</span>
          </li>
        ) : (
          fund.interestRate != null && (
            <li>
              <span className="property-label">Interest rate</span>
              <span className="property-value">{fund.interestRate}% per loan</span>
            </li>
          )
        )}
      </ul>

      <form
        onSubmit={(e) => {
          e.preventDefault()
          updateHowItWorks.mutate(howItWorks.trim() || null)
        }}
      >
        <div className="field">
          <label htmlFor={`how-it-works-${fund.id}`}>
            How It Works (shown to members)
          </label>
          <textarea
            id={`how-it-works-${fund.id}`}
            value={howItWorks}
            onChange={(e) => setHowItWorks(e.target.value)}
            maxLength={4000}
            rows={3}
            placeholder="Describe what members should expect from this fund."
          />
        </div>
        <Button
          type="submit"
          variant="secondary"
          size="sm"
          disabled={updateHowItWorks.isPending}
        >
          {updateHowItWorks.isPending ? 'Saving…' : 'Save How It Works'}
        </Button>
      </form>

      <div style={{ marginTop: 12 }}>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={() => setShowMembers((s) => !s)}
        >
          {showMembers ? <ChevronUp size={15} /> : <ChevronDown size={15} />}
          {showMembers ? 'Hide members' : 'View members'}
        </Button>

        {showMembers && (
          <div className="member-list">
            {members.isLoading && <SkeletonCard />}
            {members.error && (
              <p className="error-text">{(members.error as Error).message}</p>
            )}
            {(members.data ?? []).map((member) => (
              <div className="member-row" key={member.memberId}>
                <Avatar name={member.displayName} size="sm" />
                <div style={{ minWidth: 0, flex: 1 }}>
                  <div className="member-name">{member.displayName}</div>
                  <div className="member-email">{member.email}</div>
                </div>
                <StatusBadge status={member.status} />
              </div>
            ))}
            {members.data && members.data.length === 0 && (
              <p className="muted">No members have joined yet.</p>
            )}
          </div>
        )}
      </div>
    </Card>
  )
}

export function FundsPage() {
  const { data: funds, isLoading, error } = useFunds()
  const [showCreate, setShowCreate] = useState(funds?.length === 0)

  return (
    <div>
      <PageHeader
        title="Fund Management"
        subtitle="Create fund profiles, set terms and manage your members."
        actions={
          !isLoading && (
            <Button
              variant={showCreate ? 'secondary' : 'primary'}
              onClick={() => setShowCreate((s) => !s)}
            >
              <Plus size={16} />
              {showCreate ? 'Close form' : 'New fund'}
            </Button>
          )
        }
      />

      {showCreate && <CreateFundForm />}

      {isLoading && (
        <div className="grid grid--funds">
          <SkeletonCard />
          <SkeletonCard />
        </div>
      )}
      {error && <p className="error-text">{(error as Error).message}</p>}

      {funds && funds.length > 0 && (
        <div className="grid grid--funds">
          {funds.map((fund) => (
            <FundCard key={fund.id} fund={fund} />
          ))}
        </div>
      )}

      {funds && funds.length === 0 && !isLoading && !showCreate && (
        <Card>
          <EmptyState
            icon={<Landmark size={24} />}
            title="No funds yet"
            description="Create your first fund profile to start lending to your trusted circle."
            action={
              <Button onClick={() => setShowCreate(true)}>
                <Plus size={16} /> Create fund
              </Button>
            }
          />
        </Card>
      )}
    </div>
  )
}