import { useState, useEffect, useCallback } from 'react'
import { getCurrentUser, login as apiLogin, logout as apiLogout } from '../api/client'

export function useAuth() {
  const [username, setUsername] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const user = await getCurrentUser()
      setUsername(user?.username ?? null)
    } catch {
      setUsername(null)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    refresh()
  }, [refresh])

  const login = useCallback(async (name: string) => {
    setError(null)
    try {
      const res = await apiLogin(name)
      setUsername(res.username)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Login failed')
      throw e
    }
  }, [])

  const logout = useCallback(async () => {
    await apiLogout()
    setUsername(null)
  }, [])

  return { username, loading, error, login, logout, refresh }
}
