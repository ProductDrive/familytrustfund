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
  authorizationUrl: string | null
  grossAmount: number | null
  estimatedFee: number | null
  capitalTransactionId: string | null
}

export interface SaveRecipientInput {
  bankCode: string
  bankName: string
  accountNumber: string
  accountName: string
}

export interface InitiateDisbursementInput {
  loanId: string
  callbackUrl?: string
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
      qc.invalidateQueries({ queryKey: ['guarantor', 'payments'] })
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

// ── Funded capital (funding the platform account) ───────────────────

export type CapitalTransactionStatus = 'PendingConfirmation' | 'Confirmed' | 'Failed'

export interface CapitalTransaction {
  id: string
  fundId: string
  provider: string
  status: CapitalTransactionStatus
  amountGross: number
  providerFee: number | null
  amountNet: number | null
  providerReference: string
  failureReason: string | null
  initiatedAtUtc: string
  completedAtUtc: string | null
}

export interface CapitalFundingSummary {
  totalFunded: number
  transactionCount: number
}

export interface CapitalTransactionPage {
  items: CapitalTransaction[]
  totalCount: number
  page: number
  pageSize: number
}

export function useFundCapitalSummary(fundId: string | null) {
  return useQuery({
    queryKey: ['guarantor', 'funds', fundId, 'capital', 'summary'],
    queryFn: () => api.get<CapitalFundingSummary>(`/guarantor/funds/${fundId}/capital`),
    enabled: !!fundId,
  })
}

export function useVerifyCapitalPayment(fundId: string | null) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (providerReference: string) =>
      api.post<CapitalTransaction>(`/guarantor/funds/${fundId}/capital/payments/${providerReference}/verify`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['guarantor', 'funds', fundId, 'capital'] })
      qc.invalidateQueries({ queryKey: ['guarantor', 'loans'] })
      qc.invalidateQueries({ queryKey: ['funds'] })
      qc.invalidateQueries({ queryKey: ['payments'] })
      qc.invalidateQueries({ queryKey: ['guarantor', 'payments'] })
    },
  })
}

export function useCapitalTransactions(
  fundId: string | null,
  page = 1,
  pageSize = 10,
) {
  return useQuery({
    queryKey: ['guarantor', 'funds', fundId, 'capital', 'payments', page, pageSize],
    queryFn: () =>
      api.get<CapitalTransactionPage>(
        `/guarantor/funds/${fundId}/capital/payments?page=${page}&pageSize=${pageSize}`,
      ),
    enabled: !!fundId,
  })
}
