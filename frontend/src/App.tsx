import { Navigate, Outlet, Route, Routes } from 'react-router-dom'
import { useIsAuthenticated, type Role } from './features/auth/authApi'
import { AppShell } from './routes/AppShell'
import { HomePage } from './routes/HomePage'
import { LoginPage } from './routes/LoginPage'
import { FundsPage } from './features/funds/FundsPage'
import { MemberLoansPage } from './features/loans/MemberLoansPage'
import { GuarantorLoansPage } from './features/loans/GuarantorLoansPage'
import { MemberContributionsPage } from './features/contributions/MemberContributionsPage'
import { GuarantorContributionsPage } from './features/contributions/GuarantorContributionsPage'
import { GuarantorDisbursePage } from './features/payments/GuarantorDisbursePage'
import { MemberRepaymentPage } from './features/repayments/MemberRepaymentPage'
import { GuarantorRepaymentsPage } from './features/repayments/GuarantorRepaymentsPage'
import { AdminMembershipsPage } from './features/administration/AdminMembershipsPage'
import { NotAuthorizedPage } from './routes/NotAuthorizedPage'
import { LoadingState } from './components/ui/State'

function RequireAuth() {
  const { isLoading, isAuthenticated } = useIsAuthenticated()
  if (isLoading) return <FullPageLoader />
  if (!isAuthenticated) return <Navigate to="/login" replace />
  return <Outlet />
}

function RequireRole({ roles, children }: { roles: Role[]; children: React.ReactNode }) {
  const { isLoading, user } = useIsAuthenticated()
  if (isLoading) return <FullPageLoader />
  if (!user) return <Navigate to="/login" replace />
  const allowed = user.roles.some((r) => roles.includes(r))
  if (!allowed) return <NotAuthorizedPage />
  return <>{children}</>
}

export function FullPageLoader() {
  return (
    <div style={{ padding: '24px 48px' }}>
      <LoadingState label="Loading application…" lines={4} />
    </div>
  )
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<RequireAuth />}>
        <Route element={<AppShell />}>
          <Route index element={<HomePage />} />
          <Route
            path="funds"
            element={
              <RequireRole roles={['Guarantor', 'SuperAdmin']}>
                <FundsPage />
              </RequireRole>
            }
          />
          <Route
            path="loans"
            element={
              <RequireRole roles={['Member']}>
                <MemberLoansPage />
              </RequireRole>
            }
          />
          <Route
            path="repayments"
            element={
              <RequireRole roles={['Member']}>
                <MemberRepaymentPage />
              </RequireRole>
            }
          />
          <Route
            path="contributions"
            element={
              <RequireRole roles={['Member']}>
                <MemberContributionsPage />
              </RequireRole>
            }
          />
          <Route
            path="guarantor/loans"
            element={
              <RequireRole roles={['Guarantor']}>
                <GuarantorLoansPage />
              </RequireRole>
            }
          />
          <Route
            path="guarantor/contributions"
            element={
              <RequireRole roles={['Guarantor']}>
                <GuarantorContributionsPage />
              </RequireRole>
            }
          />
          <Route
            path="guarantor/disburse"
            element={
              <RequireRole roles={['Guarantor']}>
                <GuarantorDisbursePage />
              </RequireRole>
            }
          />
          <Route
            path="guarantor/repayments"
            element={
              <RequireRole roles={['Guarantor']}>
                <GuarantorRepaymentsPage />
              </RequireRole>
            }
          />
          <Route
            path="admin/memberships"
            element={
              <RequireRole roles={['SuperAdmin']}>
                <AdminMembershipsPage />
              </RequireRole>
            }
          />
        </Route>
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
