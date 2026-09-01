import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'

export type ScheduleItemStatus = 'Scheduled' | 'Paid' | 'Overdue'
export type RepaymentKind = 'Scheduled' | 'LumpSum' | 'FullSettlement'

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
  scheduleVersion: number
  kind: RepaymentKind
  expectedAmount: number
  actualAmount: number
  surplus: number
  paidAtUtc: string
  note: string | null
}

export interface MakeRepaymentInput {
  loanId: string
  amount: number
  note?: string | null
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

export function useMakeScheduledPayment() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: MakeRepaymentInput) =>
      api.post<RepaymentSummary>(`/loans/${input.loanId}/repayments`, input),
    onSuccess: (_data, input) => qc.invalidateQueries({ queryKey: loanKey(input.loanId) }),
  })
}

export function useMakeLumpSum() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: MakeRepaymentInput) =>
      api.post<RepaymentSummary>(`/loans/${input.loanId}/repayments/lump-sum`, input),
    onSuccess: (_data, input) => qc.invalidateQueries({ queryKey: loanKey(input.loanId) }),
  })
}

export function useSettleLoan() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: MakeRepaymentInput) =>
      api.post<RepaymentSummary>(`/loans/${input.loanId}/repayments/settle`, input),
    onSuccess: (_data, input) => {
      qc.invalidateQueries({ queryKey: loanKey(input.loanId) })
      qc.invalidateQueries({ queryKey: ['loans'] })
    },
  })
}
