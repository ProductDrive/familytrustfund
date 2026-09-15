import { useState } from 'react'
import { Landmark, ChevronDown, ChevronUp, Plus, Sparkles } from 'lucide-react'
import {
  useCreateFund,
  useFunds,
  useFundTransitionStatus,
  useTransitionFund,
  type Fund,
  type FundType,
  type FundTransitionStatus,
} from './fundApi'
import { useFundMembers, useUpdateHowItWorks } from '../membership/membershipApi'
import { useLendingCapacity } from '../loans/loanApi'
import {
  useCapitalTransactions,
  useFundCapitalSummary,
} from '../payments/paymentApi'
import { PageHeader } from '../../components/ui/PageHeader'
import { Card } from '../../components/ui/Card'
import { Badge, StatusBadge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { AmountInput } from '../../components/ui/AmountInput'
import { Avatar } from '../../components/ui/Avatar'
import { Modal } from '../../components/ui/Modal'
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
          <AmountInput
            id="fund-capital"
            value={committedCapital}
            onChange={setCommittedCapital}
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

function FamilyCapitalPanel({
  status,
  isLoading,
  onReview,
}: {
  status?: FundTransitionStatus
  isLoading: boolean
  onReview: () => void
}) {
  return (
    <section
      className={status?.isEligible ? 'transition-alert' : 'transition-alert transition-alert--progress'}
      aria-label="Family Capital status"
    >
      <h3>{status?.isEligible ? 'Family Capital Available' : 'Family Capital progress'}</h3>
      <p>
        {status?.isEligible
          ? 'Your Family Contributions have reached your committed capital. This fund is eligible to transition to Family Capital — it will not happen automatically.'
          : 'Family Contributions stay separate from your committed lending capital until this fund transitions.'}
      </p>

      {isLoading || !status ? (
        <p className="cell-secondary">Checking contributions…</p>
      ) : (
        <>
          <dl className="transition-metrics">
            <div>
              <dt>Family Contributions</dt>
              <dd>
                <MoneyDisplay amount={status.totalFamilyContributions} />
              </dd>
            </div>
            <div>
              <dt>Committed Capital</dt>
              <dd>
                <MoneyDisplay amount={status.committedCapital} />
              </dd>
            </div>
            {!status.isEligible && (
              <div>
                <dt>Remaining to threshold</dt>
                <dd>
                  <MoneyDisplay amount={status.remainingToThreshold} />
                </dd>
              </div>
            )}
          </dl>
          {status.isEligible && (
            <Button type="button" onClick={onReview}>
              <Sparkles size={16} /> Review Transition
            </Button>
          )}
        </>
      )}
    </section>
  )
}

function CapitalFundsPanel({ fund }: { fund: Fund }) {
  const [showHistory, setShowHistory] = useState(false)
  const summary = useFundCapitalSummary(fund.id)
  const history = useCapitalTransactions(showHistory ? fund.id : null)

  return (
    <section className="capital-panel" aria-label="Payments for disbursements">
      <div className="capital-header">
        <div>
          <h3>Disbursement payments</h3>
          <p className="muted">
            You pay for each loan when it is disbursed. The gross amount
            (approved amount plus provider fee) is charged at checkout and the
            member receives the net.
          </p>
        </div>
      </div>

      {summary.isLoading ? (
        <SkeletonCard />
      ) : summary.error || !summary.data ? (
        <p className="error-text">
          {(summary.error as Error).message ?? 'Disbursement payments unavailable.'}
        </p>
      ) : (
        <dl className="transition-metrics">
          <div>
            <dt>Total paid for disbursements</dt>
            <dd>
              <MoneyDisplay amount={summary.data.totalFunded} />
            </dd>
          </div>
          <div>
            <dt>Payments recorded</dt>
            <dd>{summary.data.transactionCount}</dd>
          </div>
        </dl>
      )}

      <Button
        type="button"
        variant="ghost"
        size="sm"
        onClick={() => setShowHistory((s) => !s)}
        aria-expanded={showHistory}
      >
        {showHistory ? <ChevronUp size={15} /> : <ChevronDown size={15} />}
        {showHistory ? 'Hide payment history' : 'View payment history'}
      </Button>

      {showHistory && (
        <div>
          {history.isLoading && <SkeletonCard />}
          {history.error && (
            <p className="error-text">{(history.error as Error).message}</p>
          )}
          {(history.data?.items ?? []).length === 0 && !history.isLoading && (
            <p className="muted">No disbursement payments yet.</p>
          )}
          {(history.data?.items ?? []).map((tx) => (
            <div className="member-row" key={tx.id}>
              <div style={{ minWidth: 0, flex: 1 }}>
                <div className="member-name">
                  <MoneyDisplay amount={tx.amountGross} />
                </div>
                <div className="member-email">{tx.providerReference}</div>
              </div>
              <StatusBadge status={tx.status} />
            </div>
          ))}
        </div>
      )}
    </section>
  )
}

function FundCard({ fund }: { fund: Fund }) {
  const [showMembers, setShowMembers] = useState(false)
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [howItWorks, setHowItWorks] = useState(fund.howItWorks ?? '')
  const members = useFundMembers(fund.id)
  const updateHowItWorks = useUpdateHowItWorks(fund.id)
  const capacity = useLendingCapacity(fund.id)
  const transition = useFundTransitionStatus(
    fund.type === 'Family' && fund.status === 'Active' ? fund.id : null,
  )
  const transitionFund = useTransitionFund()
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

      {isFamily && fund.status === 'Active' && (
        <FamilyCapitalPanel
          status={transition.data}
          isLoading={transition.isLoading}
          onReview={() => setConfirmOpen(true)}
        />
      )}

      <ul className="property-list">
        <li>
          <span className="property-label">Committed capital</span>
          <MoneyDisplay amount={fund.committedCapital} />
        </li>
        <li>
          <span className="property-label">Available lending capacity</span>
          {capacity.isLoading ? (
            <span className="cell-secondary">…</span>
          ) : (
            <MoneyDisplay amount={capacity.data?.availableLendingCapacity} />
          )}
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

      {fund.status === 'Active' && (
        <CapitalFundsPanel fund={fund} />
      )}

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

      <Modal
        open={confirmOpen}
        onClose={() => setConfirmOpen(false)}
        titleId="transition-modal-title"
        ariaLabel="Review Family Capital transition"
      >
        <h2 id="transition-modal-title">Review Family Capital transition</h2>
        <p className="muted">
          {fund.name} is eligible because its Family Contributions have reached your
          committed capital. Confirm before this fund changes how future loans are funded.
        </p>

        <dl className="transition-metrics">
          <div>
            <dt>Family Contributions</dt>
            <dd>
              <MoneyDisplay amount={transition.data?.totalFamilyContributions} />
            </dd>
          </div>
          <div>
            <dt>Committed Capital</dt>
            <dd>
              <MoneyDisplay amount={transition.data?.committedCapital} />
            </dd>
          </div>
        </dl>

        <ul className="checklist">
          <li>Transition is manual and will never happen automatically.</li>
          <li>It is a one-off action and cannot be undone.</li>
          <li>Existing Guarantor-funded loans are unchanged.</li>
          <li>Future loans will be funded from Family Capital.</li>
          <li>Another Guarantor-funded family group requires a new fund profile.</li>
        </ul>

        {transitionFund.isError && (
          <p className="form-error">{(transitionFund.error as Error).message}</p>
        )}

        <div className="modal-actions">
          <Button
            type="button"
            variant="secondary"
            onClick={() => setConfirmOpen(false)}
            disabled={transitionFund.isPending}
          >
            Cancel
          </Button>
          <Button
            type="button"
            onClick={() =>
              transitionFund.mutate(fund.id, {
                onSuccess: () => setConfirmOpen(false),
              })
            }
            disabled={transitionFund.isPending}
          >
            {transitionFund.isPending ? 'Transitioning…' : 'Confirm transition'}
          </Button>
        </div>
      </Modal>
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