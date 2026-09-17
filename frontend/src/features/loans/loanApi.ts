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
  purpose?: string | null
}

export interface ApproveLoanInput {
  loanId: string
  approvedAmount: number
  approvedFrequency: RepaymentFrequency
  repaymentTerm?: number
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