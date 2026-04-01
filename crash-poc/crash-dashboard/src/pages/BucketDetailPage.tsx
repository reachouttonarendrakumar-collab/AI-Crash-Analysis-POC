import { useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import { getBucketById, triggerAnalysis } from '../api';
import { useFetch } from '../hooks/useFetch';
import { Loading, ErrorBox } from '../components/Loading';

function statusColor(status: string): string {
  switch (status) {
    case 'Completed': return 'var(--green)';
    case 'Failed': return 'var(--red)';
    case 'Analyzing': return 'var(--orange)';
    case 'ManualReview': return 'var(--yellow)';
    case 'Open': return 'var(--green)';
    case 'Merged': return 'var(--accent)';
    case 'NoPR': return 'var(--orange)';
    default: return 'var(--text-muted)';
  }
}

export default function BucketDetailPage() {
  const { bucketId } = useParams<{ bucketId: string }>();
  const { data, loading, error, refetch } = useFetch(
    () => getBucketById(bucketId!),
    [bucketId]
  );
  const [analyzing, setAnalyzing] = useState(false);
  const [analyzeError, setAnalyzeError] = useState('');

  if (loading) return <Loading message="Loading bucket details..." />;
  if (error) return <ErrorBox message={`Failed to load bucket: ${error}`} />;

  if (!bucketId) {
    return <ErrorBox message="No bucket ID provided" />;
  }

  const { bucket, keyFrames, crashes, aiAnalysis, aiFix } = data!;

  const handleAnalyze = async () => {
    setAnalyzing(true);
    setAnalyzeError('');
    try {
      await triggerAnalysis(bucketId);
      refetch();
    } catch (err) {
      setAnalyzeError(err instanceof Error ? err.message : 'Analysis failed');
    } finally {
      setAnalyzing(false);
    }
  };

  return (
    <>
      <Link to="/buckets" className="back-link">
        &larr; Back to Buckets
      </Link>

      {/* Bucket Summary */}
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

      {/* AI Analysis Section */}
      <div className="section">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
          <h3 className="section-title" style={{ margin: 0 }}>AI Analysis</h3>
          <button
            onClick={handleAnalyze}
            disabled={analyzing}
            style={{
              padding: '8px 16px',
              background: analyzing ? 'var(--border)' : 'var(--accent)',
              color: 'white',
              border: 'none',
              borderRadius: 'var(--radius)',
              fontSize: 13,
              fontWeight: 600,
              cursor: analyzing ? 'wait' : 'pointer',
              fontFamily: 'var(--font)',
            }}
          >
            {analyzing ? 'Analyzing...' : aiAnalysis ? 'Re-Analyze with AI' : 'Analyze with AI'}
          </button>
        </div>

        {analyzeError && (
          <div className="error-box" style={{ marginBottom: 16 }}>{analyzeError}</div>
        )}

        {aiAnalysis ? (
          <div className="detail-grid">
            <div className="detail-card">
              <h3>Analysis Result</h3>
              <div className="detail-row">
                <span className="label">Status</span>
                <span className="value">
                  <span style={{
                    display: 'inline-block', padding: '2px 8px', borderRadius: 4,
                    fontSize: 11, fontWeight: 600,
                    background: `${statusColor(aiAnalysis.status)}22`,
                    color: statusColor(aiAnalysis.status),
                  }}>
                    {aiAnalysis.status}
                  </span>
                </span>
              </div>
              <div className="detail-row">
                <span className="label">Confidence</span>
                <span className="value">
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <div style={{ width: 80, height: 8, background: 'var(--border)', borderRadius: 4, overflow: 'hidden' }}>
                      <div style={{
                        width: `${aiAnalysis.confidence * 100}%`, height: '100%',
                        background: aiAnalysis.confidence >= 0.7 ? 'var(--green)' : aiAnalysis.confidence >= 0.4 ? 'var(--orange)' : 'var(--red)',
                        borderRadius: 4,
                      }} />
                    </div>
                    <span>{(aiAnalysis.confidence * 100).toFixed(0)}%</span>
                  </div>
                </span>
              </div>
              <div className="detail-row">
                <span className="label">Affected File</span>
                <span className="value mono" style={{ fontSize: 11 }}>{aiAnalysis.affectedFile ?? '—'}</span>
              </div>
              <div className="detail-row">
                <span className="label">Affected Function</span>
                <span className="value mono" style={{ fontSize: 11 }}>{aiAnalysis.affectedFunction ?? '—'}</span>
              </div>
              <div className="detail-row">
                <span className="label">Tokens Used</span>
                <span className="value" style={{ fontSize: 11 }}>
                  {aiAnalysis.promptTokens + aiAnalysis.responseTokens} (prompt: {aiAnalysis.promptTokens}, response: {aiAnalysis.responseTokens})
                </span>
              </div>
            </div>

            <div className="detail-card">
              <h3>Root Cause</h3>
              <p style={{ fontSize: 13, lineHeight: 1.7, whiteSpace: 'pre-wrap' }}>
                {aiAnalysis.rootCause ?? 'No root cause determined.'}
              </p>
            </div>
          </div>
        ) : (
          <div className="detail-card" style={{ textAlign: 'center', padding: 40, color: 'var(--text-muted)' }}>
            No AI analysis available. Click "Analyze with AI" to get started.
          </div>
        )}

        {/* AI Fix / PR info */}
        {aiFix && (
          <div className="detail-card" style={{ marginTop: 16 }}>
            <h3>AI-Generated Fix</h3>
            <div className="detail-row">
              <span className="label">PR Status</span>
              <span className="value">
                <span style={{
                  display: 'inline-block', padding: '2px 8px', borderRadius: 4,
                  fontSize: 11, fontWeight: 600,
                  background: `${statusColor(aiFix.prStatus)}22`,
                  color: statusColor(aiFix.prStatus),
                }}>
                  {aiFix.prStatus}
                </span>
              </span>
            </div>
            {aiFix.branchName && (
              <div className="detail-row">
                <span className="label">Branch</span>
                <span className="value mono" style={{ fontSize: 11 }}>{aiFix.branchName}</span>
              </div>
            )}
            {aiFix.prUrl && (
              <div className="detail-row">
                <span className="label">Pull Request</span>
                <span className="value">
                  <a href={aiFix.prUrl} target="_blank" rel="noopener noreferrer">
                    PR #{aiFix.prNumber} &rarr;
                  </a>
                </span>
              </div>
            )}
            {aiFix.fixDescription && (
              <div style={{ marginTop: 12 }}>
                <span className="label" style={{ display: 'block', marginBottom: 6 }}>Fix Description</span>
                <p style={{ fontSize: 13, lineHeight: 1.7, color: 'var(--text)' }}>{aiFix.fixDescription}</p>
              </div>
            )}
          </div>
        )}

        {/* Suggested Fix Code */}
        {aiAnalysis?.suggestedFix && (
          <div style={{ marginTop: 16 }}>
            <h3 className="section-title">Suggested Fix</h3>
            <div className="stack-trace">
              <pre style={{ margin: 0, fontFamily: 'var(--mono)', fontSize: 12, lineHeight: 1.7, whiteSpace: 'pre-wrap' }}>
                {aiAnalysis.suggestedFix}
              </pre>
            </div>
          </div>
        )}
      </div>

      {/* Key Stack Frames */}
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

      {/* Crashes in This Bucket */}
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
