import type { DownloadState, AgentInfo } from '../types/api'

const base = ''

export async function submitDownload(url: string): Promise<{ downloadId: string; url: string; status: string }> {
  const res = await fetch(`${base}/api/downloads`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ url }),
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
