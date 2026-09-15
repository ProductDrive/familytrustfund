import type { BadgeTone } from './Badge'

const toneForStatus = new Map<string, BadgeTone>([
  ['Active', 'success'],
  ['Completed', 'success'],
  ['Confirmed', 'success'],
  ['Disbursed', 'success'],
  ['Successful', 'success'],
  ['Approved', 'success'],
  ['Paid', 'success'],
  ['FullSettlement', 'success'],
  ['Scheduled', 'neutral'],
  ['LumpSum', 'pending'],
  ['Overdue', 'danger'],
  ['Pending', 'pending'],
  ['Pending Confirmation', 'pending'],
  ['DisbursementPending', 'pending'],
  ['Unverified', 'pending'],
  ['Review', 'pending'],
  ['Inactive', 'neutral'],
  ['Transitioned', 'info'],
  ['Suspended', 'neutral'],
  ['Disabled', 'neutral'],
  ['Cancelled', 'neutral'],
  ['Rejected', 'danger'],
  ['Failed', 'danger'],
  ['Reversed', 'danger'],
  ['Defaulted', 'danger'],
  ['Family', 'info'],
  ['External', 'info'],
])

export function toneForStatusString(status: string): BadgeTone {
  return toneForStatus.get(status) ?? 'neutral'
}