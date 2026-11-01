import { useEffect, useState } from 'react'
import { Landmark, ShieldCheck } from 'lucide-react'
import { api } from '../../lib/api'
import { useMyRecipient, useSaveRecipient, useVerifyRecipient } from './paymentApi'
import { Card } from '../../components/ui/Card'
import { StatusBadge } from '../../components/ui/Badge'
import { Button } from '../../components/ui/Button'
import { SkeletonCard } from '../../components/ui/State'

interface BankOption {
  code: string
  name: string
}

export function MemberBankDetails() {
  const { data: recipient, isLoading } = useMyRecipient()
  const saveRecipient = useSaveRecipient()
  const verifyRecipient = useVerifyRecipient()

  const [showForm, setShowForm] = useState(false)
  const [banks, setBanks] = useState<BankOption[]>([])
  const [bankName, setBankName] = useState('')
  const [bankCode, setBankCode] = useState('')
  const [accountNumber, setAccountNumber] = useState('')
  const [accountName, setAccountName] = useState('')

  useEffect(() => {
    api
      .get<BankOption[]>('/payments/banks')
      .then(setBanks)
      .catch(() => setBanks([]))
  }, [])

  const hasRecipient = !!recipient
  const isVerified = recipient?.isActive === true

  function submit(e: React.FormEvent) {
    e.preventDefault()
    saveRecipient.mutate(
      { bankName, bankCode, accountNumber, accountName },
      { onSuccess: () => setShowForm(false) },
    )
  }

  const error = saveRecipient.error ?? verifyRecipient.error

  if (isLoading) {
    return <SkeletonCard />
  }

  return (
    <Card title="Bank details for disbursements">
      <p className="muted">
        When your loan is approved, funds are disbursed into the account below.
        Your Guarantor can only disburse once your bank details are verified.
      </p>

      {hasRecipient && !showForm && (
        <ul className="property-list" style={{ marginTop: 12 }}>
          <li>
            <span className="property-label">Bank</span>
            <span className="property-value">{recipient!.bankName}</span>
          </li>
          <li>
            <span className="property-label">Account name</span>
            <span className="property-value">{recipient!.accountName}</span>
          </li>
          <li>
            <span className="property-label">Account number</span>
            <span className="property-value">{recipient!.accountNumberMasked}</span>
          </li>
          <li>
            <span className="property-label">Status</span>
            <StatusBadge status={recipient!.status} withDot />
          </li>
        </ul>
      )}

      {isVerified && !showForm && (
        <div style={{ display: 'flex', gap: 8, marginTop: 16 }}>
          <Button variant="secondary" onClick={() => setShowForm(true)}>
            <Landmark size={16} /> Update bank details
          </Button>
        </div>
      )}

      {!isVerified && !showForm && (
        <div style={{ display: 'flex', gap: 8, marginTop: 16 }}>
          <Button onClick={() => setShowForm(true)}>
            <Landmark size={16} /> {hasRecipient ? 'Review / edit details' : 'Add bank details'}
          </Button>
          {hasRecipient && (
            <Button
              variant="secondary"
              onClick={() => verifyRecipient.mutate()}
              disabled={verifyRecipient.isPending}
              title="Verify this account with the payment provider"
            >
              <ShieldCheck size={16} /> {verifyRecipient.isPending ? 'Verifying…' : 'Verify account'}
            </Button>
          )}
        </div>
      )}

      {showForm && (
        <form onSubmit={submit} style={{ marginTop: 12 }}>
          <div className="field">
            <label htmlFor="bank">Bank</label>
            <select
              id="bank"
              value={bankCode}
              onChange={(e) => {
                const code = e.target.value
                setBankCode(code)
                const bank = banks.find((b) => b.code === code)
                setBankName(bank?.name ?? '')
              }}
              required
            >
              <option value="">Select bank</option>
              {banks.map((bank) => (
                <option key={bank.code} value={bank.code}>
                  {bank.name}
                </option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor="account-number">Account number</label>
            <input
              id="account-number"
              value={accountNumber}
              onChange={(e) => setAccountNumber(e.target.value)}
              placeholder="10-digit account number"
              inputMode="numeric"
              required
            />
          </div>
          <div className="field">
            <label htmlFor="account-name">Account name</label>
            <input
              id="account-name"
              value={accountName}
              onChange={(e) => setAccountName(e.target.value)}
              placeholder="Name on the account"
              required
            />
          </div>

          {error && <p className="form-error">{(error as Error).message}</p>}

          <div style={{ display: 'flex', gap: 8 }}>
            <Button type="submit" disabled={saveRecipient.isPending}>
              {saveRecipient.isPending ? 'Saving…' : 'Save details'}
            </Button>
            <Button type="button" variant="ghost" onClick={() => setShowForm(false)}>
              Cancel
            </Button>
          </div>
        </form>
      )}
    </Card>
  )
}
