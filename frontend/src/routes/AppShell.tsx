import { useState, type ReactNode } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import { Landmark, LogOut, Menu, Users, LayoutDashboard, HandCoins, PiggyBank, Send, Wallet, BadgeCheck } from 'lucide-react'
import { useCurrentUser, type Role } from '../features/auth/authApi'
import { Avatar, LogoMark } from '../components/ui/Avatar'
import { ThemeToggle } from '../components/ui/ThemeToggle'
import { Badge } from '../components/ui/Badge'

function useGreeting() {
  const hour = new Date().getHours()
  if (hour < 12) return 'Good morning'
  if (hour < 17) return 'Good afternoon'
  return 'Good evening'
}

const NavItem = ({
  to,
  icon,
  label,
  onNavigate,
}: {
  to: string
  icon: ReactNode
  label: string
  onNavigate: () => void
}) => (
  <NavLink
    to={to}
    className={({ isActive }) =>
      isActive ? 'nav-link active' : 'nav-link'
    }
    onClick={onNavigate}
  >
    {icon}
    {label}
  </NavLink>
)

export function AppShell() {
  const { data: user } = useCurrentUser()
  const [menuOpen, setMenuOpen] = useState(false)
  const closeMenu = () => setMenuOpen(false)
  const greeting = useGreeting()

  const isGuarantor = user?.roles.includes('Guarantor')
  const isAdmin = user?.roles.includes('SuperAdmin')
  const isMember = user?.roles.includes('Member')
  const firstName = user?.displayName.split(' ')[0] ?? 'there'

  const roleLabel: Record<Role, string> = {
    SuperAdmin: 'Super Admin',
    Guarantor: 'Guarantor',
    Member: 'Member',
  }
  const primaryRole: Role =
    user?.roles.find((r) => r === 'Guarantor') ??
    user?.roles.find((r) => r === 'SuperAdmin') ??
    'Member'

  return (
    <div className="app-shell">
      {menuOpen && (
        <div className="sidebar-backdrop" onClick={closeMenu} aria-hidden="true" />
      )}

      <aside className={menuOpen ? 'sidebar sidebar--open' : 'sidebar'}>
        <div className="sidebar-logo">
          <LogoMark />
          <div>
            <div className="logo-name">Family Trust Fund</div>
            <div className="logo-sub">Guarantor-led lending</div>
          </div>
        </div>

        <nav className="sidebar-scroll" aria-label="Primary navigation">
          <div className="nav-group-label">Overview</div>
          <NavItem
            to="/"
            icon={<LayoutDashboard size={17} />}
            label="Dashboard"
            onNavigate={closeMenu}
          />

          <div className="nav-group-label">Lending</div>
          {isMember && (
            <NavItem
              to="/loans"
              icon={<HandCoins size={17} />}
              label="Loans"
              onNavigate={closeMenu}
            />
          )}
          {isMember && (
            <NavItem
              to="/contributions"
              icon={<PiggyBank size={17} />}
              label="Contributions"
              onNavigate={closeMenu}
            />
          )}
          {isMember && (
            <NavItem
              to="/repayments"
              icon={<Wallet size={17} />}
              label="Repayments"
              onNavigate={closeMenu}
            />
          )}
          {isGuarantor && (
            <NavItem
              to="/guarantor/loans"
              icon={<HandCoins size={17} />}
              label="Loan Requests"
              onNavigate={closeMenu}
            />
          )}
          {isGuarantor && (
            <NavItem
              to="/guarantor/contributions"
              icon={<PiggyBank size={17} />}
              label="Contribution Confirmations"
              onNavigate={closeMenu}
            />
          )}
          {isGuarantor && (
            <NavItem
              to="/guarantor/disburse"
              icon={<Send size={17} />}
              label="Disbursements"
              onNavigate={closeMenu}
            />
          )}
          {isGuarantor && (
            <NavItem
              to="/guarantor/repayments"
              icon={<BadgeCheck size={17} />}
              label="Repayment Confirmations"
              onNavigate={closeMenu}
            />
          )}

          <div className="nav-group-label">Management</div>
          {(isGuarantor || isAdmin) && (
            <NavItem
              to="/funds"
              icon={<Landmark size={17} />}
              label="Fund Management"
              onNavigate={closeMenu}
            />
          )}
          {isAdmin && (
            <NavItem
              to="/admin/memberships"
              icon={<Users size={17} />}
              label="Memberships"
              onNavigate={closeMenu}
            />
          )}
        </nav>

        <div className="sidebar-footer">
          <div className="user-chip">
            <Avatar name={user?.displayName ?? 'User'} />
            <div style={{ minWidth: 0 }}>
              <div className="user-chip-name">
                {user?.displayName ?? user?.email ?? 'User'}
              </div>
              <div className="user-chip-role">{roleLabel[primaryRole]}</div>
            </div>
          </div>
          <a className="logout-link" href="/api/auth/logout">
            <LogOut size={16} />
            Log out
          </a>
        </div>
      </aside>

      <div className="main-region">
        <header className="topbar">
          <button
            type="button"
            className="icon-btn sidebar-toggle"
            aria-label="Open menu"
            onClick={() => setMenuOpen(true)}
          >
            <Menu size={18} />
          </button>
          <Badge tone="info">{roleLabel[primaryRole]}</Badge>
          <span className="topbar-greeting">
            {greeting}, {firstName} — here&apos;s what&apos;s happening with your
            funds today.
          </span>
          <span className="topbar-spacer" />
          <ThemeToggle />
          <Avatar name={user?.displayName ?? 'User'} size="lg" />
        </header>

        <main className="content">
          <Outlet />
        </main>
      </div>
    </div>
  )
}