import { useState } from 'react'
import { DownloadBox } from './components/DownloadBox'
import { AgentList } from './components/AgentList'
import { DownloadList } from './components/DownloadList'
import { useDownloads } from './hooks/useDownloads'
import { useAgents } from './hooks/useAgents'
import { useAuth } from './hooks/useAuth'
import './App.css'

function App() {
  const { downloads, error: downloadError, loading, addDownload, refresh } = useDownloads()
  const { agents, error: agentsError } = useAgents()
  const { username, loading: authLoading, error: authError, login, logout } = useAuth()
  const [loginName, setLoginName] = useState('')

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault()
    const name = loginName.trim()
    if (!name) return
    await login(name)
    setLoginName('')
  }

  return (
    <div className="app">
      <header>
        <div className="header-row">
          <div>
            <h1>Download portal</h1>
            <p>Drop a URL — any connected agent will pick it up and download.</p>
          </div>
          <div className="auth-box">
            {authLoading ? (
              <span className="muted">Loading…</span>
            ) : username ? (
              <>
                <span className="auth-user">Logged in as <strong>{username}</strong></span>
                <button type="button" className="btn-logout" onClick={() => logout()}>Logout</button>
              </>
            ) : (
              <form onSubmit={handleLogin} className="auth-form">
                <input
                  type="text"
                  placeholder="Your name"
                  value={loginName}
                  onChange={(e) => setLoginName(e.target.value)}
                  maxLength={64}
                  aria-label="Username"
                />
                <button type="submit">Login</button>
              </form>
            )}
          </div>
        </div>
        {authError && <p className="error">{authError}</p>}
      </header>
      <main>
        <DownloadBox agents={agents} onSubmit={addDownload} loading={loading} error={downloadError} isLoggedIn={!!username} />
        <AgentList agents={agents} error={agentsError} />
        <DownloadList downloads={downloads} onRefresh={refresh} />
      </main>
    </div>
  )
}

export default App
