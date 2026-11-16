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

export function useCancelDisbursement() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ loanId, reason }: { loanId: string; reason?: string | null }) =>
      api.post(`/guarantor/payments/disbursement/${loanId}/cancel`, { reason }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['guarantor', 'loans'] })
      qc.invalidateQueries({ queryKey: ['loans'] })
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

// ── Offline / manual disbursements (ADR-056) ────────────────────────
//
// The Fund Originator pays a member outside Paystack and reports it. The loan
// only becomes DISBURSED once the member confirms receipt. A live report blocks
// every other disbursement path so a loan can never be double-disbursed.

export type ManualDisbursementStatus =
  | 'Draft'
  | 'PendingMemberConfirmation'
  | 'Confirmed'
  | 'Disputed'
  | 'Cancelled'

export type ManualDisbursementMethod = 'BankTransfer' | 'Cash' | 'Other'

export interface ManualDisbursement {
  id: string
  loanId: string
  fundId: string
  fundName: string
  memberId: string
  memberDisplayName: string
  memberEmail: string
  reportedByGuarantorId: string
  amount: number
  currency: string
  method: ManualDisbursementMethod
  destinationBankName: string | null
  destinationAccountLast4: string | null
  reference: string | null
  note: string | null
  status: ManualDisbursementStatus
  memberNote: string | null
  cancelReason: string | null
  reportedAtUtc: string
  submittedAtUtc: string | null
  confirmedAtUtc: string | null
  disputedAtUtc: string | null
  cancelledAtUtc: string | null
  hasEvidence: boolean
}

export interface CreateOfflineDisbursementInput {
  loanId: string
  amount: number
  method: ManualDisbursementMethod
  destinationBankName?: string | null
  destinationAccountLast4?: string | null
  reference?: string | null
  note?: string | null
}

export const MANUAL_DISBURSEMENT_METHOD_LABELS: Record<ManualDisbursementMethod, string> = {
  BankTransfer: 'Bank transfer',
  Cash: 'Cash',
  Other: 'Other',
}

function invalidateOfflineDisbursements(qc: ReturnType<typeof useQueryClient>) {
  qc.invalidateQueries({ queryKey: ['offline-disbursements'] })
  qc.invalidateQueries({ queryKey: ['guarantor', 'loans'] })
  qc.invalidateQueries({ queryKey: ['loans'] })
  qc.invalidateQueries({ queryKey: ['payments'] })
  qc.invalidateQueries({ queryKey: ['guarantor', 'payments'] })
  qc.invalidateQueries({ queryKey: ['evidence'] })
}

/** The latest offline report for a loan, if any (Fund Originator view). */
export function useGuarantorOfflineDisbursement(loanId: string | null) {
  return useQuery({
    queryKey: ['guarantor', 'payments', 'offline-disbursement', loanId],
    queryFn: () =>
      api.get<ManualDisbursement>(`/guarantor/payments/disbursements/offline/loan/${loanId}`),
    enabled: !!loanId,
    retry: false,
  })
}

export function useCreateOfflineDisbursement() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: CreateOfflineDisbursementInput) =>
      api.post<ManualDisbursement>('/guarantor/payments/disbursements/offline', input),
    onSuccess: () => invalidateOfflineDisbursements(qc),
  })
}

export function useSubmitOfflineDisbursement() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (reportId: string) =>
      api.post<ManualDisbursement>(
        `/guarantor/payments/disbursements/offline/${reportId}/submit`,
      ),
    onSuccess: () => invalidateOfflineDisbursements(qc),
  })
}

export function useCancelOfflineDisbursement() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ reportId, reason }: { reportId: string; reason?: string | null }) =>
      api.post<ManualDisbursement>(
        `/guarantor/payments/disbursements/offline/${reportId}/cancel`,
        { reason },
      ),
    onSuccess: () => invalidateOfflineDisbursements(qc),
  })
}

/** Offline reports for the member's own loans, awaiting confirmation or resolved. */
export function useMyOfflineDisbursements() {
  return useQuery({
    queryKey: ['offline-disbursements', 'mine'],
    queryFn: () => api.get<ManualDisbursement[]>('/loans/disbursements/offline/mine'),
  })
}

export function useConfirmOfflineDisbursement() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ reportId, note }: { reportId: string; note?: string | null }) =>
      api.post<ManualDisbursement>(`/loans/disbursements/offline/${reportId}/confirm`, { note }),
    onSuccess: () => invalidateOfflineDisbursements(qc),
  })
}

export function useDisputeOfflineDisbursement() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ reportId, reason }: { reportId: string; reason: string }) =>
      api.post<ManualDisbursement>(`/loans/disbursements/offline/${reportId}/dispute`, { reason }),
    onSuccess: () => invalidateOfflineDisbursements(qc),
  })
}

