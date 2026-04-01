# API Client Layer

## Structure

```
src/api/
├── config.ts      # API base URL (proxied to backend)
├── types.ts        # TypeScript interfaces for all API responses
├── index.ts        # Public API functions
└── README.md       # This file
```

## Functions

| Function | Endpoint | Returns |
|---|---|---|
| `getCrashes(limit?)` | `GET /crashes?limit=N` | `CrashesResponse` |
| `getCrashById(crashId)` | `GET /crashes/{id}` | `CrashDetailResponse` |
| `getBuckets(limit?)` | `GET /buckets?limit=N` | `BucketsResponse` |
| `getBucketById(bucketId)` | `GET /buckets/{id}` | `BucketDetailResponse` |

## Usage

```ts
import { getCrashes, getCrashById } from '../api';

// List crashes
const { count, crashes } = await getCrashes(50);

// Get crash detail with symbolicated stack
const { crash, bucket, symbolicatedStack } = await getCrashById('WER-C0000409-...');
```

## Error Handling

All functions throw on HTTP errors with a clear message: `API {status}: {statusText}`. Use a try/catch or the provided `useFetch` hook for graceful UI handling.
