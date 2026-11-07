import { Button } from '../components/ui/Button'
import { useLogout } from '../features/auth/authApi'

export function PendingApprovalPage() {
  const logout = useLogout()

  return (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', minHeight: '100vh', padding: 24 }}>
      <div style={{ maxWidth: 420, textAlign: 'center', display: 'flex', flexDirection: 'column', gap: 16 }}>
        <h1 style={{ margin: 0 }}>Account awaiting approval</h1>
        <p className="muted">
          Your Guarantor account is awaiting approval. Please contact Productdrive.proton.me
        </p>
        {logout.isError && (
          <p className="form-error" role="alert">
            {(logout.error as Error).message || 'Could not sign out. Please try again.'}
          </p>
        )}
        <Button onClick={() => logout.mutate()} disabled={logout.isPending}>
          {logout.isPending ? 'Signing out…' : 'Back to sign in'}
        </Button>
      </div>
    </div>
  )
}
