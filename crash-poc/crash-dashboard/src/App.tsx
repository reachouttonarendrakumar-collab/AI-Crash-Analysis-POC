import { BrowserRouter, Routes, Route } from 'react-router-dom';
import Layout from './components/Layout';
import OverviewPage from './pages/OverviewPage';
import CrashesPage from './pages/CrashesPage';
import CrashDetailPage from './pages/CrashDetailPage';
import BucketsPage from './pages/BucketsPage';
import BucketDetailPage from './pages/BucketDetailPage';

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route path="/" element={<OverviewPage />} />
          <Route path="/crashes" element={<CrashesPage />} />
          <Route path="/crashes/:id" element={<CrashDetailPage />} />
          <Route path="/buckets" element={<BucketsPage />} />
          <Route path="/buckets/:bucketId" element={<BucketDetailPage />} />
        </Route>
      </Routes>
    </BrowserRouter>
  );
}
