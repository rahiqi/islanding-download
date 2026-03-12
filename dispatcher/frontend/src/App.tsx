import { DownloadBox } from './components/DownloadBox'
import { AgentList } from './components/AgentList'
import { DownloadList } from './components/DownloadList'
import { useDownloads } from './hooks/useDownloads'
import { useAgents } from './hooks/useAgents'
import './App.css'

function App() {
  const { downloads, error: downloadError, loading, addDownload, refresh } = useDownloads()
  const { agents, error: agentsError } = useAgents()

  return (
    <div className="app">
      <header>
        <h1>Download portal</h1>
        <p>Drop a URL — any connected agent will pick it up and download.</p>
      </header>
      <main>
        <DownloadBox agents={agents} onSubmit={addDownload} loading={loading} error={downloadError} />
        <AgentList agents={agents} error={agentsError} />
        <DownloadList downloads={downloads} onRefresh={refresh} />
      </main>
    </div>
  )
}

export default App
