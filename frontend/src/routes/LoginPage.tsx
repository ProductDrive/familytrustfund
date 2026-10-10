import { useState } from 'react'
import {
  useAuthOptions,
  useRequestOtp,
  useVerifyOtp,
  useSignInOptions,
  useDevLogin,
  type Role,
} from '../features/auth/authApi'
import { LogoMark } from '../components/ui/Avatar'
import { Button } from '../components/ui/Button'

type Step = 'email' | 'terms' | 'code' | 'pending'

export function LoginPage() {
  const { data: options } = useAuthOptions()
  const { devEnabled } = useSignInOptions()

  const otpEnabled = options?.otpEnabled ?? false
  const googleEnabled = options?.googleEnabled ?? false
  const termsVersion = options?.termsVersion ?? '1.0'

  if (!options && !devEnabled) {
    return (
      <div className="login-page">
        <main className="login-card">
          <p className="muted">Checking sign-in options…</p>
        </main>
      </div>
    )
  }

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

        {otpEnabled && <OtpSignIn termsVersion={termsVersion} />}

        {otpEnabled && googleEnabled && <Divider label="or" />}

        {googleEnabled && (
          <a className="btn btn--secondary btn-google" href="/api/auth/google/challenge">
            <GoogleMark />
            Sign in with Google
          </a>
        )}

        {devEnabled && (
          <>
            {(otpEnabled || googleEnabled) && <Divider label="dev" />}
            <DevSignInForm />
          </>
        )}
      </main>
    </div>
  )
}

function OtpSignIn({ termsVersion }: { termsVersion: string }) {
  const requestOtp = useRequestOtp()
  const verifyOtp = useVerifyOtp()

  const [step, setStep] = useState<Step>('email')
  const [email, setEmail] = useState('')
  const [role, setRole] = useState<'Member' | 'Guarantor'>('Member')
  const [termsAccepted, setTermsAccepted] = useState(false)
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)

  function sendCode(withTerms: boolean) {
    setError(null)
    requestOtp.mutate(
      {
        email: email.trim(),
        role: role as Role,
        termsAccepted: withTerms,
        termsVersion: withTerms ? termsVersion : undefined,
      },
      {
        onSuccess: () => {
          setTermsAccepted(withTerms)
          setCode('')
          setStep('code')
        },
        onError: (err) => setError(errorMessage(err)),
      },
    )
  }

  function handleEmailStep(e: React.FormEvent) {
    e.preventDefault()
    if (!email.trim()) return
    if (role === 'Guarantor' && !termsAccepted) {
      setError(null)
      setStep('terms')
      return
    }
    sendCode(role === 'Guarantor')
  }

  function handleVerify(e: React.FormEvent) {
    e.preventDefault()
    if (!code.trim()) return
    setError(null)
    verifyOtp.mutate(
      { email: email.trim(), code: code.trim() },
      {
        onSuccess: (user) => {
          if (user.guarantorApprovalStatus === 'Pending') {
            setStep('pending')
            return
          }
          window.location.href = '/'
        },
        onError: (err) => setError(errorMessage(err)),
      },
    )
  }

  if (step === 'pending') {
    return (
      <div>
        <p className="form-title">Guarantor review in progress</p>
        <p className="muted">
          Your account is being reviewed by a Super Admin. You can continue as a member while you
          wait.
        </p>
        <Button className="btn--block" onClick={() => (window.location.href = '/')}>
          Continue to dashboard
        </Button>
      </div>
    )
  }

  if (step === 'terms') {
    return (
      <div>
        <p className="form-title">Guarantor terms</p>
        <p className="muted">
          Guarantors create and manage lending funds and are responsible for confirming contributions
          and repayments.
        </p>
        <ul className="otp-terms">
          <li>Guarantor accounts are reviewed and approved by a Super Admin.</li>
          <li>Committed capital is an indication of funds you are willing to lend, not a deposit.</li>
          <li>You are responsible for confirming manual contributions and repayments.</li>
        </ul>
        <label className="otp-check">
          <input
            type="checkbox"
            checked={termsAccepted}
            onChange={(e) => setTermsAccepted(e.target.checked)}
          />
          <span>I accept the guarantor terms (version {termsVersion}).</span>
        </label>
        {error && <p className="form-error">{error}</p>}
        <Button
          className="btn--block"
          disabled={!termsAccepted || requestOtp.isPending}
          onClick={() => sendCode(true)}
        >
          {requestOtp.isPending ? 'Sending code…' : 'Accept and continue'}
        </Button>
        <button
          type="button"
          className="link-button"
          onClick={() => {
            setError(null)
            setStep('email')
          }}
        >
          Back
        </button>
      </div>
    )
  }

  if (step === 'code') {
    return (
      <form onSubmit={handleVerify}>
        <p className="form-title">Enter your code</p>
        <p className="muted">
          We sent a 6-digit code to <strong>{email.trim()}</strong>.
        </p>
        <div className="field">
          <label htmlFor="otp-code">Sign-in code</label>
          <input
            id="otp-code"
            inputMode="numeric"
            autoComplete="one-time-code"
            maxLength={10}
            value={code}
            onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))}
            placeholder="123456"
            autoFocus
          />
          <p className="field-hint">The code expires in a few minutes.</p>
        </div>
        {error && <p className="form-error">{error}</p>}
        <Button type="submit" className="btn--block" disabled={verifyOtp.isPending || !code.trim()}>
          {verifyOtp.isPending ? 'Verifying…' : 'Sign in'}
        </Button>
        <button
          type="button"
          className="link-button"
          disabled={requestOtp.isPending}
          onClick={() => sendCode(termsAccepted)}
        >
          {requestOtp.isPending ? 'Sending…' : 'Resend code'}
        </button>
        <button
          type="button"
          className="link-button"
          onClick={() => {
            setError(null)
            setStep(role === 'Guarantor' ? 'terms' : 'email')
          }}
        >
          Use a different email
        </button>
      </form>
    )
  }

  return (
    <form onSubmit={handleEmailStep}>
      <p className="form-title">Sign in or create an account</p>
      <p className="muted">We will email you a one-time code. No password needed.</p>
      <div className="field">
        <label htmlFor="otp-email">Email address</label>
        <input
          id="otp-email"
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          placeholder="you@example.com"
          autoComplete="email"
          autoFocus
        />
      </div>
      <div className="field">
        <label htmlFor="otp-role">Join as</label>
        <select
          id="otp-role"
          value={role}
          onChange={(e) => {
            setRole(e.target.value as 'Member' | 'Guarantor')
            setTermsAccepted(false)
          }}
        >
          <option value="Member">Member</option>
          <option value="Guarantor">Guarantor</option>
        </select>
        <p className="field-hint">
          {role === 'Guarantor'
            ? 'Guarantor accounts are reviewed by a Super Admin before activation.'
            : 'Members join funds and request loans using a guarantor code.'}
        </p>
      </div>
      {error && <p className="form-error">{error}</p>}
      <Button type="submit" className="btn--block" disabled={requestOtp.isPending || !email.trim()}>
        {requestOtp.isPending ? 'Sending code…' : 'Continue'}
      </Button>
    </form>
  )
}

function errorMessage(err: unknown): string {
  const e = err as { status?: number; message?: string }
  if (e?.status === 429) {
    return 'Too many attempts. Please wait a moment and try again.'
  }
  return e?.message ?? 'Something went wrong. Please try again.'
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
      <p className="muted">Local-only shortcut. Exercises the real role and cookie pipeline.</p>
      <div className="field">
        <label htmlFor="dev-email">Email</label>
        <input
          id="dev-email"
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          placeholder="you@example.com"
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