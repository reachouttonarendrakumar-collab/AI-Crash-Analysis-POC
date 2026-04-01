export function Loading({ message = 'Loading...' }: { message?: string }) {
  return (
    <div className="loading">
      <div className="spinner" />
      {message}
    </div>
  );
}

export function ErrorBox({ message }: { message: string }) {
  return <div className="error-box">{message}</div>;
}

export function EmptyState({ 
  title, 
  message, 
  action 
}: { 
  title: string; 
  message: string; 
  action?: { label: string; onClick: () => void };
}) {
  return (
    <div className="loading" style={{ flexDirection: 'column', gap: 16 }}>
      <div style={{ textAlign: 'center' }}>
        <div style={{ fontSize: 48, opacity: 0.3 }}>📊</div>
        <h3 style={{ margin: 0, color: 'var(--text)' }}>{title}</h3>
        <p style={{ margin: 0, color: 'var(--text-muted)' }}>{message}</p>
      </div>
      {action && (
        <button
          onClick={action.onClick}
          style={{
            padding: '8px 16px',
            backgroundColor: 'var(--accent)',
            color: 'white',
            border: 'none',
            borderRadius: 'var(--radius)',
            fontSize: 13,
            cursor: 'pointer',
          }}
        >
          {action.label}
        </button>
      )}
    </div>
  );
}
