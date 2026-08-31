import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'

export type FundType = 'Family' | 'External'
export type FundStatus = 'Active' | 'Inactive'

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
  createdAtUtc: string
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
