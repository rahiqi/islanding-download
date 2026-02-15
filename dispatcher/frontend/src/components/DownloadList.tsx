import type { DownloadState } from '../types/api'

interface DownloadListProps {
  downloads: DownloadState[]
}

function formatBytes(n: number): string {
  if (n === 0) return '0 B'
  const k = 1024
  const i = Math.floor(Math.log(n) / Math.log(k))
  return `${(n / Math.pow(k, i)).toFixed(1)} ${['B', 'KB', 'MB', 'GB'][i]}`
}

function formatRate(bytesPerSecond: number): string {
  return `${formatBytes(bytesPerSecond)}/s`
}

export function DownloadList({ downloads }: DownloadListProps) {
  if (downloads.length === 0) {
    return (
      <section className="download-list">
        <h2>Downloads</h2>
        <p className="muted">No downloads yet. Add a URL above.</p>
      </section>
    )
  }

  return (
    <section className="download-list">
      <h2>Downloads ({downloads.length})</h2>
      <ul>
        {downloads.map((d) => (
          <li key={d.downloadId} data-status={d.status}>
            <div className="download-row">
              <a href={d.url} target="_blank" rel="noopener noreferrer" className="download-url" title={d.url}>
                {d.url.length > 60 ? d.url.slice(0, 57) + '…' : d.url}
              </a>
              <span className="download-status">{d.status}</span>
            </div>
            <div className="download-progress">
              <div className="progress-bar">
                <div className="progress-fill" style={{ width: `${d.percentComplete}%` }} />
              </div>
              <div className="progress-meta">
                <span>
                  {formatBytes(d.downloadedBytes)}
                  {d.totalBytes > 0 && ` / ${formatBytes(d.totalBytes)}`}
                </span>
                {d.status === 'Downloading' && d.bytesPerSecond > 0 && (
                  <span className="rate">{formatRate(d.bytesPerSecond)}</span>
                )}
                {d.agentId && <span className="agent">Agent: {d.agentId}</span>}
              </div>
            </div>
            {d.errorMessage && <p className="error-message">{d.errorMessage}</p>}
          </li>
        ))}
      </ul>
    </section>
  )
}
