import { useState } from 'react'
import { useSignInOptions, useDevLogin, type Role } from '../features/auth/authApi'
import { LogoMark } from '../components/ui/Avatar'
import { Button } from '../components/ui/Button'

export function LoginPage() {
  const { devEnabled, googleEnabled } = useSignInOptions()

  return (
    <div className="login-page">
      <main className="login-card">
        <header className="login-brand">
          <LogoMark size="lg" />
          <div>
            <h1>Family Trust Fund</h1>
            <p>Guarantor-led, closed-group lending</p>
          </div>
        </header>

        {googleEnabled && (
          <a className="btn btn--secondary btn-google" href="/api/auth/google/challenge">
            <GoogleMark />
            Sign in with Google
          </a>
        )}

        {googleEnabled && devEnabled && <Divider label="or" />}

        {devEnabled && <DevSignInForm />}

        {!googleEnabled && !devEnabled && (
          <p className="muted">Checking sign-in options…</p>
        )}
      </main>
    </div>
  )
}

function Divider({ label }: { label: string }) {
  return <div className="divider">{label}</div>
}

function GoogleMark() {
  return (
    <svg className="google-mark" viewBox="0 0 48 48" aria-hidden="true">
      <path
        fill="#EA4335"
        d="M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z"
      />
      <path
        fill="#4285F4"
        d="M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z"
      />
      <path
        fill="#FBBC05"
        d="M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z"
      />
      <path
        fill="#34A853"
        d="M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z"
      />
    </svg>
  )
}

function DevSignInForm() {
  const devLogin = useDevLogin()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [role, setRole] = useState<Role>('Member')

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault()
    if (!email.trim()) return
    devLogin.mutate(
      {
        email: email.trim(),
        displayName: email.trim(),
        role,
      },
      { onSuccess: () => { window.location.href = '/' } },
    )
  }

  return (
    <form onSubmit={handleSubmit}>
      <p className="form-title">Development sign-in</p>
      <p className="muted">For local testing only — any password works.</p>
      <div className="field">
        <label htmlFor="dev-email">Email</label>
        <input
          id="dev-email"
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          placeholder="you@example.com"
          autoComplete="email"
          required
        />
      </div>
      <div className="field">
        <label htmlFor="dev-password">Password</label>
        <input
          id="dev-password"
          type="password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          placeholder="••••••••"
          autoComplete="current-password"
        />
      </div>
      <div className="field">
        <label htmlFor="dev-role">Sign in as</label>
        <select
          id="dev-role"
          value={role}
          onChange={(e) => setRole(e.target.value as Role)}
        >
          <option value="Member">Member</option>
          <option value="Guarantor">Guarantor</option>
          <option value="SuperAdmin">SuperAdmin</option>
        </select>
      </div>
      {devLogin.isError && (
        <p className="form-error">{(devLogin.error as Error).message}</p>
      )}
      <Button type="submit" disabled={devLogin.isPending} className="btn--block">
        {devLogin.isPending ? 'Signing in…' : 'Sign in'}
      </Button>
    </form>
  )
}