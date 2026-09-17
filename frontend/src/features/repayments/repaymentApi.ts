import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'

export type ScheduleItemStatus = 'Scheduled' | 'Paid' | 'Overdue'
export type RepaymentKind = 'Scheduled' | 'LumpSum' | 'FullSettlement'
export type PendingRepaymentStatus = 'PendingConfirmation' | 'Confirmed' | 'Rejected'

export interface ScheduleItem {
  id: string
  sequence: number
  dueDateUtc: string
  principalDue: number
  interestDue: number
  expectedAmount: number
  paidAmount: number
  paidAtUtc: string | null
  status: ScheduleItemStatus
}

export interface Schedule {
  id: string
  loanId: string
  version: number
  createdAtUtc: string
  isCurrent: boolean
  items: ScheduleItem[]
}

export interface RepaymentSummary {
  outstandingBalance: number
  outstandingInterest: number
  totalExpected: number
  totalPaid: number
  totalSurplus: number
  overdueItems: number
  overdueAmount: number
  settlementQuote: number
}

export interface RepaymentRecord {
  id: string
  loanId: string
  fundName: string
  scheduleVersion: number
  kind: RepaymentKind
  expectedAmount: number
  actualAmount: number
  surplus: number
  paidAtUtc: string
  note: string | null
}

export interface PagedRepaymentsResult {
  items: RepaymentRecord[]
  totalCount: number
  page: number
  pageSize: number
}

export interface PendingRepayment {
  id: string
  loanId: string
  memberId: string
  fundId: string
  fundName: string
  memberDisplayName: string
  memberEmail: string
  amount: number
  kind: RepaymentKind
  reference: string | null
  note: string | null
  status: PendingRepaymentStatus
  rejectionReason: string | null
  confirmationNote: string | null
  reportedAtUtc: string
  confirmedAtUtc: string | null
  rejectedAtUtc: string | null
  hasEvidence: boolean
}

export interface DeclareRepaymentInput {
  loanId: string
  amount: number
  kind: RepaymentKind
  reference?: string | null
  note?: string | null
}

export interface PendingRepaymentDecisionInput {
  pendingRepaymentId: string
  reason?: string | null
  note?: string | null
}

export interface ConfirmPendingRepaymentResult {
  pending: PendingRepayment
  repaymentPosted: boolean
}

function loanKey(loanId: string) {
  return ['loans', loanId, 'repayments']
}

export function useScheduleItems(loanId: string | null) {
  return useQuery({
    queryKey: [...loanKey(loanId ?? ''), 'schedule'],
    queryFn: () => api.get<ScheduleItem[]>(`/loans/${loanId}/repayments/schedule`),
    enabled: !!loanId,
  })
}

export function useRepaymentSummary(loanId: string | null) {
  return useQuery({
    queryKey: [...loanKey(loanId ?? ''), 'summary'],
    queryFn: () => api.get<RepaymentSummary>(`/loans/${loanId}/repayments/summary`),
    enabled: !!loanId,
  })
}

export function useRepaymentHistory(loanId: string | null) {
  return useQuery({
    queryKey: [...loanKey(loanId ?? ''), 'history'],
    queryFn: () => api.get<RepaymentRecord[]>(`/loans/${loanId}/repayments/history`),
    enabled: !!loanId,
  })
}

export function useMyRepaymentHistory(page: number, pageSize: number) {
  return useQuery({
    queryKey: ['repayments', 'history', 'mine', page, pageSize],
    queryFn: () =>
      api.get<PagedRepaymentsResult>(
        `/loans/repayments/history/mine?page=${page}&pageSize=${pageSize}`,
      ),
  })
}

export function useMyPendingRepayments() {
  return useQuery({
    queryKey: ['repayments', 'pending', 'mine'],
    queryFn: () => api.get<PendingRepayment[]>('/loans/repayments/pending/mine'),
  })
}

export function useDeclareRepayment() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: DeclareRepaymentInput) =>
      api.post<PendingRepayment>(`/loans/${input.loanId}/repayments/declare`, {
        loanId: input.loanId,
        amount: input.amount,
        kind: input.kind,
        reference: input.reference,
        note: input.note,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['repayments'] })
      qc.invalidateQueries({ queryKey: ['loans'] })
    },
  })
}

export function useGuarantorPendingRepayments() {
  return useQuery({
    queryKey: ['guarantor', 'repayments', 'pending'],
    queryFn: () => api.get<PendingRepayment[]>('/guarantor/repayments/pending'),
  })
}

export function useConfirmPendingRepayment() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: PendingRepaymentDecisionInput) =>
      api.post<ConfirmPendingRepaymentResult>('/guarantor/repayments/confirm', input),
    onSuccess: (data) => {
      qc.invalidateQueries({ queryKey: ['guarantor', 'repayments'] })
      qc.invalidateQueries({ queryKey: ['repayments'] })
      qc.invalidateQueries({ queryKey: ['loans', data.pending.loanId, 'repayments'] })
      qc.invalidateQueries({ queryKey: ['loans'] })
    },
  })
}

export function useRejectPendingRepayment() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: PendingRepaymentDecisionInput) =>
      api.post<PendingRepayment>('/guarantor/repayments/reject', input),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['guarantor', 'repayments'] })
      qc.invalidateQueries({ queryKey: ['repayments'] })
    },
  })
}