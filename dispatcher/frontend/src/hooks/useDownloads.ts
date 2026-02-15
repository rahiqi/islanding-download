import { useState, useEffect, useCallback } from 'react'
import type { DownloadState, ProgressEvent } from '../types/api'
import { listDownloads, submitDownload, getProgressEventSource } from '../api/client'

export function useDownloads() {
  const [downloads, setDownloads] = useState<DownloadState[]>([])
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  const refresh = useCallback(async () => {
    try {
      const data = await listDownloads()
      setDownloads(data)
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load')
    }
  }, [])

  useEffect(() => {
    refresh()
    const es = getProgressEventSource()
    es.onmessage = (e) => {
      try {
        const ev: ProgressEvent = JSON.parse(e.data)
        setDownloads((prev) =>
          prev.map((d) =>
            d.downloadId === ev.downloadId
              ? {
                  ...d,
                  totalBytes: ev.totalBytes ?? d.totalBytes,
                  downloadedBytes: ev.downloadedBytes,
                  bytesPerSecond: ev.bytesPerSecond,
                  status: ev.status as DownloadState['status'],
                  agentId: ev.agentId ?? d.agentId,
                  errorMessage: ev.message ?? d.errorMessage,
                  percentComplete:
                    (ev.totalBytes ?? 0) > 0
                      ? Math.min(100, (100 * ev.downloadedBytes) / (ev.totalBytes ?? 1))
                      : d.percentComplete,
                }
              : d
          )
        )
      } catch {
        // ignore parse errors
      }
    }
    es.onerror = () => {
      es.close()
      // Reconnect after a short delay
      setTimeout(() => refresh(), 2000)
    }
    return () => es.close()
  }, [refresh])

  const addDownload = useCallback(async (url: string) => {
    setLoading(true)
    setError(null)
    try {
      await submitDownload(url)
      await refresh()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Submit failed')
    } finally {
      setLoading(false)
    }
  }, [refresh])

  return { downloads, error, loading, addDownload, refresh }
}
