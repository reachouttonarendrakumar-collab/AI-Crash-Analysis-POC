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
}

export interface BucketDetailResponse {
  bucket: BucketRow;
  keyFrames: string[];
  crashes: CrashRow[];
}
