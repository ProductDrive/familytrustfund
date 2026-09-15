import { Users } from 'lucide-react'
import { useAdminMemberships } from '../membership/membershipApi'
import { PageHeader } from '../../components/ui/PageHeader'
import { Card } from '../../components/ui/Card'
import { StatusBadge } from '../../components/ui/Badge'
import { EmptyState, SkeletonCard } from '../../components/ui/State'
import { Avatar } from '../../components/ui/Avatar'
import { formatShortDate } from '../../lib/formatDate'

export function AdminMembershipsPage() {
  const { data: memberships, isLoading, error } = useAdminMemberships()

  return (
    <div>
      <PageHeader
        title="Memberships"
        subtitle="Every membership across all funds on the platform."
      />

      {isLoading && <SkeletonCard />}
      {error && <p className="error-text">{(error as Error).message}</p>}

      {memberships && memberships.length > 0 && (
        <Card className="table-card">
          <table className="data-table">
            <thead>
              <tr>
                <th>Member</th>
                <th>Email</th>
                <th>Fund</th>
                <th>Status</th>
                <th>Joined</th>
              </tr>
            </thead>
            <tbody>
              {memberships.map((m) => (
                <tr key={m.membershipId}>
                  <td>
                    <span style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                      <Avatar name={m.displayName} size="sm" />
                      <strong>{m.displayName}</strong>
                    </span>
                  </td>
                  <td className="cell-secondary">{m.email}</td>
                  <td>{m.fundName}</td>
                  <td>
                    <StatusBadge status={m.status} withDot />
                  </td>
                  <td className="cell-secondary">
                    {formatShortDate(m.joinedAtUtc)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      )}

      {memberships && memberships.length === 0 && !isLoading && (
        <Card>
          <EmptyState
            icon={<Users size={24} />}
            title="No memberships yet"
            description="Once members join funds using a Guarantor code, they will appear here."
          />
        </Card>
      )}
    </div>
  )
}