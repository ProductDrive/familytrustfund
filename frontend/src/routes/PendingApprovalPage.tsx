import { Button } from '../components/ui/Button'

export function PendingApprovalPage() {
  return (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', minHeight: '100vh', padding: 24 }}>
      <div style={{ maxWidth: 420, textAlign: 'center', display: 'flex', flexDirection: 'column', gap: 16 }}>
        <h1 style={{ margin: 0 }}>Account awaiting approval</h1>
        <p className="muted">
          Your Guarantor account is awaiting approval. Please contact Productdrive.proton.me
        </p>
        <Button onClick={() => (window.location.href = '/login')}>Back to sign in</Button>
      </div>
    </div>
  )
}
