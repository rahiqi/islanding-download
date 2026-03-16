import type { DownloadState, AgentInfo } from '../types/api'

const base = ''
const fetchOpts = { credentials: 'include' as RequestCredentials }

export async function getCurrentUser(): Promise<{ username: string } | null> {
  const res = await fetch(`${base}/api/auth/me`, fetchOpts)
  if (res.status === 401) return null
  if (!res.ok) return null
  return res.json()
}

export async function login(username: string): Promise<{ username: string }> {
  const res = await fetch(`${base}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: username.trim() }),
    ...fetchOpts,
  })
  if (!res.ok) {
    const err = await res.json().catch(() => ({}))
    throw new Error(err?.error ?? 'Login failed')
  }
  return res.json()
}

export async function logout(): Promise<void> {
  await fetch(`${base}/api/auth/logout`, { method: 'POST', ...fetchOpts })
}

export async function submitDownload(
  url: string,
  preferredAgentId?: string | null
): Promise<{ downloadId: string; url: string; status: string }> {
  const res = await fetch(`${base}/api/downloads`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ url, preferredAgentId: preferredAgentId || undefined }),
    ...fetchOpts,
  })
  if (!res.ok) {
    const err = await res.json().catch(() => ({}))
    throw new Error(err?.error ?? res.statusText)
  }
  return res.json()
}

export async function listDownloads(): Promise<DownloadState[]> {
  const res = await fetch(`${base}/api/downloads`, fetchOpts)
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

// Pause/Resume/Cancel were removed to avoid corrupting files; downloads now run to completion once started.
