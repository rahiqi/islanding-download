export interface DownloadState {
  downloadId: string
  url: string
  status: 'Queued' | 'Downloading' | 'Completed' | 'Failed'
  enqueuedAt: string
  totalBytes: number
  downloadedBytes: number
  bytesPerSecond: number
  agentId: string | null
  errorMessage: string | null
  completedAt: string | null
  percentComplete: number
}

export interface AgentInfo {
  agentId: string
  lastSeen: string
  currentDownloads: number
}

export interface ProgressEvent {
  downloadId: string
  agentId: string | null
  totalBytes: number | null
  downloadedBytes: number
  bytesPerSecond: number
  status: string
  message: string | null
}
