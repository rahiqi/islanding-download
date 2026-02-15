import { useState, useCallback } from 'react'

interface DownloadBoxProps {
  onSubmit: (url: string) => Promise<void>
  loading: boolean
  error: string | null
}

export function DownloadBox({ onSubmit, loading, error }: DownloadBoxProps) {
  const [url, setUrl] = useState('')

  const handleSubmit = useCallback(
    async (e: React.FormEvent) => {
      e.preventDefault()
      const trimmed = url.trim()
      if (!trimmed) return
      await onSubmit(trimmed)
      setUrl('')
    },
    [url, onSubmit]
  )

  const handlePaste = useCallback((e: React.ClipboardEvent) => {
    const text = e.clipboardData.getData('text').trim()
    if (text && (text.startsWith('http://') || text.startsWith('https://'))) {
      setUrl(text)
    }
  }, [])

  return (
    <section className="download-box">
      <h2>Add download</h2>
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
        <button type="submit" disabled={loading || !url.trim()}>
          {loading ? 'Adding…' : 'Add to queue'}
        </button>
      </form>
      {error && <p className="error">{error}</p>}
    </section>
  )
}
