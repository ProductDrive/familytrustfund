import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'

export type LoanStatus =
  | 'Pending'
  | 'Approved'
  | 'Rejected'
  | 'DisbursementPending'
  | 'Disbursed'
  | 'Completed'
  | 'Defaulted'
  | 'Cancelled'

export type RepaymentFrequency = 'Weekly' | 'Biweekly' | 'Monthly'

export type LoanFundingSource = 'GuarantorCapital' | 'FamilyCapital'

export type FundType = 'Family' | 'External'

export type FundGuarantorMode = 'Single' | 'Group'

export type LoanApprovalStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled'

/** One Guarantor's attributable decision on a loan (ADR-052). */
export interface LoanApproval {
  guarantorId: string
  guarantorDisplayName: string
  guarantorEmail: string
  status: LoanApprovalStatus
  decidedAtUtc: string | null
  comment: string | null
}

export interface Loan {
  id: string
  fundId: string
  fundName: string
  fundType: FundType
  memberId: string
  memberDisplayName: string
  memberEmail: string
  requestedAmount: number
  approvedAmount: number | null
  interestRate: number
  requestedFrequency: RepaymentFrequency
  approvedFrequency: RepaymentFrequency | null
  requestedTermMonths: number | null
  requestedInstalments: number | null
  approvedTermMonths: number | null
  repaymentTerm: number
  status: LoanStatus
  fundingSource: LoanFundingSource
  outstandingBalance: number
  totalRepayable: number
  purpose: string | null
  rejectionReason: string | null
  requestedAtUtc: string
  approvedAtUtc: string | null
  rejectedAtUtc: string | null
  cancelledAtUtc: string | null
  fundGuarantorMode: FundGuarantorMode
  fundOriginatorId: string
  termsFrozen: boolean
  requiredApprovalCount: number
  approvedApprovalCount: number
  rejectedApprovalCount: number
  approvals: LoanApproval[]
}

export interface CancelLoanInput {
  loanId: string
}

export interface LendingCapacity {
  fundId: string
  committedCapital: number
  activeDisbursedLoans: number
  availableLendingCapacity: number
  familyBorrowingEntitlement: number | null
  fundCredit: number | null
}

export interface RequestLoanInput {
  fundId: string
  amount: number
  frequency: RepaymentFrequency
  termMonths: number
  instalments: number
  purpose?: string | null
}

export interface ApproveLoanInput {
  loanId: string
  approvedAmount: number
  approvedFrequency: RepaymentFrequency
  approvedTermMonths: number
  approvedInstalments: number
  confirmedWithMember: boolean
}

export interface LoanPreviewInput {
  fundId: string
  amount: number
  frequency: RepaymentFrequency
  termMonths: number
  instalments: number
}

export interface LoanPreview {
  fundId: string
  fundType: FundType
  amount: number
  frequency: RepaymentFrequency
  termMonths: number
  instalments: number
  interestRate: number
  totalRepayable: number
  interestAmount: number
  instalmentAmount: number
  finalInstalmentAmount: number
  firstDueDateUtc: string | null
  finalDueDateUtc: string | null
  committedCapital: number
  activeDisbursedLoans: number
  availableLendingCapacity: number
  familyBorrowingEntitlement: number | null
  fundCredit: number | null
  minTermMonths: number
  maxTermMonths: number
  isValid: boolean
  errors: string[]
}

export interface GuarantorLoanPreviewInput {
  loanId: string
  approvedAmount: number
  approvedFrequency: RepaymentFrequency
  approvedTermMonths: number
  approvedInstalments: number
}

export interface LoanTermChange {
  field: string
  requested: string
  approved: string
}

export interface GuarantorLoanPreview {
  preview: LoanPreview
  termsChanged: boolean
  changes: LoanTermChange[]
}

/**
 * UI hint only. The server validates the term/instalment combination via
 * LoanTermRules and remains authoritative (AGENTS §7, §18.13).
 */
export function suggestedInstalments(
  termMonths: number,
  frequency: RepaymentFrequency,
): number {
  if (!Number.isFinite(termMonths) || termMonths <= 0) return 0
  const count =
    frequency === 'Weekly'
      ? Math.round((termMonths * 52) / 12)
      : frequency === 'Biweekly'
        ? Math.round((termMonths * 26) / 12)
        : termMonths
  return Math.max(1, count)
}

export interface RejectLoanInput {
  loanId: string
  reason?: string | null
}

// ── Member hooks ────────────────────────────────────────────────────

export function useMyLoans() {
  return useQuery({
    queryKey: ['loans', 'mine'],
    queryFn: () => api.get<Loan[]>('/loans/mine'),
  })
}

export function useLendingCapacity(fundId: string | null) {
  return useQuery({
    queryKey: ['loans', 'lending-capacity', fundId],
    queryFn: () => api.get<LendingCapacity>(`/loans/lending-capacity/${fundId}`),
    enabled: !!fundId,
  })
}

export function useLoanPreview(input: LoanPreviewInput | null) {
  return useQuery({
    queryKey: ['loans', 'preview', input],
    queryFn: () => api.post<LoanPreview>('/loans/preview', input),
    enabled: !!input,
  })
}

export function useGuarantorLoanPreview(input: GuarantorLoanPreviewInput | null) {
  return useQuery({
    queryKey: ['guarantor', 'loans', 'preview', input],
    queryFn: () => api.post<GuarantorLoanPreview>('/guarantor/loans/preview', input),
    enabled: !!input,
  })
}

export function useRequestLoan() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: RequestLoanInput) => api.post<Loan>('/loans/request', input),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['loans'] })
    },
  })
}

export function useCancelLoan() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: CancelLoanInput) =>
      api.post<Loan>(`/loans/${input.loanId}/cancel`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['loans'] })
    },
  })
}

// ── Guarantor hooks ─────────────────────────────────────────────────

export function useGuarantorPendingLoans() {
  return useQuery({
    queryKey: ['guarantor', 'loans', 'pending'],
    queryFn: () => api.get<Loan[]>('/guarantor/loans/pending'),
  })
}

export function useFundLoans(fundId: string | null) {
  return useQuery({
    queryKey: ['guarantor', 'loans', 'fund', fundId],
    queryFn: () => api.get<Loan[]>(`/guarantor/loans/fund/${fundId}`),
    enabled: !!fundId,
  })
}

export function useApproveLoan() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: ApproveLoanInput) =>
      api.post<Loan>('/guarantor/loans/approve', input),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['guarantor', 'loans'] })
      qc.invalidateQueries({ queryKey: ['loans'] })
    },
  })
}

export function useRejectLoan() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: RejectLoanInput) =>
      api.post<Loan>('/guarantor/loans/reject', input),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['guarantor', 'loans'] })
      qc.invalidateQueries({ queryKey: ['loans'] })
    },
  })
}

export function useGuarantorCancelLoan() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: CancelLoanInput) =>
      api.post<Loan>('/guarantor/loans/cancel', { loanId: input.loanId }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['guarantor', 'loans'] })
      qc.invalidateQueries({ queryKey: ['loans'] })
    },
  })
}

export function useGuarantorLendingCapacity(fundId: string | null) {
  return useQuery({
    queryKey: ['guarantor', 'loans', 'lending-capacity', fundId],
    queryFn: () =>
      api.get<LendingCapacity>(`/guarantor/loans/lending-capacity/${fundId}`),
    enabled: !!fundId,
  })
}