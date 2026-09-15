import { useState } from 'react'
import { Eye, FileText } from 'lucide-react'
import { Button } from '../../components/ui/Button'
import {
  guarantorEvidenceUrl,
  useGuarantorContributionEvidence,
  useGuarantorRepaymentEvidence,
} from './evidenceApi'
import { formatShortDateTime } from '../../lib/formatDate'

interface GuarantorEvidenceViewProps {
  kind: 'contribution' | 'repayment'
  resourceId: string
  hasEvidence: boolean
}

export function GuarantorEvidenceView({
  kind,
  resourceId,
  hasEvidence,
}: GuarantorEvidenceViewProps) {
  const contributionQuery = useGuarantorContributionEvidence(
    kind === 'contribution' && hasEvidence ? resourceId : null,
  )
  const repaymentQuery = useGuarantorRepaymentEvidence(
    kind === 'repayment' && hasEvidence ? resourceId : null,
  )
  const query = kind === 'contribution' ? contributionQuery : repaymentQuery
  const [open, setOpen] = useState(false)

  if (!hasEvidence) return <span className="cell-secondary">No evidence</span>

  return open ? (
    <div className="evidence-list">
      {query.isLoading && <p className="muted">Loading evidence…</p>}
      {query.data && query.data.length === 0 && (
        <p className="muted">No evidence uploaded.</p>
      )}
      {query.data?.map((e) => (
        <p key={e.id}>
          <FileText size={14} />
          <a href={guarantorEvidenceUrl(e.id)} target="_blank" rel="noreferrer">
            {e.originalFileName}
          </a>
          <span className="cell-secondary">
            {' '}
            ({formatShortDateTime(e.uploadedAtUtc)})
          </span>
        </p>
      ))}
    </div>
  ) : (
    <Button type="button" variant="secondary" onClick={() => setOpen(true)}>
      <Eye size={16} /> View evidence
    </Button>
  )
}