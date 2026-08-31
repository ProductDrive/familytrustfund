import { ShieldAlert } from 'lucide-react'
import { Card } from '../components/ui/Card'
import { EmptyState } from '../components/ui/State'
import { Button } from '../components/ui/Button'

export function NotAuthorizedPage() {
  return (
    <Card>
      <EmptyState
        icon={<ShieldAlert size={24} />}
        title="Not authorized"
        description="Your account does not have permission to view this page."
        action={
          <Button variant="secondary" onClick={() => history.back()}>
            Go back
          </Button>
        }
      />
    </Card>
  )
}