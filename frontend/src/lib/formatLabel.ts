// Presents enum member names as human-readable labels, inserting spaces at
// camelCase boundaries. "DisbursementPending" -> "Disbursement Pending",
// "LumpSum" -> "Lump Sum", "PendingConfirmation" -> "Pending Confirmation".
export function formatEnumLabel(value: string): string {
  return value.replace(/([a-z0-9])([A-Z])/g, '$1 $2')
}