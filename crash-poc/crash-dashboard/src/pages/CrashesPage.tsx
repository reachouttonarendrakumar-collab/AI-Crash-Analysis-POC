import { useMemo } from 'react';
import { Link } from 'react-router-dom';
import { getCrashes } from '../api';
import { useFetch } from '../hooks/useFetch';
import { Loading, ErrorBox, EmptyState } from '../components/Loading';

export default function CrashesPage() {
  const { data, loading, error } = useFetch(() => getCrashes(100), []);

  // Memoized sorting - always called, even if data is still loading
  const sortedCrashes = useMemo(() => {
    if (!data) return [];
    return data.crashes.slice().sort((a, b) => 
      new Date(b.timestamp).getTime() - new Date(a.timestamp).getTime()
    );
  }, [data]);

  // Early returns after all hooks are called
  if (loading) return <Loading message="Loading crashes..." />;
  if (error) return <ErrorBox message={`Failed to load crashes: ${error}`} />;

  const crashes = data!.crashes;

  if (crashes.length === 0) {
    return (
      <EmptyState
        title="No Crashes Found"
        message="No crash reports have been collected yet. Run the console application to collect crash data from Windows Error Reporting."
        action={{
          label: "View Overview",
          onClick: () => window.location.href = '/'
        }}
      />
    );
  }

  return (
    <>
      <div className="page-header">
        <h2>Crashes</h2>
        <p>{data!.count} crash report(s) in database</p>
      </div>

      <div className="table-container">
        <table>
          <thead>
            <tr>
              <th>Crash ID</th>
              <th>Timestamp</th>
              <th>Exception Code</th>
              <th>App Version</th>
              <th>Bucket ID</th>
            </tr>
          </thead>
          <tbody>
            {sortedCrashes.map((c) => (
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
                <td>
                  {c.failureBucket ? (
                    <span className="mono" style={{ fontSize: 11, color: 'var(--text-muted)' }}>
                      {c.failureBucket.slice(0, 12)}...
                    </span>
                  ) : (
                    <span style={{ color: 'var(--text-muted)' }}>n/a</span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}
