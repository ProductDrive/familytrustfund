import { useState } from 'react'
import { Landmark, ChevronDown, ChevronUp, Plus, Sparkles } from 'lucide-react'
import {
  useCreateFund,
  useFunds,
  useFundTransitionStatus,
  useTransitionFund,
  useUpdateRepaymentAccount,
  useFundGuarantors,
  useInviteGuarantor,
  useRevokeGuarantor,
  useLeaveFund,
  useUpdateGuarantorCommitment,
  type Fund,
  type FundGuarantor,
  type FundGuarantorMode,
  type FundType,
  type FundTransitionStatus,
} from './fundApi'
import { useFundMembers, useUpdateHowItWorks } from '../membership/membershipApi'
import { useLendingCapacity } from '../loans/loanApi'
import { useCurrentUser } from '../auth/authApi'
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
  const [guarantorMode, setGuarantorMode] = useState<FundGuarantorMode>('Single')
  const [committedCapital, setCommittedCapital] = useState('')
  const [interestRate, setInterestRate] = useState('')
  const [minTerm, setMinTerm] = useState('1')
  const [maxTerm, setMaxTerm] = useState('60')

  const termRangeInvalid =
    !minTerm ||
    !maxTerm ||
    Number(minTerm) < 1 ||
    Number(maxTerm) < Number(minTerm) ||
    Number(maxTerm) > 120

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    if (!name.trim() || !committedCapital) return
    if (type === 'External' && !interestRate) return
    if (termRangeInvalid) return
    createFund.mutate(
      {
        name: name.trim(),
        type,
        guarantorMode,
        committedCapital: Number(committedCapital),
        interestRate: type === 'External' ? Number(interestRate) : null,
        minLoanTermMonths: Number(minTerm),
        maxLoanTermMonths: Number(maxTerm),
      },
      {
        onSuccess: () => {
          setName('')
          setGuarantorMode('Single')
          setCommittedCapital('')
          setInterestRate('')
          setMinTerm('1')
          setMaxTerm('60')
        },
      },
    )
  }

  const isFamily = type === 'Family'
  const isGroup = guarantorMode === 'Group'

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
          <label htmlFor="fund-guarantor-mode">Who operates this fund?</label>
          <select
            id="fund-guarantor-mode"
            value={guarantorMode}
            onChange={(e) => setGuarantorMode(e.target.value as FundGuarantorMode)}
          >
            <option value="Single">Just me (single Guarantor)</option>
            <option value="Group">A group of Guarantors</option>
          </select>
          <p className="field-hint">
            {isGroup
              ? 'You become the Fund Originator. You can invite other approved Guarantors to operate the fund jointly. This cannot be changed later.'
              : 'Only you operate and approve loans for this fund. You can create a separate group fund later.'}
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
          <p className="field-hint">
            A planning figure, not money deposited. {isGroup
              ? 'Each Guarantor commits their own share after joining.'
              : ''}
          </p>
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
        <div
          className="grid"
          style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))', gap: 12 }}
        >
          <div className="field">
            <label htmlFor="fund-min-term">Minimum loan term (months)</label>
            <input
              id="fund-min-term"
              type="number"
              min="1"
              max="120"
              value={minTerm}
              onChange={(e) => setMinTerm(e.target.value)}
              required
            />
          </div>
          <div className="field">
            <label htmlFor="fund-max-term">Maximum loan term (months)</label>
            <input
              id="fund-max-term"
              type="number"
              min="1"
              max="120"
              value={maxTerm}
              onChange={(e) => setMaxTerm(e.target.value)}
              required
            />
          </div>
        </div>
        <p className="field-hint">
          Members can request terms within this range. Defaults are 1–60 months;
          the maximum is 120.
        </p>
        {termRangeInvalid && (
          <p className="form-error">
            Enter a valid term range: minimum at least 1, maximum no more than 120,
            and maximum not less than minimum.
          </p>
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

function FundGuarantorsPanel({ fund }: { fund: Fund }) {
  const { data: user } = useCurrentUser()
  const { data: guarantors, isLoading, error } = useFundGuarantors(fund.id)
  const invite = useInviteGuarantor()
  const revoke = useRevokeGuarantor()
  const leave = useLeaveFund()
  const updateCommitment = useUpdateGuarantorCommitment()

  const [email, setEmail] = useState('')
  const [inviteCommitment, setInviteCommitment] = useState('')
  const [myCommitment, setMyCommitment] = useState('')

  const isOriginator = user?.id === fund.guarantorId
  const myMembership = guarantors?.find((g) => g.guarantorId === user?.id)
  const activeCount = guarantors?.filter((g) => g.status === 'Active').length ?? 0
  const commitmentValue =
    myCommitment !== ''
      ? myCommitment
      : myMembership
        ? String(myMembership.committedCapital)
        : ''

  function handleInvite(e: React.FormEvent) {
    e.preventDefault()
    if (!email.trim() || !inviteCommitment) return
    invite.mutate(
      {
        fundId: fund.id,
        email: email.trim(),
        committedCapital: Number(inviteCommitment),
      },
      {
        onSuccess: () => {
          setEmail('')
          setInviteCommitment('')
        },
      },
    )
  }

  function handleCommitment(e: React.FormEvent) {
    e.preventDefault()
    if (!commitmentValue) return
    updateCommitment.mutate(
      { fundId: fund.id, committedCapital: Number(commitmentValue) },
      { onSuccess: () => setMyCommitment('') },
    )
  }

  return (
    <section className="capital-panel" aria-label="Group Guarantors">
      <div className="capital-header">
        <div>
          <h3>Group Guarantors</h3>
          <p className="muted">
            Every active Guarantor must approve a loan before it can be disbursed.
            Only the Fund Originator can invite, remove or disburse.
          </p>
        </div>
        <Badge tone="info">{activeCount} active</Badge>
      </div>

      {isLoading && <SkeletonCard />}
      {error && <p className="error-text">{(error as Error).message}</p>}

      {guarantors && (
        <div className="member-list">
          {guarantors.map((g) => (
            <GuarantorRow
              key={g.id}
              guarantor={g}
              isOriginator={isOriginator}
              canRevoke={
                isOriginator &&
                !g.isOriginator &&
                (g.status === 'Active' || g.status === 'Invited')
              }
              onRevoke={() => {
                if (
                  window.confirm(
                    `Remove ${g.guarantorDisplayName || g.guarantorEmail} from ${fund.name}?`,
                  )
                ) {
                  revoke.mutate({ fundId: fund.id, guarantorId: g.guarantorId })
                }
              }}
              revokePending={revoke.isPending}
            />
          ))}
        </div>
      )}

      {activeCount < 2 && (
        <p className="field-hint">
          At least two active Guarantors are required before this fund can disburse
          a loan.
        </p>
      )}

      {revoke.isError && (
        <p className="form-error">{(revoke.error as Error).message}</p>
      )}

      {isOriginator && (
        <form onSubmit={handleInvite} style={{ marginTop: 16 }}>
          <div className="card-title" style={{ marginBottom: 4 }}>
            <h4 style={{ margin: 0 }}>Invite a Guarantor</h4>
          </div>
          <p className="muted" style={{ marginTop: 0 }}>
            They must already have an approved Guarantor account. Invitations expire
            after 14 days and do not add capital until accepted.
          </p>
          <div
            className="grid"
            style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 12 }}
          >
            <div className="field">
              <label htmlFor={`invite-email-${fund.id}`}>Guarantor email</label>
              <input
                id={`invite-email-${fund.id}`}
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="guarantor@example.com"
                required
              />
            </div>
            <div className="field">
              <label htmlFor={`invite-commitment-${fund.id}`}>
                Their committed capital (NGN)
              </label>
              <AmountInput
                id={`invite-commitment-${fund.id}`}
                value={inviteCommitment}
                onChange={setInviteCommitment}
                placeholder="e.g. 1,000,000"
              />
            </div>
          </div>
          {invite.isError && (
            <p className="form-error">{(invite.error as Error).message}</p>
          )}
          {invite.isSuccess && (
            <p className="muted">Invitation sent.</p>
          )}
          <Button type="submit" size="sm" disabled={invite.isPending}>
            {invite.isPending ? 'Sending…' : 'Send invitation'}
          </Button>
        </form>
      )}

      {myMembership?.status === 'Active' && (
        <form onSubmit={handleCommitment} style={{ marginTop: 16 }}>
          <div className="field">
            <label htmlFor={`my-commitment-${fund.id}`}>
              Your committed capital (NGN)
            </label>
            <AmountInput
              id={`my-commitment-${fund.id}`}
              value={commitmentValue}
              onChange={setMyCommitment}
            />
            <p className="field-hint">
              A planning figure only. You cannot reduce it below the capital already
              disbursed.
            </p>
          </div>
          {updateCommitment.isError && (
            <p className="form-error">{(updateCommitment.error as Error).message}</p>
          )}
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            <Button type="submit" variant="secondary" size="sm" disabled={updateCommitment.isPending}>
              {updateCommitment.isPending ? 'Saving…' : 'Update my commitment'}
            </Button>
            {!isOriginator && (
              <Button
                type="button"
                variant="ghost"
                size="sm"
                disabled={leave.isPending}
                onClick={() => {
                  if (
                    window.confirm(
                      `Leave ${fund.name}? You will no longer be able to approve loans or disburse for this fund.`,
                    )
                  ) {
                    leave.mutate(fund.id)
                  }
                }}
              >
                {leave.isPending ? 'Leaving…' : 'Leave fund'}
              </Button>
            )}
          </div>
          {leave.isError && (
            <p className="form-error">{(leave.error as Error).message}</p>
          )}
        </form>
      )}
    </section>
  )
}

