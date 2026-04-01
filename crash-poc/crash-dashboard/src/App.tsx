import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import Layout from './components/Layout';
import LoginPage from './pages/LoginPage';
import OverviewPage from './pages/OverviewPage';
import CrashesPage from './pages/CrashesPage';
import CrashDetailPage from './pages/CrashDetailPage';
import BucketsPage from './pages/BucketsPage';
import BucketDetailPage from './pages/BucketDetailPage';
import AIAnalysisPage from './pages/AIAnalysisPage';
import FixesPage from './pages/FixesPage';

function RequireAuth({ children }: { children: React.ReactNode }) {
  const token = localStorage.getItem('auth_token');
  if (!token) return <Navigate to="/login" replace />;
  return <>{children}</>;
}

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route element={<RequireAuth><Layout /></RequireAuth>}>
          <Route path="/" element={<OverviewPage />} />
          <Route path="/crashes" element={<CrashesPage />} />
          <Route path="/crashes/:id" element={<CrashDetailPage />} />
          <Route path="/buckets" element={<BucketsPage />} />
          <Route path="/buckets/:bucketId" element={<BucketDetailPage />} />
          <Route path="/ai-analysis" element={<AIAnalysisPage />} />
          <Route path="/fixes" element={<FixesPage />} />
        </Route>
      </Routes>
    </BrowserRouter>
  );
}
