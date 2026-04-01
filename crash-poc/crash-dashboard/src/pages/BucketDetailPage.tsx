import { useParams, Link } from 'react-router-dom';
import { getBucketById } from '../api';
import { useFetch } from '../hooks/useFetch';
import { Loading, ErrorBox, EmptyState } from '../components/Loading';

export default function BucketDetailPage() {
  const { bucketId } = useParams<{ bucketId: string }>();
  const { data, loading, error } = useFetch(
    () => getBucketById(bucketId!),
    [bucketId]
  );

  if (loading) return <Loading message="Loading bucket details..." />;
  if (error) return <ErrorBox message={`Failed to load bucket: ${error}`} />;

  if (!bucketId) {
    return <ErrorBox message="No bucket ID provided" />;
  }

  const { bucket, keyFrames, crashes } = data!;

  return (
    <>
      <Link to="/buckets" className="back-link">
        &larr; Back to Buckets
      </Link>

      {/* Bucket Summary - Top Priority */}
      <div className="section">
        <h3 className="section-title">Bucket Summary</h3>
        <div className="detail-grid">
          <div className="detail-card">
            <h3>Identification</h3>
            <div className="detail-row">
              <span className="label">Bucket ID</span>
              <span className="value mono" style={{ fontSize: 10 }}>
                {bucket.bucketId}
              </span>
            </div>
            <div className="detail-row">
              <span className="label">Exception Code</span>
              <span className="value">
                {bucket.exceptionCode ? (
                  <span className="badge badge-exception">
                    {bucket.exceptionCode}
                  </span>
                ) : (
                  <span style={{ color: 'var(--text-muted)' }}>n/a</span>
                )}
              </span>
            </div>
          </div>

          <div className="detail-card">
            <h3>Impact</h3>
            <div className="detail-row">
              <span className="label">Total Crashes</span>
              <span className="value">
                <span className="badge badge-count">{bucket.crashCount}</span>
              </span>
            </div>
            <div className="detail-row">
              <span className="label">First Seen</span>
              <span className="value">
                {new Date(bucket.firstSeenUtc).toLocaleString()}
              </span>
            </div>
            <div className="detail-row">
              <span className="label">Last Seen</span>
              <span className="value">
                {new Date(bucket.lastSeenUtc).toLocaleString()}
              </span>
            </div>
          </div>
        </div>
      </div>

      {/* Key Stack Frames - Signature */}
      <div className="section">
        <h3 className="section-title">Stack Signature (Key Frames)</h3>
        <div className="table-container">
          <table>
            <thead>
              <tr>
                <th style={{ width: '50px' }}>#</th>
                <th>Key Frame</th>
              </tr>
            </thead>
            <tbody>
              {keyFrames.length > 0 ? (
                keyFrames.map((kf, i) => (
                  <tr key={i}>
                    <td style={{ textAlign: 'center', color: 'var(--accent)' }}>
                      {i + 1}
                    </td>
                    <td className="mono" style={{ fontSize: 13, color: 'var(--green)' }}>
                      {kf}
                    </td>
                  </tr>
                ))
              ) : (
                <tr>
                  <td colSpan={2} style={{ textAlign: 'center', color: 'var(--text-muted)' }}>
                    No key frames available
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Crashes in This Bucket - Developer Triage */}
      <div className="section">
        <h3 className="section-title">
          Crashes in This Bucket ({crashes.length})
        </h3>
        <div className="table-container">
          <table>
            <thead>
              <tr>
                <th>Crash ID</th>
                <th>Timestamp</th>
                <th>Exception Code</th>
                <th>App Version</th>
              </tr>
            </thead>
            <tbody>
              {crashes.map((c) => (
                <tr key={c.crashId}>
                  <td>
                    <Link to={`/crashes/${c.crashId}`} className="mono">
                      {c.crashId.slice(0, 24)}...
                    </Link>
                  </td>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    {new Date(c.timestamp).toLocaleString()}
                  </td>
                  <td>
                    {c.exceptionCode ? (
                      <span className="badge badge-exception">
                        {c.exceptionCode}
                      </span>
                    ) : (
                      <span style={{ color: 'var(--text-muted)' }}>n/a</span>
                    )}
                  </td>
                  <td className="mono" style={{ fontSize: 12 }}>
                    {c.appVersion}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </>
  );
}
