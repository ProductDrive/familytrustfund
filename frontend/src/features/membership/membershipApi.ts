import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import type { Fund, FundStatus, FundType } from '../funds/fundApi'

export type MemberStatus = 'Active' | 'Suspended'

export interface Membership {
  fundId: string
  fundName: string
  fundType: FundType
  fundStatus: FundStatus
  joinCode: string
  contributionMultiplier: number
  interestRate: number | null
  howItWorks: string | null
  status: MemberStatus
  joinedAtUtc: string
}

export interface FundMember {
  memberId: string
  displayName: string
  email: string
  status: MemberStatus
  joinedAtUtc: string
}

export interface AdminMembership {
  membershipId: string
  fundId: string
  fundName: string
  memberId: string
  displayName: string
  email: string
  status: MemberStatus
  joinedAtUtc: string
}

export function useMyMemberships() {
  return useQuery({
    queryKey: ['memberships', 'mine'],
    queryFn: () => api.get<Membership[]>('/memberships/mine'),
  })
}

export function useJoinFund() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (joinCode: string) =>
      api.post<Membership>('/memberships/join', { joinCode }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['memberships'] })
      qc.invalidateQueries({ queryKey: ['funds'] })
    },
  })
}

export function useFundMembers(fundId: string) {
  return useQuery({
    queryKey: ['funds', fundId, 'members'],
    queryFn: () => api.get<FundMember[]>(`/funds/${fundId}/members`),
    enabled: !!fundId,
  })
}

export function useUpdateHowItWorks(fundId: string) {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (content: string | null) =>
      api.put<Fund>(`/funds/${fundId}/how-it-works`, { content }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['funds'] }),
  })
}

export function useAdminMemberships() {
  return useQuery({
    queryKey: ['admin', 'memberships'],
    queryFn: () => api.get<AdminMembership[]>('/admin/memberships'),
  })
}