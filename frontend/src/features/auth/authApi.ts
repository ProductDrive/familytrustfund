import { useMutation, useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'

export type Role = 'SuperAdmin' | 'Guarantor' | 'Member'

export interface CurrentUser {
  id: string
  email: string
  displayName: string
  roles: Role[]
}

export interface DevLoginRequest {
  email: string
  displayName: string
  role: Role
}

export function useCurrentUser() {
  return useQuery({
    queryKey: ['me'],
    queryFn: () => api.get<CurrentUser>('/auth/me'),
    retry: false,
  })
}

export function useIsAuthenticated() {
  const { data, isError, isLoading } = useCurrentUser()
  return {
    isLoading,
    isAuthenticated: !!data && !isError,
    user: data,
  }
}

export interface DevAuthStatus {
  enabled: boolean
  googleEnabled: boolean
}

export interface SignInOptions {
  devEnabled: boolean
  googleEnabled: boolean
}

export function useDevAuthEnabled() {
  return useQuery({
    queryKey: ['dev-auth'],
    queryFn: () => api.get<DevAuthStatus>('/auth/dev/status'),
    retry: false,
  })
}

export function useSignInOptions(): SignInOptions {
  const { data, isError } = useDevAuthEnabled()
  if (isError) return { devEnabled: false, googleEnabled: true }
  if (!data) return { devEnabled: false, googleEnabled: false }
  return { devEnabled: data.enabled, googleEnabled: data.googleEnabled }
}

export function useDevLogin() {
  return useMutation({
    mutationFn: (input: DevLoginRequest) =>
      api.post<CurrentUser>('/auth/dev/login', input),
  })
}
