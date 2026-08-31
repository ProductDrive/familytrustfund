import type { BadgeTone } from './Badge'

const toneForStatus = new Map<string, BadgeTone>([
  ['Active', 'success'],
  ['Completed', 'success'],
  ['Confirmed', 'success'],
  ['Disbursed', 'success'],
  ['Pending', 'pending'],
  ['Pending Confirmation', 'pending'],
  ['Review', 'pending'],
  ['Inactive', 'neutral'],
  ['Suspended', 'neutral'],
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