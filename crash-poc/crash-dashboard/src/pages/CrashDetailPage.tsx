import { useParams, Link } from 'react-router-dom';
import { getCrashById } from '../api';
import { useFetch } from '../hooks/useFetch';
import { Loading, ErrorBox, EmptyState } from '../components/Loading';

const SYSTEM_PREFIXES = ['coreclr!', 'ntdll!', 'kernel32!', 'KERNELBASE!', 'System.Private.CoreLib!'];

function isSystemFrame(frame: string): boolean {
  return SYSTEM_PREFIXES.some((p) => frame.startsWith(p));
}

export default function CrashDetailPage() {
  const { id } = useParams<{ id: string }>();
  const { data, loading, error } = useFetch(
    () => getCrashById(id!),
    [id]
  );

  if (loading) return <Loading message="Loading crash details..." />;
  if (error) return <ErrorBox message={`Failed to load crash: ${error}`} />;

  if (!id) {
    return <ErrorBox message="No crash ID provided" />;
  }

  const { crash, bucketId, bucket, symbolicatedStack } = data!;

  const keyFrames = bucket
    ? [bucket.keyFrame1, bucket.keyFrame2, bucket.keyFrame3].filter(Boolean)
    : [];

  // Action handlers (no backend logic)
  const handleCopyStackTrace = () => {
    if (symbolicatedStack) {
      const stackText = symbolicatedStack.join('\n');
      navigator.clipboard.writeText(stackText).then(() => {
        // Could add a toast notification here
        console.log('Stack trace copied to clipboard');
      });
    }
  };

  const handleDownloadDump = () => {
    // Stub for download functionality
    if (crash.dumpPath) {
      console.log('Download dump:', crash.dumpPath);
      // In real implementation, this would trigger a file download
      alert('Download functionality not implemented in POC');
    }
  };

  return (
    <>
      <Link to="/crashes" className="back-link">
        &larr; Back to Crashes
      </Link>

      {/* 1. Crash Metadata */}
      <div className="section">
        <h3 className="section-title">Crash Metadata</h3>
        <div className="detail-grid">
          <div className="detail-card">
            <h3>Identification</h3>
            <div className="detail-row">
              <span className="label">Crash ID</span>
              <span className="value mono" style={{ fontSize: 11 }}>
                {crash.crashId}
              </span>
            </div>
            <div className="detail-row">
              <span className="label">Exception Code</span>
              <span className="value">
                {crash.exceptionCode ? (
                  <span className="badge badge-exception">
                    {crash.exceptionCode}
                  </span>
                ) : (
                  <span style={{ color: 'var(--text-muted)' }}>n/a</span>
                )}
              </span>
            </div>
            <div className="detail-row">
              <span className="label">Exception Name</span>
              <span className="value">{crash.exceptionName ?? 'Unknown'}</span>
            </div>
            <div className="detail-row">
              <span className="label">Faulting Module</span>
              <span className="value mono">{crash.faultingModule ?? 'n/a'}</span>
            </div>
          </div>

          <div className="detail-card">
            <h3>Runtime Info</h3>
            <div className="detail-row">
              <span className="label">Thread Count</span>
              <span className="value">{crash.threadCount ?? 'n/a'}</span>
            </div>
            <div className="detail-row">
              <span className="label">Application</span>
              <span className="value">{crash.appName}</span>
            </div>
            <div className="detail-row">
              <span className="label">Version</span>
              <span className="value mono">{crash.appVersion}</span>
            </div>
            <div className="detail-row">
              <span className="label">Timestamp</span>
              <span className="value">
                {new Date(crash.timestamp).toLocaleString()}
              </span>
            </div>
          </div>
        </div>
      </div>

      {/* 2. Bucket Information */}
      <div className="section">
        <h3 className="section-title">Bucket Information</h3>
        <div className="detail-grid">
          <div className="detail-card">
            <h3>Bucket Details</h3>
            {bucket ? (
              <>
                <div className="detail-row">
                  <span className="label">Bucket ID</span>
                  <span className="value mono" style={{ fontSize: 10 }}>
                    <Link to={`/buckets/${bucketId}`}>
                      {bucketId}
                    </Link>
                  </span>
                </div>
                <div className="detail-row">
                  <span className="label">Total Crashes</span>
                  <span className="value">
                    <span className="badge badge-count">{bucket.crashCount}</span>
                  </span>
                </div>
              </>
            ) : (
              <div className="detail-row">
                <span className="label">Status</span>
                <span className="value" style={{ color: 'var(--text-muted)' }}>
                  No bucket assigned
                </span>
              </div>
            )}
          </div>

          <div className="detail-card">
            <h3>Key Stack Frames</h3>
            {keyFrames.length > 0 ? (
              <ul className="key-frames-list">
                {keyFrames.map((kf, i) => (
                  <li key={i}>{kf}</li>
                ))}
              </ul>
            ) : (
              <p style={{ color: 'var(--text-muted)', fontSize: 13 }}>
                No key frames available
              </p>
            )}
          </div>
        </div>
      </div>

      {/* 3. Symbolicated Stack Trace */}
      {symbolicatedStack && symbolicatedStack.length > 0 && (
        <div className="section">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
            <h3 className="section-title" style={{ margin: 0 }}>
              Symbolicated Stack Trace ({symbolicatedStack.length} frames)
            </h3>
            <div style={{ display: 'flex', gap: 8 }}>
              <button
                onClick={handleCopyStackTrace}
                style={{
                  padding: '6px 12px',
                  backgroundColor: 'var(--accent)',
                  color: 'white',
                  border: 'none',
                  borderRadius: 'var(--radius)',
                  fontSize: 12,
                  cursor: 'pointer',
                  fontFamily: 'var(--font)',
                }}
              >
                📋 Copy Stack
              </button>
              <button
                onClick={handleDownloadDump}
                disabled={!crash.dumpPath}
                style={{
                  padding: '6px 12px',
                  backgroundColor: crash.dumpPath ? 'var(--green)' : 'var(--border)',
                  color: crash.dumpPath ? 'white' : 'var(--text-muted)',
                  border: 'none',
                  borderRadius: 'var(--radius)',
                  fontSize: 12,
                  cursor: crash.dumpPath ? 'pointer' : 'not-allowed',
                  fontFamily: 'var(--font)',
                }}
              >
                💾 Download Dump
              </button>
            </div>
          </div>
          <div className="stack-trace">
            {symbolicatedStack.map((frame, i) => {
              const isKey = keyFrames.some((kf) => frame.includes(kf!));
              const isSys = isSystemFrame(frame);
              const cls = isKey
                ? 'stack-frame key-frame'
                : isSys
                  ? 'stack-frame system-frame'
                  : 'stack-frame';

              return (
                <div key={i} className={cls}>
                  <span className="frame-num">#{String(i).padStart(2, '0')}</span>
                  {frame}
                  {isKey && <span className="key-tag">KEY</span>}
                </div>
              );
            })}
          </div>
        </div>
      )}

      {!symbolicatedStack && (
        <div className="section">
          <h3 className="section-title">Stack Trace</h3>
          <div className="table-container" style={{ padding: '40px', textAlign: 'center' }}>
            <p style={{ color: 'var(--text-muted)' }}>
              No symbolicated stack trace available for this crash
            </p>
          </div>
        </div>
      )}
    </>
  );
}
