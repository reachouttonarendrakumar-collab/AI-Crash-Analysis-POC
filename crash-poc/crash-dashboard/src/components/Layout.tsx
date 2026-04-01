import { NavLink, Outlet } from 'react-router-dom';

export default function Layout() {

  return (
    <div className="app-layout">
      <aside className="sidebar">
        <div className="sidebar-brand">
          <h1>Crash Collector</h1>
          <span>POC Dashboard</span>
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
        </nav>

        <div className="sidebar-footer">
          <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>
            Connected to API
          </div>
          <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>
            Crash Collector v1.0.0-poc
          </div>
        </div>
      </aside>

      <main className="main-content">
        <Outlet />
      </main>
    </div>
  );
}
