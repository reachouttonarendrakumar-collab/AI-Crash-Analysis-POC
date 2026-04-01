import { API_BASE_URL } from './config';
import type {
  CrashesResponse,
  BucketsResponse,
  CrashDetailResponse,
  BucketDetailResponse,
} from './types';

/**
 * Generic fetch wrapper with error handling
 */
async function apiFetch<T>(path: string): Promise<T> {
  const res = await fetch(`${API_BASE_URL}${path}`);
  if (!res.ok) {
    throw new Error(`API ${res.status}: ${res.statusText}`);
  }
  return res.json() as Promise<T>;
}

/**
 * Get list of crashes with optional limit
 */
export function getCrashes(limit = 100): Promise<CrashesResponse> {
  return apiFetch<CrashesResponse>(`/crashes?limit=${limit}`);
}

/**
 * Get detailed crash information by ID
 */
export function getCrashById(crashId: string): Promise<CrashDetailResponse> {
  return apiFetch<CrashDetailResponse>(`/crashes/${encodeURIComponent(crashId)}`);
}

/**
 * Get list of buckets with optional limit
 */
export function getBuckets(limit = 100): Promise<BucketsResponse> {
  return apiFetch<BucketsResponse>(`/buckets?limit=${limit}`);
}

/**
 * Get detailed bucket information by ID
 */
export function getBucketById(bucketId: string): Promise<BucketDetailResponse> {
  return apiFetch<BucketDetailResponse>(`/buckets/${encodeURIComponent(bucketId)}`);
}
