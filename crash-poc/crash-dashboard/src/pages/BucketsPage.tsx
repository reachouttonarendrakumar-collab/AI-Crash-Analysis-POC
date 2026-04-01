import { useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { getBuckets } from '../api';
import { useFetch } from '../hooks/useFetch';
import { Loading, ErrorBox, EmptyState } from '../components/Loading';

export default function BucketsPage() {
  const navigate = useNavigate();
  const { data, loading, error } = useFetch(() => getBuckets(100), []);

  // Memoized sorting - always called, even if data is still loading
  const sortedBuckets = useMemo(() => {
    if (!data) return [];
    return data.buckets.slice().sort((a, b) => b.crashCount - a.crashCount);
  }, [data]);

  // Early returns after all hooks are called
  if (loading) return <Loading message="Loading buckets..." />;
  if (error) return <ErrorBox message={`Failed to load buckets: ${error}`} />;

  const buckets = data!.buckets;

  if (buckets.length === 0) {
    return (
      <EmptyState
        title="No Buckets Found"
        message="No crash buckets have been created yet. Buckets are created when crashes are processed and grouped by stack signature."
        action={{
          label: "View Crashes",
          onClick: () => navigate('/crashes')
        }}
      />
    );
  }

  return (
    <>
      <div className="page-header">
        <h2>Buckets</h2>
        <p>{data!.count} unique crash signature(s)</p>
      </div>

      <div className="table-container">
        <table>
          <thead>
            <tr>
              <th>Bucket ID</th>
              <th>Exception Code</th>
              <th>Key Frame 1</th>
              <th>Crash Count</th>
              <th>AI Status</th>
              <th>Fix</th>
              <th>First Seen</th>
            </tr>
          </thead>
          <tbody>
            {sortedBuckets.map((b) => (
              <tr 
                key={b.bucketId}
                onClick={() => navigate(`/buckets/${b.bucketId}`)}
                style={{ cursor: 'pointer' }}
              >
                <td>
                  <span className="mono" style={{ fontSize: 12 }}>
                    {b.bucketId.slice(0, 12)}...
                  </span>
                </td>
                <td>
                  {b.exceptionCode ? (
                    <span className="badge badge-exception">
                      {b.exceptionCode}
                    </span>
                  ) : (
                    <span style={{ color: 'var(--text-muted)' }}>n/a</span>
                  )}
                </td>
                <td className="truncate mono" style={{ fontSize: 12, maxWidth: 250 }}>
                  {b.keyFrame1 ?? 'Unknown'}
                </td>
                <td style={{ textAlign: 'center' }}>
                  <span className="badge badge-count">{b.crashCount}</span>
                </td>
                <td>
                  {b.aiStatus ? (
                    <span style={{
                      display: 'inline-block', padding: '2px 8px', borderRadius: 4,
                      fontSize: 11, fontWeight: 600,
                      background: b.aiStatus === 'Completed' ? 'rgba(80,200,120,0.15)' :
                                  b.aiStatus === 'Failed' ? 'rgba(240,83,101,0.15)' :
                                  'rgba(255,180,50,0.15)',
                      color: b.aiStatus === 'Completed' ? 'var(--green)' :
                             b.aiStatus === 'Failed' ? 'var(--red)' : 'var(--orange)',
                    }}>
                      {b.aiStatus}
                      {b.aiConfidence != null && b.aiConfidence > 0 ? ` ${(b.aiConfidence * 100).toFixed(0)}%` : ''}
                    </span>
                  ) : (
                    <span style={{ color: 'var(--text-muted)', fontSize: 11 }}>Not analyzed</span>
                  )}
                </td>
                <td>
                  {b.prUrl ? (
                    <a href={b.prUrl} target="_blank" rel="noopener noreferrer"
                       onClick={(e) => e.stopPropagation()}
                       style={{ fontSize: 11 }}>
                      {b.fixStatus ?? 'PR'}
                    </a>
                  ) : b.fixStatus ? (
                    <span style={{ fontSize: 11, color: 'var(--text-muted)' }}>{b.fixStatus}</span>
                  ) : (
                    <span style={{ color: 'var(--text-muted)', fontSize: 11 }}>—</span>
                  )}
                </td>
                <td style={{ whiteSpace: 'nowrap' }}>
                  {new Date(b.firstSeenUtc).toLocaleDateString()}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}