function GuarantorRow({
  guarantor,
  isOriginator,
  canRevoke,
  onRevoke,
  revokePending,
}: {
  guarantor: FundGuarantor
  isOriginator: boolean
  canRevoke: boolean
  onRevoke: () => void
  revokePending: boolean
}) {
  const expired = guarantor.status === 'Invited' && guarantor.isExpired
  return (
    <div className="member-row">
      <Avatar name={guarantor.guarantorDisplayName || guarantor.guarantorEmail} size="sm" />
      <div style={{ minWidth: 0, flex: 1 }}>
        <div className="member-name">
          {guarantor.guarantorDisplayName || guarantor.guarantorEmail}
          {isOriginator && <span className="muted"> · You</span>}
        </div>
        <div className="member-email">{guarantor.guarantorEmail}</div>
        <div className="member-email">
          Committed: <MoneyDisplay amount={guarantor.committedCapital} />
        </div>
      </div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
        {guarantor.isOriginator && <Badge tone="info">Originator</Badge>}
        <StatusBadge status={expired ? 'Expired' : guarantor.status} />
        {canRevoke && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            disabled={revokePending}
            onClick={onRevoke}
          >
            Remove
          </Button>
        )}
      </div>
    </div>
  )
}

function FundCard({ fund }: { fund: Fund }) {
  const [showMembers, setShowMembers] = useState(false)
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [howItWorks, setHowItWorks] = useState(fund.howItWorks ?? '')
  const [repaymentBankName, setRepaymentBankName] = useState(fund.repaymentBankName ?? '')
  const [repaymentAccountNumber, setRepaymentAccountNumber] = useState(fund.repaymentAccountNumber ?? '')
  const [repaymentAccountName, setRepaymentAccountName] = useState(fund.repaymentAccountName ?? '')
  const members = useFundMembers(fund.id)
  const updateHowItWorks = useUpdateHowItWorks(fund.id)
  const updateRepaymentAccount = useUpdateRepaymentAccount()
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
        {fund.guarantorMode === 'Group' && <Badge tone="info">Group Guarantors</Badge>}
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
        <li>
          <span className="property-label">Loan term range</span>
          <span className="property-value">
            {fund.minLoanTermMonths}–{fund.maxLoanTermMonths} months
          </span>
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

      {fund.guarantorMode === 'Group' && <FundGuarantorsPanel fund={fund} />}

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

      <form
        onSubmit={(e) => {
          e.preventDefault()
          const bankName = repaymentBankName.trim() || null
          const accountNumber = repaymentAccountNumber.trim() || null
          const accountName = repaymentAccountName.trim() || null
          updateRepaymentAccount.mutate({
            fundId: fund.id,
            bankName,
            accountNumber,
            accountName,
          })
        }}
        style={{ marginTop: 16 }}
      >
        <div style={{ marginBottom: 8 }}>
          <div className="card-title" style={{ marginBottom: 4 }}>
            <h4 style={{ margin: 0 }}>Repayment account details</h4>
          </div>
          <p className="muted" style={{ margin: 0 }}>
            Shown to members so they know where to send repayments.
          </p>
        </div>
        <div className="grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: 12 }}>
          <div className="field">
            <label htmlFor={`repay-bank-${fund.id}`}>Bank name</label>
            <input
              id={`repay-bank-${fund.id}`}
              value={repaymentBankName}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setRepaymentBankName(e.target.value)}
            />
          </div>
          <div className="field">
            <label htmlFor={`repay-acctname-${fund.id}`}>Account name</label>
            <input
              id={`repay-acctname-${fund.id}`}
              value={repaymentAccountName}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setRepaymentAccountName(e.target.value)}
            />
          </div>
          <div className="field">
            <label htmlFor={`repay-acctno-${fund.id}`}>Account number</label>
            <input
              id={`repay-acctno-${fund.id}`}
              value={repaymentAccountNumber}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setRepaymentAccountNumber(e.target.value)}
            />
          </div>
        </div>
        {updateRepaymentAccount.isError && (
          <p className="form-error">{(updateRepaymentAccount.error as Error).message}</p>
        )}
        {updateRepaymentAccount.isSuccess && (
          <p className="muted">Repayment account details saved.</p>
        )}
        <div style={{ marginTop: 10 }}>
          <Button
            type="submit"
            variant="secondary"
            size="sm"
            disabled={updateRepaymentAccount.isPending}
          >
            {updateRepaymentAccount.isPending ? 'Saving…' : 'Save repayment account'}
          </Button>
        </div>
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