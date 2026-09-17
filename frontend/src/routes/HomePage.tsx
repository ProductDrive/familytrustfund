import { useCurrentUser } from '../features/auth/authApi'
import { MembershipDashboard } from '../features/membership/MembershipDashboard'
import { GuarantorDashboardPage } from '../features/dashboard/GuarantorDashboardPage'

export function HomePage() {
  const { data: user } = useCurrentUser()

  if (user?.roles.includes('Guarantor')) {
    return <GuarantorDashboardPage />
  }

  // Super Admins get a separate front-end app later; the dashboard below is
  // the fallback until then.
  return <MembershipDashboard />
}