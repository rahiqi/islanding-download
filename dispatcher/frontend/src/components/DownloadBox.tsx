import { useState, useCallback } from 'react'
import type { AgentInfo } from '../types/api'

interface DownloadBoxProps {
  agents: AgentInfo[]
  onSubmit: (url: string, preferredAgentId?: string | null) => Promise<void>
  loading: boolean
  error: string | null
  isLoggedIn: boolean
}

export function DownloadBox({ agents, onSubmit, loading, error, isLoggedIn }: DownloadBoxProps) {
  const [url, setUrl] = useState('')
  const [preferredAgentId, setPreferredAgentId] = useState<string>('')

  const handleSubmit = useCallback(
    async (e: React.FormEvent) => {
      e.preventDefault()
      const trimmed = url.trim()
      if (!trimmed) return
      await onSubmit(trimmed, preferredAgentId || null)
      setUrl('')
    },
    [url, preferredAgentId, onSubmit]
  )

  const handlePaste = useCallback((e: React.ClipboardEvent<HTMLInputElement>) => {
    const text = e.clipboardData.getData('text').trim()
    if (text && (text.startsWith('http://') || text.startsWith('https://'))) {
      e.preventDefault()
      setUrl(text)
    }
  }, [])

  return (
    <section className="download-box">
      <h2>Add download</h2>
      {!isLoggedIn ? (
        <p className="muted">Log in above to queue a download.</p>
      ) : (
        <>
          <form onSubmit={handleSubmit}>
            <input
              type="url"
              placeholder="Paste or type download URL (http/https)"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              onPaste={handlePaste}
              disabled={loading}
              autoComplete="url"
              aria-label="Download URL"
            />
            <label className="download-box-agent">
              <span className="download-box-agent-label">Run on</span>
              <select
                value={preferredAgentId}
                onChange={(e) => setPreferredAgentId(e.target.value)}
                disabled={loading}
                aria-label="Preferred agent"
              >
                <option value="">Any available agent</option>
                {agents.map((a) => (
                  <option key={a.agentId} value={a.agentId}>
                    {a.name || a.agentId}{a.location ? ` (${a.location})` : ''}
                  </option>
                ))}
              </select>
            </label>
            <button type="submit" disabled={loading || !url.trim()}>
              {loading ? 'Adding…' : 'Add to queue'}
            </button>
          </form>
          {error && <p className="error">{error}</p>}
        </>
      )}
    </section>
  )
}
