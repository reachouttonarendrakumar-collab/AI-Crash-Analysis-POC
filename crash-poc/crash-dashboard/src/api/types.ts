export interface CrashRow {
  crashId: string;
  appName: string;
  appVersion: string;
  timestamp: string;
  exceptionCode: string | null;
  exceptionName: string | null;
  faultingModule: string | null;
  threadCount: number;
  dumpPath: string | null;
  dumpSizeMB: number;
  dumpSha256: string | null;
  failureBucket: string | null;
  extractionMethod: string | null;
}

export interface BucketRow {
  bucketId: string;
  exceptionCode: string | null;
  keyFrame1: string | null;
  keyFrame2: string | null;
  keyFrame3: string | null;
  crashCount: number;
  firstSeenUtc: string;
  lastSeenUtc: string;
  aiStatus?: string | null;
  aiConfidence?: number | null;
  fixStatus?: string | null;
  prUrl?: string | null;
}

export interface AIAnalysisRow {
  analysisId: string;
  bucketId: string;
  rootCause: string | null;
  confidence: number;
  suggestedFix: string | null;
  affectedFile: string | null;
  affectedFunction: string | null;
  status: string;
  errorMessage: string | null;
  promptTokens: number;
  responseTokens: number;
  createdAtUtc: string;
  completedAtUtc: string | null;
}

export interface AIFixRow {
  fixId: string;
  analysisId: string;
  bucketId: string;
  branchName: string | null;
  commitSha: string | null;
  prNumber: number;
  prUrl: string | null;
  prTitle: string | null;
  prStatus: string;
  fixDescription: string | null;
  filesChanged: string | null;
  errorMessage: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface CrashesResponse {
  count: number;
  crashes: CrashRow[];
}

export interface BucketsResponse {
  count: number;
  buckets: BucketRow[];
}

export interface CrashDetailResponse {
  crash: CrashRow;
  bucketId: string | null;
  bucket: BucketRow | null;
  symbolicatedStack: string[] | null;
  aiAnalysis: AIAnalysisRow | null;
  aiFix: AIFixRow | null;
}

export interface BucketDetailResponse {
  bucket: BucketRow;
  keyFrames: string[];
  crashes: CrashRow[];
  aiAnalysis: AIAnalysisRow | null;
  aiFix: AIFixRow | null;
}

export interface AIAnalysesResponse {
  count: number;
  analyses: AIAnalysisRow[];
}

export interface AIFixesResponse {
  count: number;
  fixes: AIFixRow[];
}

export interface AnalyzeResponse {
  bucketId: string;
  status: string;
  rootCause: string | null;
  confidence: number | null;
  fixId: string | null;
  prUrl: string | null;
  prNumber: number;
}

export interface LoginResponse {
  token: string;
  user: UserInfo;
}

export interface UserInfo {
  id: string;
  name: string;
  email: string;
  role: string;
  application: string;
}
