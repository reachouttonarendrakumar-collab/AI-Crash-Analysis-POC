# Crash Collector Dashboard

A React-based web dashboard for analyzing crash reports collected by the Crash Collector POC. Built for developers to triage and investigate application crashes efficiently.

## Purpose

The dashboard provides a developer-friendly interface to:
- **Visualize crash trends** over time with interactive charts
- **Group crashes by stack signature** into logical buckets
- **Examine symbolicated stack traces** with key frame highlighting
- **Navigate between crashes and buckets** for deep analysis
- **Quickly identify high-impact issues** through sorting and filtering

## Architecture

```
crash-dashboard/ (React SPA)
    ↓ API calls (fetch)
crash-poc/CrashCollector.Api (ASP.NET Minimal API)
    ↓
crash-poc/data/crashes.db (SQLite)
```

### API Integration

The dashboard connects to `CrashCollector.Api` via a Vite proxy:

```typescript
// vite.config.ts
server: {
  proxy: {
    '/api': {
      target: 'http://localhost:5100',
      changeOrigin: true,
      rewrite: (path) => path.replace(/^\/api/, ''),
    },
  },
}
```

**Endpoints Used:**
- `GET /api/crashes?limit=N` - List crash reports
- `GET /api/crashes/{id}` - Crash detail with symbolicated stack
- `GET /api/buckets?limit=N` - List crash buckets
- `GET /api/buckets/{id}` - Bucket details with member crashes

## How Developers Use It

### 1. Overview Page (`/`)
- **Summary Metrics**: Total crashes, buckets, top exception code
- **Trend Analysis**: Daily crash volume chart
- **Quick Access**: Top 5 buckets and recent crashes

### 2. Buckets Page (`/buckets`)
- **Sorted by Impact**: Most frequent crashes first
- **Click Navigation**: Jump to bucket details
- **Exception Focus**: See patterns by exception type

### 3. Crashes Page (`/crashes`)
- **Chronological Order**: Newest crashes first
- **Key Data**: Exception codes, versions, bucket associations
- **Deep Links**: Navigate to detailed crash analysis

### 4. Detail Pages (`/crashes/{id}`, `/buckets/{id}`)
- **Debugging Tools**: Symbolicated stack traces with key frame highlighting
- **Actions**: Copy stack trace, download dump (stub)
- **Context**: Bucket information and related crashes

## Running Locally

### Prerequisites
- **Node.js** 18+ and npm
- **CrashCollector.Api** running on `http://localhost:5100`
- **Populated database** (run the Console app first)

### Development Setup

```bash
# 1. Install dependencies
npm install

# 2. Start development server
npm run dev
# → http://localhost:3000

# 3. (Optional) Build for production
npm run build
npm run preview
```

### Production Deployment

```bash
# Build optimized bundle
npm run build

# Deploy dist/ folder to static hosting
# Configure API proxy to production backend
```

## Project Structure

```
src/
├── api/                    # API client layer
│   ├── config.ts          # Base URL configuration
│   ├── types.ts           # TypeScript interfaces
│   └── index.ts           # Fetch functions (getCrashes, getBuckets...)
├── components/
│   ├── Layout.tsx         # Sidebar navigation
│   └── Loading.tsx        # Loading/Error/EmptyState components
├── pages/
│   ├── OverviewPage.tsx   # Dashboard with charts and metrics
│   ├── BucketsPage.tsx    # Bucket list with sorting
│   ├── BucketDetailPage.tsx # Bucket analysis
│   ├── CrashesPage.tsx    # Crash list
│   └── CrashDetailPage.tsx # Crash debugging view
├── hooks/
│   └── useFetch.ts        # Generic data fetching hook
└── main.tsx               # App entry point
```

## Known Limitations

### Data
- **Mock Data Dependency**: Requires Console app to populate SQLite database
- **No Real-time Updates**: Manual refresh needed for new crashes
- **Limited Pagination**: Fixed limits (100 items) on list pages

### Features
- **No Authentication**: Open access to all crash data
- **No Export**: Cannot download crash lists or reports
- **No Filtering**: Limited to sorting and basic navigation

### Technical
- **Single Database**: No multi-tenant or environment isolation
- **No Caching**: API calls on every navigation
- **Error Boundaries**: Basic error handling, no graceful degradation

### Browser Support
- **Modern Browsers Only**: Requires ES2020+ features
- **No Mobile Optimization**: Designed for desktop developer workflows

## Technology Stack

- **Frontend**: React 19 + TypeScript + Vite
- **Routing**: React Router v6
- **Charts**: Recharts (lightweight, SVG-based)
- **Styling**: CSS variables + modules (dark theme)
- **API**: Native fetch (no Axios)
- **Build**: Vite (HMR, optimized bundles)

## Development Notes

- **API Proxy**: All `/api/*` requests proxy to backend during development
- **Error Handling**: Centralized in `useFetch` hook with loading states
- **Type Safety**: Full TypeScript coverage for API responses
- **Performance**: useMemo for expensive calculations (sorting, grouping)
