import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'

export type FundType = 'Family' | 'External'
export type FundStatus = 'Active' | 'Inactive' | 'Transitioned'

export interface Fund {
  id: string
  name: string
  type: FundType
  joinCode: string
  committedCapital: number
  contributionMultiplier: number
  interestRate: number | null
  howItWorks: string | null
  status: FundStatus
  transitionedAtUtc: string | null
  createdAtUtc: string
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
  contributionMultiplier?: number
  interestRate?: number | null
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
