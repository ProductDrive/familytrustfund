import { Check, Mail, X } from 'lucide-react'
import {
  useAcceptInvitation,
  useDeclineInvitation,
  useMyInvitations,
  type GuarantorInvitation,
} from '../funds/fundApi'
import { PageHeader } from '../../components/ui/PageHeader'
import { Card } from '../../components/ui/Card'
import { Badge, StatusBadge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { EmptyState, SkeletonCard } from '../../components/ui/State'
import { MoneyDisplay } from '../../lib/money'
import { formatShortDate } from '../../lib/formatDate'

function InvitationCard({ invitation }: { invitation: GuarantorInvitation }) {
  const accept = useAcceptInvitation()
  const decline = useDeclineInvitation()
  const expired = invitation.status === 'Invited' && invitation.isExpired
  const pending = invitation.status === 'Invited' && !expired

  return (
    <Card>
      <div className="card-title">
        <h3>{invitation.fundName}</h3>
        <StatusBadge status={expired ? 'Expired' : invitation.status} withDot />
      </div>

      <div style={{ display: 'flex', gap: 8, marginBottom: 12, flexWrap: 'wrap' }}>
        <Badge tone="info">{invitation.fundType}</Badge>
      </div>

      <ul className="property-list">
        <li>
          <span className="property-label">Invited by</span>
          <span className="property-value">
            {invitation.originatorDisplayName || invitation.originatorEmail}
          </span>
        </li>
        <li>
          <span className="property-label">Your committed capital</span>
          <MoneyDisplay amount={invitation.committedCapital} />
        </li>
        <li>
          <span className="property-label">Invited</span>
          <span className="property-value">
            {formatShortDate(invitation.invitedAtUtc)}
          </span>
        </li>
        {invitation.expiresAtUtc && (
          <li>
            <span className="property-label">Expires</span>
            <span className="property-value">
              {formatShortDate(invitation.expiresAtUtc)}
            </span>
          </li>
        )}
      </ul>

      {(accept.isError || decline.isError) && (
        <p className="form-error">
          {((accept.error ?? decline.error) as Error).message}
        </p>
      )}

      {pending && (
        <div style={{ display: 'flex', gap: 8, marginTop: 16, flexWrap: 'wrap' }}>
          <Button
            onClick={() => accept.mutate(invitation.membershipId)}
            disabled={accept.isPending || decline.isPending}
          >
            <Check size={16} /> {accept.isPending ? 'Accepting…' : 'Accept invitation'}
          </Button>
          <Button
            variant="ghost"
            onClick={() => decline.mutate(invitation.membershipId)}
            disabled={accept.isPending || decline.isPending}
          >
            <X size={16} /> {decline.isPending ? 'Declining…' : 'Decline'}
          </Button>
        </div>
      )}
    </Card>
  )
}

export function GuarantorInvitationsPage() {
  const { data: invitations, isLoading, error } = useMyInvitations()

  const pending = (invitations ?? []).filter(
    (i) => i.status === 'Invited' && !i.isExpired,
  )
  const history = (invitations ?? []).filter(
    (i) => i.status !== 'Invited' || i.isExpired,
  )

  return (
    <div>
      <PageHeader
        title="Invitations"
        subtitle="Group funds you have been invited to operate, and your existing group memberships."
      />

      {isLoading && <SkeletonCard />}
      {error && <p className="error-text">{(error as Error).message}</p>}

      {!isLoading && !error && pending.length === 0 && history.length === 0 && (
        <Card>
          <EmptyState
            icon={<Mail size={24} />}
            title="No invitations"
            description="When a Fund Originator invites your Guarantor account to a group fund, it will appear here."
          />
        </Card>
      )}

      {pending.length > 0 && (
        <>
          <h2 className="section-heading">Pending invitations</h2>
          <div className="grid">
            {pending.map((invitation) => (
              <InvitationCard key={invitation.membershipId} invitation={invitation} />
            ))}
          </div>
        </>
      )}

      {history.length > 0 && (
        <>
          <h2 className="section-heading" style={{ marginTop: 24 }}>
            Previous invitations and memberships
          </h2>
          <div className="grid">
            {history.map((invitation) => (
              <InvitationCard key={invitation.membershipId} invitation={invitation} />
            ))}
          </div>
        </>
      )}
    </div>
  )
}
