import type { DownloadState, AgentInfo } from '../types/api'

const base = ''

export async function submitDownload(
  url: string,
  preferredAgentId?: string | null
): Promise<{ downloadId: string; url: string; status: string }> {
  const res = await fetch(`${base}/api/downloads`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ url, preferredAgentId: preferredAgentId || undefined }),
  })
  if (!res.ok) {
    const err = await res.json().catch(() => ({}))
    throw new Error(err?.error ?? res.statusText)
  }
  return res.json()
}

export async function listDownloads(): Promise<DownloadState[]> {
  const res = await fetch(`${base}/api/downloads`)
  if (!res.ok) throw new Error('Failed to fetch downloads')
  return res.json()
}

export async function listAgents(): Promise<AgentInfo[]> {
  const res = await fetch(`${base}/api/agents`)
  if (!res.ok) throw new Error('Failed to fetch agents')
  return res.json()
}

export function getProgressEventSource(): EventSource {
  return new EventSource(`${base}/api/downloads/events`)
}

export async function pauseDownload(downloadId: string): Promise<void> {
  const res = await fetch(`${base}/api/downloads/${downloadId}/pause`, { method: 'POST' })
  if (!res.ok) throw new Error(res.status === 404 ? 'Download not found' : 'Failed to pause')
}

export async function resumeDownload(downloadId: string): Promise<void> {
  const res = await fetch(`${base}/api/downloads/${downloadId}/resume`, { method: 'POST' })
  if (!res.ok) {
    const err = await res.json().catch(() => ({}))
    throw new Error(err?.error ?? 'Failed to resume')
  }
}

export async function cancelDownload(downloadId: string): Promise<void> {
  const res = await fetch(`${base}/api/downloads/${downloadId}/cancel`, { method: 'POST' })
  if (!res.ok) throw new Error(res.status === 404 ? 'Download not found' : 'Failed to cancel')
}
