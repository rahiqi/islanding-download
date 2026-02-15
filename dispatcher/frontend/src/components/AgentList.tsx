import type { AgentInfo } from '../types/api'

interface AgentListProps {
  agents: AgentInfo[]
  error: string | null
}

function formatTime(iso: string) {
  const d = new Date(iso)
  const now = Date.now()
  const diff = (now - d.getTime()) / 1000
  if (diff < 60) return 'just now'
  if (diff < 3600) return `${Math.floor(diff / 60)}m ago`
  return d.toLocaleTimeString()
}

export function AgentList({ agents, error }: AgentListProps) {
  return (
    <section className="agent-list">
      <h2>Available agents ({agents.length})</h2>
      {error && <p className="error">{error}</p>}
      {agents.length === 0 && !error && <p className="muted">No agents connected yet.</p>}
      <ul>
        {agents.map((a) => (
          <li key={a.agentId}>
            <span className="agent-id">{a.agentId}</span>
            <span className="agent-meta">
              last seen {formatTime(a.lastSeen)} · {a.currentDownloads} active
            </span>
          </li>
        ))}
      </ul>
    </section>
  )
}
