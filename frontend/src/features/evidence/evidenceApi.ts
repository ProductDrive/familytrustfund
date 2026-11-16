import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'

export interface PaymentEvidence {
  id: string
  uploadedByUserId: string
  resourceType: string
  resourceId: string
  originalFileName: string
  contentType: string
  sizeBytes: number
  uploadedAtUtc: string
}

export function memberEvidenceUrl(evidenceId: string) {
  return `/api/evidence/${evidenceId}`
}

export function guarantorEvidenceUrl(evidenceId: string) {
  return `/api/guarantor/evidence/${evidenceId}`
}

// ── Member uploads ──────────────────────────────────────────────────

export function useUploadContributionEvidence() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ contributionId, file }: { contributionId: string; file: File }) => {
      const form = new FormData()
      form.append('file', file)
      return api.postForm<PaymentEvidence>(`/evidence/contribution/${contributionId}`, form)
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['contributions'] })
      qc.invalidateQueries({ queryKey: ['evidence'] })
    },
  })
}

export function useUploadRepaymentEvidence() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ pendingRepaymentId, file }: { pendingRepaymentId: string; file: File }) => {
      const form = new FormData()
      form.append('file', file)
      return api.postForm<PaymentEvidence>(`/evidence/repayment/${pendingRepaymentId}`, form)
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['repayments'] })
      qc.invalidateQueries({ queryKey: ['evidence'] })
    },
  })
}

// ── Guarantor uploads (offline disbursement proof) ──────────────────

export function useUploadDisbursementEvidence() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ reportId, file }: { reportId: string; file: File }) => {
      const form = new FormData()
      form.append('file', file)
      return api.postForm<PaymentEvidence>(`/guarantor/evidence/disbursement/${reportId}`, form)
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['offline-disbursements'] })
      qc.invalidateQueries({ queryKey: ['guarantor', 'payments'] })
      qc.invalidateQueries({ queryKey: ['guarantor', 'evidence'] })
      qc.invalidateQueries({ queryKey: ['evidence'] })
    },
  })
}

/** Proof of payment attached to one of the member's own offline disbursements. */
export function useMemberDisbursementEvidence(reportId: string | null) {
  return useQuery({
    queryKey: ['evidence', 'disbursement', reportId],
    queryFn: () => api.get<PaymentEvidence[]>(`/evidence/disbursement/${reportId}`),
    enabled: !!reportId,
  })
}

// ── Guarantor evidence lookup ───────────────────────────────────────

export function useGuarantorContributionEvidence(contributionId: string | null) {
  return useQuery({
    queryKey: ['guarantor', 'evidence', 'contribution', contributionId],
    queryFn: () =>
      api.get<PaymentEvidence[]>(`/guarantor/evidence/contribution/${contributionId}`),
    enabled: !!contributionId,
  })
}

export function useGuarantorRepaymentEvidence(pendingRepaymentId: string | null) {
  return useQuery({
    queryKey: ['guarantor', 'evidence', 'repayment', pendingRepaymentId],
    queryFn: () => api.get<PaymentEvidence[]>(`/guarantor/evidence/repayment/${pendingRepaymentId}`),
    enabled: !!pendingRepaymentId,
  })
}