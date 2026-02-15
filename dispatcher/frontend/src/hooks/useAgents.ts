import { useState, useEffect, useCallback } from 'react'
import type { AgentInfo } from '../types/api'
import { listAgents } from '../api/client'

export function useAgents() {
  const [agents, setAgents] = useState<AgentInfo[]>([])
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    try {
      const data = await listAgents()
      setAgents(data)
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load agents')
    }
  }, [])

  useEffect(() => {
    refresh()
    const interval = setInterval(refresh, 10000)
    return () => clearInterval(interval)
  }, [refresh])

  return { agents, error, refresh }
}
