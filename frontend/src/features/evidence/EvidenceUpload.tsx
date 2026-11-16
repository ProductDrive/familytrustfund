import { useState } from 'react'
import { Paperclip, UploadCloud } from 'lucide-react'
import { Button } from '../../components/ui/Button'
import {
  guarantorEvidenceUrl,
  memberEvidenceUrl,
  useUploadContributionEvidence,
  useUploadDisbursementEvidence,
  useUploadRepaymentEvidence,
} from './evidenceApi'
import type { PaymentEvidence } from './evidenceApi'

const MAX_MB = 5

interface EvidenceUploadProps {
  resourceType: 'contribution' | 'repayment' | 'disbursement'
  resourceId: string
  hasEvidence?: boolean
}

export function EvidenceUpload({
  resourceType,
  resourceId,
  hasEvidence,
}: EvidenceUploadProps) {
  const uploadContribution = useUploadContributionEvidence()
  const uploadRepayment = useUploadRepaymentEvidence()
  const uploadDisbursement = useUploadDisbursementEvidence()

  const isPending =
    resourceType === 'contribution'
      ? uploadContribution.isPending
      : resourceType === 'repayment'
        ? uploadRepayment.isPending
        : uploadDisbursement.isPending
  const error =
    resourceType === 'contribution'
      ? uploadContribution.error
      : resourceType === 'repayment'
        ? uploadRepayment.error
        : uploadDisbursement.error

  // Offline-disbursement proof is uploaded by the Guarantor and viewed through
  // the guarantor evidence endpoint; member evidence uses the member endpoint.
  const isGuarantorUpload = resourceType === 'disbursement'

  const [file, setFile] = useState<File | null>(null)
  const [uploaded, setUploaded] = useState<PaymentEvidence | null>(null)

  function submit(e: React.FormEvent) {
    e.preventDefault()
    if (!file) return
    const onSuccess = (evidence: PaymentEvidence) => {
      setUploaded(evidence)
      setFile(null)
    }
    if (resourceType === 'contribution') {
      uploadContribution.mutate({ contributionId: resourceId, file }, { onSuccess })
    } else if (resourceType === 'repayment') {
      uploadRepayment.mutate({ pendingRepaymentId: resourceId, file }, { onSuccess })
    } else {
      uploadDisbursement.mutate({ reportId: resourceId, file }, { onSuccess })
    }
  }

  return (
    <form onSubmit={submit}>
      <div className="field">
        <label htmlFor={`evidence-${resourceType}-${resourceId}`}>
          {isGuarantorUpload ? 'Proof of payment' : 'Payment evidence'}
        </label>
        <input
          id={`evidence-${resourceType}-${resourceId}`}
          type="file"
          accept="image/png,image/jpeg,application/pdf"
          onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          required
        />
        <p className="field-hint">
          {isGuarantorUpload
            ? `Upload the transfer receipt or proof that you paid the member (image or PDF, up to ${MAX_MB} MB). The member sees this before confirming receipt.`
            : `Upload a bank transfer receipt or payment proof (image or PDF, up to ${MAX_MB} MB). Evidence does not confirm payment — the Guarantor still verifies it.`}
        </p>
      </div>

      {error && <p className="form-error">{(error as Error).message}</p>}

      {uploaded ? (
        <div className="evidence-uploaded">
          <p>
            <Paperclip size={14} /> Evidence uploaded: {uploaded.originalFileName}
          </p>
          <Button
            type="button"
            variant="ghost"
            onClick={() =>
              window.open(
                isGuarantorUpload
                  ? guarantorEvidenceUrl(uploaded.id)
                  : memberEvidenceUrl(uploaded.id),
                '_blank',
              )
            }
          >
            View uploaded evidence
          </Button>
        </div>
      ) : (
        <Button type="submit" disabled={!file || isPending}>
          <UploadCloud size={16} />
          {isPending
            ? 'Uploading…'
            : hasEvidence
              ? 'Upload another file'
              : isGuarantorUpload
                ? 'Upload proof'
                : 'Upload evidence'}
        </Button>
      )}
    </form>
  )
}
