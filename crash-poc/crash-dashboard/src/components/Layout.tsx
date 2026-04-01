import { NavLink, Outlet, useNavigate } from 'react-router-dom';

export default function Layout() {
  const navigate = useNavigate();
  const userJson = localStorage.getItem('auth_user');
  const user = userJson ? JSON.parse(userJson) : null;

  const handleLogout = () => {
    localStorage.removeItem('auth_token');
    localStorage.removeItem('auth_user');
    navigate('/login');
  };

  return (
    <div className="app-layout">
      <aside className="sidebar">
        <div className="sidebar-brand">
          <h1>Crash Analysis</h1>
          <span>AI-Powered Dashboard</span>
        </div>

        <nav className="sidebar-nav">
          <NavLink 
            to="/" 
            end
            className={({ isActive }) => isActive ? 'active' : ''}
          >
            <span className="nav-icon">&#9632;</span>
            <span>Overview</span>
          </NavLink>
          <NavLink 
            to="/buckets"
            className={({ isActive }) => isActive ? 'active' : ''}
          >
            <span className="nav-icon">&#9638;</span>
            <span>Buckets</span>
          </NavLink>
          <NavLink 
            to="/crashes"
            className={({ isActive }) => isActive ? 'active' : ''}
          >
            <span className="nav-icon">&#9888;</span>
            <span>Crashes</span>
          </NavLink>

          <div style={{ height: 1, background: 'var(--border)', margin: '8px 12px' }} />

          <NavLink 
            to="/ai-analysis"
            className={({ isActive }) => isActive ? 'active' : ''}
          >
            <span className="nav-icon">&#9883;</span>
            <span>AI Analysis</span>
          </NavLink>
          <NavLink 
            to="/fixes"
            className={({ isActive }) => isActive ? 'active' : ''}
          >
            <span className="nav-icon">&#10003;</span>
            <span>Fixes & PRs</span>
          </NavLink>
        </nav>

        <div className="sidebar-footer">
          {user && (
            <div style={{ marginBottom: 8 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 4 }}>
                <div style={{
                  width: 28, height: 28, borderRadius: '50%',
                  background: 'var(--accent)', display: 'flex',
                  alignItems: 'center', justifyContent: 'center',
                  fontSize: 12, fontWeight: 700, color: 'white',
                }}>
                  {user.name?.[0] ?? 'D'}
                </div>
                <div>
                  <div style={{ fontSize: 12, fontWeight: 600 }}>{user.name}</div>
                  <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>{user.email}</div>
                </div>
              </div>
              <div style={{ fontSize: 10, color: 'var(--text-muted)', marginBottom: 6 }}>
                App: {user.application}
              </div>
              <button
                onClick={handleLogout}
                style={{
                  width: '100%', padding: '4px 0', background: 'transparent',
                  border: '1px solid var(--border)', borderRadius: 4,
                  color: 'var(--text-muted)', fontSize: 11, cursor: 'pointer',
                  fontFamily: 'var(--font)',
                }}
              >
                Sign Out
              </button>
            </div>
          )}
          <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>
            Crash Collector v2.0.0-poc
          </div>
        </div>
      </aside>

      <main className="main-content">
        <Outlet />
      </main>
    </div>
  );
}
