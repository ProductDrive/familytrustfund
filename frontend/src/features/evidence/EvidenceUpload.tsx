import { useState } from 'react'
import { Paperclip, UploadCloud } from 'lucide-react'
import { Button } from '../../components/ui/Button'
import {
  memberEvidenceUrl,
  useUploadContributionEvidence,
  useUploadRepaymentEvidence,
} from './evidenceApi'
import type { PaymentEvidence } from './evidenceApi'

const MAX_MB = 5

interface EvidenceUploadProps {
  resourceType: 'contribution' | 'repayment'
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
  const isPending =
    resourceType === 'contribution'
      ? uploadContribution.isPending
      : uploadRepayment.isPending
  const error =
    resourceType === 'contribution'
      ? uploadContribution.error
      : uploadRepayment.error

  const [file, setFile] = useState<File | null>(null)
  const [uploaded, setUploaded] = useState<PaymentEvidence | null>(null)

  function submit(e: React.FormEvent) {
    e.preventDefault()
    if (!file) return
    const args = { file }
    if (resourceType === 'contribution') {
      uploadContribution.mutate(
        { contributionId: resourceId, ...args },
        { onSuccess: (evidence) => { setUploaded(evidence); setFile(null) } },
      )
    } else {
      uploadRepayment.mutate(
        { pendingRepaymentId: resourceId, ...args },
        { onSuccess: (evidence) => { setUploaded(evidence); setFile(null) } },
      )
    }
  }

  return (
    <form onSubmit={submit}>
      <div className="field">
        <label htmlFor={`evidence-${resourceType}-${resourceId}`}>Payment evidence</label>
        <input
          id={`evidence-${resourceType}-${resourceId}`}
          type="file"
          accept="image/png,image/jpeg,application/pdf"
          onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          required
        />
        <p className="field-hint">
          Upload a bank transfer receipt or payment proof (image or PDF, up to {MAX_MB} MB).
          Evidence does not confirm payment — the Guarantor still verifies it.
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
            onClick={() => window.open(memberEvidenceUrl(uploaded.id), '_blank')}
          >
            View uploaded evidence
          </Button>
        </div>
      ) : (
        <Button type="submit" disabled={!file || isPending}>
          <UploadCloud size={16} />
          {isPending ? 'Uploading…' : hasEvidence ? 'Upload another file' : 'Upload evidence'}
        </Button>
      )}
    </form>
  )
}