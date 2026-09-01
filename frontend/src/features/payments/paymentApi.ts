import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'

export type PaymentRecipientStatus = 'Unverified' | 'Active' | 'Disabled'
export type DisbursementStatus = 'Pending' | 'Successful' | 'Failed' | 'Reversed'

export interface PaymentRecipient {
  id: string
  memberId: string
  provider: string
  bankName: string
  bankCode: string
  accountName: string
  accountNumberMasked: string
  status: PaymentRecipientStatus
  isActive: boolean
}

export interface Disbursement {
  id: string
  loanId: string
  recipientId: string
  amount: number
  currency: string
  status: DisbursementStatus
  providerReference: string
  failureReason: string | null
  initiatedAtUtc: string
  completedAtUtc: string | null
}

export interface SaveRecipientInput {
  bankCode: string
  bankName: string
  accountNumber: string
  accountName: string
}

export interface InitiateDisbursementInput {
  loanId: string
}

// ── Member hooks ────────────────────────────────────────────────────

export function useMyRecipient() {
  return useQuery({
    queryKey: ['payments', 'recipient', 'mine'],
    queryFn: () => api.get<PaymentRecipient>('/payments/recipient'),
    retry: false,
  })
}

export function useSaveRecipient() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: SaveRecipientInput) =>
      api.post<PaymentRecipient>('/payments/recipient', input),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['payments', 'recipient'] })
    },
  })
}

export function useVerifyRecipient() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => api.post<PaymentRecipient>('/payments/recipient/verify'),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['payments', 'recipient'] })
    },
  })
}

export function useMyLoanDisbursement(loanId: string | null) {
  return useQuery({
    queryKey: ['payments', 'disbursement', loanId],
    queryFn: () => api.get<Disbursement>(`/payments/disbursement/${loanId}`),
    enabled: !!loanId,
    retry: false,
  })
}

// ── Guarantor hooks ─────────────────────────────────────────────────

export function useDisburseLoan() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: InitiateDisbursementInput) =>
      api.post<Disbursement>('/guarantor/payments/disburse', input),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['guarantor', 'loans'] })
      qc.invalidateQueries({ queryKey: ['loans'] })
      qc.invalidateQueries({ queryKey: ['payments'] })
    },
  })
}

export function useGuarantorLoanDisbursement(loanId: string | null) {
  return useQuery({
    queryKey: ['guarantor', 'payments', 'disbursement', loanId],
    queryFn: () => api.get<Disbursement>(`/guarantor/payments/disbursement/${loanId}`),
    enabled: !!loanId,
    retry: false,
  })
}
