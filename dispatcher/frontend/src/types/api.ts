export interface DownloadState {
  downloadId: string
  url: string
  status: 'Queued' | 'Downloading' | 'Paused' | 'Completed' | 'Failed' | 'Cancelled'
  enqueuedAt: string
  totalBytes: number
  downloadedBytes: number
  bytesPerSecond: number
  agentId: string | null
  preferredAgentId: string | null
  errorMessage: string | null
  completedAt: string | null
  percentComplete: number
  /** Local URL on the agent to download the file (only when status is Completed and agent serves it). */
  localDownloadUrl: string | null
}

export interface AgentInfo {
  agentId: string
  name: string
  location: string
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
  localDownloadUrl: string | null
}
