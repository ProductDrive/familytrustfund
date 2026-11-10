import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'

export type FundType = 'Family' | 'External'
export type FundStatus = 'Active' | 'Inactive' | 'Transitioned'

/** Single-Guarantor or Group Guarantor operation (ADR-051). */
export type FundGuarantorMode = 'Single' | 'Group'
export type FundGuarantorRole = 'Originator' | 'Guarantor'
export type FundGuarantorStatus =
  | 'Invited'
  | 'Active'
  | 'Declined'
  | 'Revoked'
  | 'Removed'
  | 'Expired'

export interface Fund {
  id: string
  name: string
  type: FundType
  joinCode: string
  committedCapital: number
  /** The Fund Originator; only they may initiate disbursement (ADR-053). */
  guarantorId: string
  guarantorMode: FundGuarantorMode
  contributionMultiplier: number
  interestRate: number | null
  minLoanTermMonths: number
  maxLoanTermMonths: number
  howItWorks: string | null
  repaymentBankName: string | null
  repaymentAccountNumber: string | null
  repaymentAccountName: string | null
  status: FundStatus
  transitionedAtUtc: string | null
  createdAtUtc: string
}

/** One Guarantor's relationship to a group fund. */
export interface FundGuarantor {
  id: string
  fundId: string
  guarantorId: string
  guarantorDisplayName: string
  guarantorEmail: string
  role: FundGuarantorRole
  status: FundGuarantorStatus
  committedCapital: number
  invitedAtUtc: string
  respondedAtUtc: string | null
  expiresAtUtc: string | null
  isExpired: boolean
  isOriginator: boolean
}

/** An invitation or membership as seen by the invited Guarantor. */
export interface GuarantorInvitation {
  membershipId: string
  fundId: string
  fundName: string
  fundType: FundType
  originatorId: string
  originatorDisplayName: string
  originatorEmail: string
  committedCapital: number
  status: FundGuarantorStatus
  invitedAtUtc: string
  expiresAtUtc: string | null
  isExpired: boolean
}

export interface FundTransitionStatus {
  fundId: string
  isFamilyFund: boolean
  isTransitioned: boolean
  committedCapital: number
  totalFamilyContributions: number
  isEligible: boolean
  remainingToThreshold: number
}

export interface FundTransitionResult {
  fundId: string
  committedCapital: number
  totalFamilyContributions: number
  transitionedAtUtc: string
}

export interface CreateFundRequest {
  name: string
  type: FundType
  committedCapital: number
  /** Defaults to a single-Guarantor fund when omitted. */
  guarantorMode?: FundGuarantorMode
  contributionMultiplier?: number
  interestRate?: number | null
  minLoanTermMonths?: number
  maxLoanTermMonths?: number
}

export function useFunds() {
  return useQuery({
    queryKey: ['funds'],
    queryFn: () => api.get<Fund[]>('/funds'),
  })
}

export function useCreateFund() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (input: CreateFundRequest) =>
      api.post<Fund>('/funds', input),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['funds'] }),
  })
}

export function useFundTransitionStatus(fundId: string | null) {
  return useQuery({
    queryKey: ['funds', 'transition', fundId],
    queryFn: () => api.get<FundTransitionStatus>(`/funds/${fundId}/transition-status`),
    enabled: !!fundId,
  })
}

export function useTransitionFund() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (fundId: string) =>
      api.post<FundTransitionResult>(`/funds/${fundId}/transition`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['funds'] })
      qc.invalidateQueries({ queryKey: ['loans'] })
    },
  })
}

export interface RepaymentAccountInput {
  bankName: string | null
  accountNumber: string | null
  accountName: string | null
}

export function useUpdateRepaymentAccount() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ fundId, ...input }: RepaymentAccountInput & { fundId: string }) =>
      api.put<Fund>(`/funds/${fundId}/repayment-account`, input),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['funds'] })
    },
  })
}

// ── Group Guarantors (ADR-051) ──────────────────────────────────────

export function useFundGuarantors(fundId: string | null) {
  return useQuery({
    queryKey: ['funds', fundId, 'guarantors'],
    queryFn: () => api.get<FundGuarantor[]>(`/funds/${fundId}/guarantors`),
    enabled: !!fundId,
  })
}

export interface InviteGuarantorInput {
  fundId: string
  email: string
  committedCapital: number
}

export function useInviteGuarantor() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ fundId, ...input }: InviteGuarantorInput) =>
      api.post<FundGuarantor>(`/funds/${fundId}/guarantors/invitations`, input),
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['funds', variables.fundId, 'guarantors'] })
      qc.invalidateQueries({ queryKey: ['funds'] })
    },
  })
}

export function useRevokeGuarantor() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ fundId, guarantorId }: { fundId: string; guarantorId: string }) =>
      api.delete<FundGuarantor>(`/funds/${fundId}/guarantors/${guarantorId}`),
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['funds', variables.fundId, 'guarantors'] })
      qc.invalidateQueries({ queryKey: ['funds'] })
    },
  })
}

export function useLeaveFund() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (fundId: string) =>
      api.post<void>(`/funds/${fundId}/guarantors/leave`),
    onSuccess: (_data, fundId) => {
      qc.invalidateQueries({ queryKey: ['funds', fundId, 'guarantors'] })
      qc.invalidateQueries({ queryKey: ['funds'] })
      qc.invalidateQueries({ queryKey: ['guarantor'] })
    },
  })
}

export function useUpdateGuarantorCommitment() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({
      fundId,
      committedCapital,
    }: {
      fundId: string
      committedCapital: number
    }) =>
      api.put<FundGuarantor>(`/funds/${fundId}/guarantors/me/commitment`, {
        committedCapital,
      }),
    onSuccess: (_data, variables) => {
      qc.invalidateQueries({ queryKey: ['funds', variables.fundId, 'guarantors'] })
      qc.invalidateQueries({ queryKey: ['funds'] })
    },
  })
}

export function useMyInvitations() {
  return useQuery({
    queryKey: ['guarantor', 'invitations'],
    queryFn: () => api.get<GuarantorInvitation[]>('/guarantor/invitations'),
  })
}

export function useAcceptInvitation() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (membershipId: string) =>
      api.post<GuarantorInvitation>(`/guarantor/invitations/${membershipId}/accept`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['guarantor', 'invitations'] })
      qc.invalidateQueries({ queryKey: ['funds'] })
    },
  })
}

export function useDeclineInvitation() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (membershipId: string) =>
      api.post<GuarantorInvitation>(`/guarantor/invitations/${membershipId}/decline`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['guarantor', 'invitations'] })
    },
  })
}
