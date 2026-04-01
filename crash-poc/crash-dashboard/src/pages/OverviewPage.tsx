import { useMemo } from 'react';
import { Link } from 'react-router-dom';
import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer } from 'recharts';
import { getCrashes, getBuckets, getAIAnalyses, getAIFixes } from '../api';
import { useFetch } from '../hooks/useFetch';
import { Loading, ErrorBox, EmptyState } from '../components/Loading';

export default function OverviewPage() {
  const crashes = useFetch(() => getCrashes(1000), []);
  const buckets = useFetch(() => getBuckets(100), []);
  const aiAnalyses = useFetch(() => getAIAnalyses(100), []);
  const aiFixes = useFetch(() => getAIFixes(100), []);

  // Memoized calculations - always called, even if data is still loading
  const topException = useMemo(() => {
    if (!crashes.data) return ['Unknown', 0];
    const counts = new Map<string, number>();
    crashes.data.crashes.forEach((c) => {
      const code = c.exceptionCode ?? 'Unknown';
      counts.set(code, (counts.get(code) ?? 0) + 1);
    });
    return Array.from(counts.entries())
      .sort(([, a], [, b]) => b - a)[0] ?? ['Unknown', 0];
  }, [crashes.data]);

  const trendData = useMemo(() => {
    if (!crashes.data) return [];
    const grouped = new Map<string, number>();
    
    crashes.data.crashes.forEach((c) => {
      const date = new Date(c.timestamp).toISOString().split('T')[0]; // YYYY-MM-DD
      grouped.set(date, (grouped.get(date) ?? 0) + 1);
    });

    // Sort by date and fill missing days with 0
    const sorted = Array.from(grouped.entries()).sort(([a], [b]) => a.localeCompare(b));
    if (sorted.length === 0) return [];

    // Fill gaps between first and last date
    const [firstDate] = sorted[0];
    const [lastDate] = sorted[sorted.length - 1];
    const filled: typeof sorted = [];
    
    const current = new Date(firstDate);
    const end = new Date(lastDate);
    
    while (current <= end) {
      const dateStr = current.toISOString().split('T')[0];
      const count = grouped.get(dateStr) ?? 0;
      filled.push([dateStr, count]);
      current.setDate(current.getDate() + 1);
    }

    return filled.map(([date, count]) => ({
      date: new Date(date).toLocaleDateString('en-US', { month: 'short', day: 'numeric' }),
      count,
    }));
  }, [crashes.data]);

  // AI metrics
  const aiAnalyzed = aiAnalyses.data?.analyses?.length ?? 0;
  const aiFixed = aiFixes.data?.fixes?.filter(f => f.prStatus === 'Open' || f.prStatus === 'Merged').length ?? 0;
  const aiPending = (aiAnalyses.data?.analyses?.filter(a => a.status === 'ManualReview').length ?? 0);

  // Early returns after all hooks are called
  if (crashes.loading || buckets.loading) return <Loading message="Loading dashboard data..." />;
  if (crashes.error) return <ErrorBox message={`Failed to load crashes: ${crashes.error}`} />;
  if (buckets.error) return <ErrorBox message={`Failed to load buckets: ${buckets.error}`} />;

  const crashData = crashes.data!;
  const bucketData = buckets.data!;

  // Handle empty data
  if (crashData.count === 0 && bucketData.count === 0) {
    return (
      <EmptyState
        title="No Crash Data Available"
        message="No crash reports have been collected yet. Run the console application to populate the database."
        action={{
          label: "View Console App",
          onClick: () => window.open('https://github.com/microsoft/Windows-Dev-Protect', '_blank')
        }}
      />
    );
  }

  // Summary metrics
  const totalCrashes = crashData.count;
  const totalBuckets = bucketData.count;
  const withDumps = crashData.crashes.filter((c) => c.dumpPath).length;

  return (
    <>
      <div className="page-header">
        <h2>Overview</h2>
        <p>Crash Collector POC dashboard summary</p>
      </div>

      {/* Summary Metrics */}
      <div className="stats-grid">
        <div className="stat-card">
          <div className="stat-label">Total Crashes</div>
          <div className="stat-value">{totalCrashes.toLocaleString()}</div>
          <div className="stat-sub">{withDumps} with dump files</div>
        </div>
        <div className="stat-card">
          <div className="stat-label">Unique Buckets</div>
          <div className="stat-value">{totalBuckets}</div>
          <div className="stat-sub">Crash signatures</div>
        </div>
        <div className="stat-card">
          <div className="stat-label">Top Exception</div>
          <div className="stat-value mono" style={{ fontSize: 18 }}>
            {topException[0]}
          </div>
          <div className="stat-sub">{topException[1]} occurrences</div>
        </div>
        <div className="stat-card">
          <div className="stat-label">Avg per Bucket</div>
          <div className="stat-value">
            {totalBuckets > 0 ? (totalCrashes / totalBuckets).toFixed(1) : '0'}
          </div>
          <div className="stat-sub">Crashes per bucket</div>
        </div>
      </div>

      {/* AI Metrics */}
      <div className="stats-grid">
        <div className="stat-card">
          <div className="stat-label">AI Analyzed</div>
          <div className="stat-value" style={{ color: 'var(--accent)' }}>{aiAnalyzed}</div>
          <div className="stat-sub">Buckets analyzed by Gemini</div>
        </div>
        <div className="stat-card">
          <div className="stat-label">Fixes Generated</div>
          <div className="stat-value" style={{ color: 'var(--green)' }}>{aiFixed}</div>
          <div className="stat-sub">PRs open or merged</div>
        </div>
        <div className="stat-card">
          <div className="stat-label">Manual Review</div>
          <div className="stat-value" style={{ color: 'var(--orange)' }}>{aiPending}</div>
          <div className="stat-sub">Need developer attention</div>
        </div>
        <div className="stat-card">
          <div className="stat-label">Auto-Fix Rate</div>
          <div className="stat-value">
            {aiAnalyzed > 0 ? ((aiFixed / aiAnalyzed) * 100).toFixed(0) : '0'}%
          </div>
          <div className="stat-sub">Of analyzed buckets</div>
        </div>
      </div>

      {/* Crash Trend Chart */}
      <div className="section">
        <h3 className="section-title">Crash Trend</h3>
        <div className="table-container" style={{ padding: '20px', height: '300px' }}>
          {trendData.length > 0 ? (
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={trendData} margin={{ top: 10, right: 10, bottom: 30, left: 40 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--border)" />
                <XAxis 
                  dataKey="date" 
                  tick={{ fill: 'var(--text-muted)', fontSize: 11 }}
                  axisLine={{ stroke: 'var(--border)' }}
                />
                <YAxis 
                  tick={{ fill: 'var(--text-muted)', fontSize: 11 }}
                  axisLine={{ stroke: 'var(--border)' }}
                />
                <Tooltip
                  contentStyle={{
                    backgroundColor: 'var(--bg-card)',
                    border: '1px solid var(--border)',
                    borderRadius: 'var(--radius)',
                  }}
                  labelStyle={{ color: 'var(--text)' }}
                  itemStyle={{ color: 'var(--accent)' }}
                />
                <Bar dataKey="count" fill="var(--accent)" radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          ) : (
            <div style={{ 
              display: 'flex', 
              alignItems: 'center', 
              justifyContent: 'center', 
              height: '100%', 
              color: 'var(--text-muted)' 
            }}>
              No crash data available
            </div>
          )}
        </div>
      </div>

      {/* Top 5 Buckets Table */}
      <div className="section">
        <h3 className="section-title">Top 5 Crash Buckets</h3>
        <div className="table-container">
          <table>
            <thead>
              <tr>
                <th>Exception</th>
                <th>Top Key Frame</th>
                <th>Crashes</th>
                <th>First Seen</th>
              </tr>
            </thead>
            <tbody>
              {bucketData.buckets.slice(0, 5).map((b) => (
                <tr key={b.bucketId}>
                  <td>
                    {b.exceptionCode ? (
                      <span className="badge badge-exception">
                        {b.exceptionCode}
                      </span>
                    ) : (
                      <span style={{ color: 'var(--text-muted)' }}>n/a</span>
                    )}
                  </td>
                  <td className="truncate">
                    <Link to={`/buckets/${b.bucketId}`}>
                      {b.keyFrame1 ?? 'Unknown'}
                    </Link>
                  </td>
                  <td style={{ textAlign: 'center' }}>
                    <span className="badge badge-count">{b.crashCount}</span>
                  </td>
                  <td style={{ whiteSpace: 'nowrap' }}>
                    {new Date(b.firstSeenUtc).toLocaleDateString()}
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
