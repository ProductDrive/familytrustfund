import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'

export type ContributionStatus = 'PendingConfirmation' | 'Confirmed' | 'Rejected'

export interface Contribution {
  id: string
  fundId: string
  fundName: string
  memberId: string
  memberDisplayName: string
  memberEmail: string
  amount: number
  status: ContributionStatus
  reference: string | null
  note: string | null
  rejectionReason: string | null
  reportedAtUtc: string
  confirmedAtUtc: string | null
  rejectedAtUtc: string | null
}

export interface ContributionSummary {
  fundId: string
  memberId: string
  fundCredit: number
  borrowingEntitlement: number | null
  hasActiveDisbursedLoan: boolean
}

export interface ReportContributionInput {
  fundId: string
  amount: number
  reference?: string | null
  note?: string | null
}

export interface ConfirmContributionInput {
  contributionId: string
}

export interface RejectContributionInput {
  contributionId: string
  reason?: string | null
}

// ── Member hooks ────────────────────────────────────────────────────

export function useMyContributions() {
  return useQuery({
    queryKey: ['contributions', 'mine'],
    queryFn: () => api.get<Contribution[]>('/contributions/mine'),
  })
}

export function useMyFundCredit() {
  return useQuery({
    queryKey: ['contributions', 'fund-credit'],
    queryFn: () => api.get<ContributionSummary[]>('/contributions/fund-credit'),
  })
}

export function useReportContribution() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: ReportContributionInput) =>
      api.post<Contribution>('/contributions/report', input),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['contributions'] })
    },
  })
}

// ── Guarantor hooks ─────────────────────────────────────────────────

export function useGuarantorPendingContributions() {
  return useQuery({
    queryKey: ['guarantor', 'contributions', 'pending'],
    queryFn: () => api.get<Contribution[]>('/guarantor/contributions/pending'),
  })
}

export function useConfirmContribution() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: ConfirmContributionInput) =>
      api.post<Contribution>('/guarantor/contributions/confirm', input),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['guarantor', 'contributions'] })
      qc.invalidateQueries({ queryKey: ['contributions'] })
    },
  })
}

export function useRejectContribution() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: RejectContributionInput) =>
      api.post<Contribution>('/guarantor/contributions/reject', input),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['guarantor', 'contributions'] })
      qc.invalidateQueries({ queryKey: ['contributions'] })
    },
  })
}
