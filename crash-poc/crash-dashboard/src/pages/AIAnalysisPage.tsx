import { useNavigate } from 'react-router-dom';
import { getAIAnalyses, getBuckets } from '../api';
import { useFetch } from '../hooks/useFetch';
import { Loading, ErrorBox, EmptyState } from '../components/Loading';

function statusColor(status: string): string {
  switch (status) {
    case 'Completed': return 'var(--green)';
    case 'Failed': return 'var(--red)';
    case 'Analyzing': return 'var(--orange)';
    case 'ManualReview': return 'var(--yellow)';
    default: return 'var(--text-muted)';
  }
}

export default function AIAnalysisPage() {
  const navigate = useNavigate();
  const analyses = useFetch(() => getAIAnalyses(100), []);
  const buckets = useFetch(() => getBuckets(100), []);

  if (analyses.loading || buckets.loading) return <Loading message="Loading AI analyses..." />;
  if (analyses.error) return <ErrorBox message={`Failed to load analyses: ${analyses.error}`} />;

  const items = analyses.data?.analyses ?? [];
  const bucketMap = new Map(
    (buckets.data?.buckets ?? []).map(b => [b.bucketId, b])
  );

  if (items.length === 0) {
    return (
      <EmptyState
        title="No AI Analyses Yet"
        message="No crash buckets have been analyzed by AI yet. Go to a bucket detail page and trigger an analysis."
        action={{ label: 'View Buckets', onClick: () => navigate('/buckets') }}
      />
    );
  }

  const completed = items.filter(a => a.status === 'Completed').length;
  const failed = items.filter(a => a.status === 'Failed').length;
  const manual = items.filter(a => a.status === 'ManualReview').length;
  const avgConfidence = items.filter(a => a.confidence > 0).reduce((s, a) => s + a.confidence, 0)
    / Math.max(items.filter(a => a.confidence > 0).length, 1);

  return (
    <>
      <div className="page-header">
        <h2>AI Analysis</h2>
        <p>{items.length} bucket(s) analyzed</p>
      </div>

      <div className="stats-grid">
        <div className="stat-card">
          <div className="stat-label">Completed</div>
          <div className="stat-value" style={{ color: 'var(--green)' }}>{completed}</div>
        </div>
        <div className="stat-card">
          <div className="stat-label">Manual Review</div>
          <div className="stat-value" style={{ color: 'var(--yellow)' }}>{manual}</div>
        </div>
        <div className="stat-card">
          <div className="stat-label">Failed</div>
          <div className="stat-value" style={{ color: 'var(--red)' }}>{failed}</div>
        </div>
        <div className="stat-card">
          <div className="stat-label">Avg Confidence</div>
          <div className="stat-value">{(avgConfidence * 100).toFixed(0)}%</div>
        </div>
      </div>

      <div className="table-container">
        <table>
          <thead>
            <tr>
              <th>Bucket</th>
              <th>Exception</th>
              <th>Status</th>
              <th>Confidence</th>
              <th>Root Cause</th>
              <th>Affected</th>
              <th>Analyzed</th>
            </tr>
          </thead>
          <tbody>
            {items.map((a) => {
              const bucket = bucketMap.get(a.bucketId);
              return (
                <tr key={a.analysisId} onClick={() => navigate(`/buckets/${a.bucketId}`)} style={{ cursor: 'pointer' }}>
                  <td className="mono" style={{ fontSize: 11 }}>{a.bucketId.slice(0, 12)}...</td>
                  <td>
                    {bucket?.exceptionCode ? (
                      <span className="badge badge-exception">{bucket.exceptionCode}</span>
                    ) : <span style={{ color: 'var(--text-muted)' }}>n/a</span>}
                  </td>
                  <td>
                    <span style={{
                      display: 'inline-block', padding: '2px 8px', borderRadius: 4,
                      fontSize: 11, fontWeight: 600,
                      background: `${statusColor(a.status)}22`,
                      color: statusColor(a.status),
                    }}>
                      {a.status}
                    </span>
                  </td>
                  <td>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                      <div style={{
                        width: 50, height: 6, background: 'var(--border)', borderRadius: 3, overflow: 'hidden',
                      }}>
                        <div style={{
                          width: `${a.confidence * 100}%`, height: '100%',
                          background: a.confidence >= 0.7 ? 'var(--green)' : a.confidence >= 0.4 ? 'var(--orange)' : 'var(--red)',
                          borderRadius: 3,
                        }} />
                      </div>
                      <span style={{ fontSize: 11 }}>{(a.confidence * 100).toFixed(0)}%</span>
                    </div>
                  </td>
                  <td className="truncate" style={{ maxWidth: 250, fontSize: 12 }}>
                    {a.rootCause?.slice(0, 80) ?? <span style={{ color: 'var(--text-muted)' }}>—</span>}
                  </td>
                  <td className="mono" style={{ fontSize: 11 }}>
                    {a.affectedFunction ?? '—'}
                  </td>
                  <td style={{ whiteSpace: 'nowrap', fontSize: 12 }}>
                    {a.completedAtUtc ? new Date(a.completedAtUtc).toLocaleString() : '—'}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </>
  );
}
