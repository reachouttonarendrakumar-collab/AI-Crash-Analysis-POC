import { API_BASE_URL } from './config';
import type {
  CrashesResponse,
  BucketsResponse,
  CrashDetailResponse,
  BucketDetailResponse,
  AIAnalysesResponse,
  AIFixesResponse,
  AnalyzeResponse,
  LoginResponse,
  UserInfo,
} from './types';

function getAuthHeaders(): Record<string, string> {
  const token = localStorage.getItem('auth_token');
  return token ? { Authorization: `Bearer ${token}` } : {};
}

async function apiFetch<T>(path: string): Promise<T> {
  const res = await fetch(`${API_BASE_URL}${path}`, {
    headers: { ...getAuthHeaders() },
  });
  if (!res.ok) {
    throw new Error(`API ${res.status}: ${res.statusText}`);
  }
  return res.json() as Promise<T>;
}

async function apiPost<T>(path: string, body?: unknown): Promise<T> {
  const res = await fetch(`${API_BASE_URL}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...getAuthHeaders() },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!res.ok) {
    throw new Error(`API ${res.status}: ${res.statusText}`);
  }
  return res.json() as Promise<T>;
}

// ── Crashes ──────────────────────────────────────────────────────────────
export function getCrashes(limit = 100): Promise<CrashesResponse> {
  return apiFetch<CrashesResponse>(`/crashes?limit=${limit}`);
}

export function getCrashById(crashId: string): Promise<CrashDetailResponse> {
  return apiFetch<CrashDetailResponse>(`/crashes/${encodeURIComponent(crashId)}`);
}

// ── Buckets ──────────────────────────────────────────────────────────────
export function getBuckets(limit = 100): Promise<BucketsResponse> {
  return apiFetch<BucketsResponse>(`/buckets?limit=${limit}`);
}

export function getBucketById(bucketId: string): Promise<BucketDetailResponse> {
  return apiFetch<BucketDetailResponse>(`/buckets/${encodeURIComponent(bucketId)}`);
}

// ── AI Analysis ──────────────────────────────────────────────────────────
export function getAIAnalyses(limit = 100): Promise<AIAnalysesResponse> {
  return apiFetch<AIAnalysesResponse>(`/ai/analyses?limit=${limit}`);
}

export function triggerAnalysis(bucketId: string): Promise<AnalyzeResponse> {
  return apiPost<AnalyzeResponse>(`/ai/analyze/${encodeURIComponent(bucketId)}`);
}

// ── AI Fixes ─────────────────────────────────────────────────────────────
export function getAIFixes(limit = 100): Promise<AIFixesResponse> {
  return apiFetch<AIFixesResponse>(`/ai/fixes?limit=${limit}`);
}

export function triggerFix(bucketId: string): Promise<AnalyzeResponse> {
  return apiPost<AnalyzeResponse>(`/ai/fix/${encodeURIComponent(bucketId)}`);
}

// ── Auth ─────────────────────────────────────────────────────────────────
export function login(email: string, password: string): Promise<LoginResponse> {
  return apiPost<LoginResponse>('/auth/login', { email, password });
}

export function getCurrentUser(): Promise<UserInfo> {
  return apiFetch<UserInfo>('/auth/me');
}
