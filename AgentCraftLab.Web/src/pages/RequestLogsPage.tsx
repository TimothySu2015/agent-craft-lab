import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { BarChart3 } from 'lucide-react'

interface Summary {
  totalCalls: number;
  successCount: number;
  errorCount: number;
  avgResponseMs: number;
}

interface LogEntry {
  id: string;
  timestamp: string;
  protocol: string;
  workflowKey: string;
  message: string;
  elapsedMs: number;
  statusCode: number;
}

export function RequestLogsPage() {
  const { t } = useTranslation(['studio', 'common'])
  const [timeRange, setTimeRange] = useState('24h')
  const [protocol, setProtocol] = useState('all')
  const [summary, setSummary] = useState<Summary>({ totalCalls: 0, successCount: 0, errorCount: 0, avgResponseMs: 0 })
  const [logs, setLogs] = useState<LogEntry[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    const from = new Date()
    if (timeRange === '1h') from.setHours(from.getHours() - 1)
    else if (timeRange === '24h') from.setDate(from.getDate() - 1)
    else if (timeRange === '7d') from.setDate(from.getDate() - 7)
    else from.setDate(from.getDate() - 30)

    const protocolParam = protocol === 'all' ? '' : `&protocol=${protocol}`

    setLoading(true)
    Promise.all([
      fetch(`/api/analytics/summary?from=${from.toISOString()}`).then((r) => r.json()).catch(() => null),
      fetch(`/api/analytics/logs?from=${from.toISOString()}${protocolParam}&limit=100`).then((r) => r.json()).catch(() => []),
    ]).then(([s, l]) => {
      if (s) setSummary(s)
      if (Array.isArray(l)) setLogs(l)
    }).finally(() => setLoading(false))
  }, [timeRange, protocol])

  const successRate = (summary?.totalCalls ?? 0) > 0
    ? (((summary?.successCount ?? 0) / summary.totalCalls) * 100).toFixed(1)
    : '0'

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      {/* Top Bar */}
      <div className="flex items-center justify-between border-b border-border bg-card px-5 shrink-0 h-[41px]">
        <div className="flex items-center gap-2">
          <BarChart3 size={16} className="text-violet-400" />
          <h1 className="text-sm font-semibold text-foreground">{t('common:nav.logs')}</h1>
        </div>
        <div className="flex items-center gap-2">
          <select className="field-input text-[11px] py-1 px-2" style={{ width: 140 }} value={timeRange} onChange={(e) => setTimeRange(e.target.value)}>
            <option value="1h">{t('logs.last1h')}</option>
            <option value="24h">{t('logs.last24h')}</option>
            <option value="7d">{t('logs.last7d')}</option>
            <option value="30d">{t('logs.last30d')}</option>
          </select>
          <select className="field-input text-[11px] py-1 px-2" style={{ width: 140 }} value={protocol} onChange={(e) => setProtocol(e.target.value)}>
            <option value="all">{t('logs.allProtocols')}</option>
            <option value="api">API</option>
            <option value="a2a">A2A</option>
            <option value="mcp">MCP</option>
            <option value="teams">Teams</option>
          </select>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto p-6">
        {/* Stat Cards */}
        <div className="grid grid-cols-4 gap-3 mb-6">
          <StatCard value={(summary?.totalCalls ?? 0).toString()} label={t('logs.totalCalls')} color="text-blue-400" />
          <StatCard value={`${successRate}%`} label={t('logs.successRate')} color="text-green-400" />
          <StatCard value={(summary?.avgResponseMs ?? 0) > 0 ? `${(summary.avgResponseMs / 1000).toFixed(1)}s` : '—'} label={t('logs.avgResponse')} color="text-yellow-400" />
          <StatCard value={(summary?.errorCount ?? 0).toString()} label={t('logs.errors')} color="text-red-400" />
        </div>

        {/* Log Table */}
        <div className="overflow-x-auto rounded-lg border border-border">
          <table className="w-full">
            <thead>
              <tr className="border-b border-border bg-card">
                <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">{t('logs.colTime')}</th>
                <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">{t('logs.colProtocol')}</th>
                <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">{t('logs.colWorkflow')}</th>
                <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">{t('logs.colMessage')}</th>
                <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">{t('logs.colMs')}</th>
                <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">{t('logs.colStatus')}</th>
              </tr>
            </thead>
            <tbody>
              {loading && (
                <tr><td colSpan={6} className="px-3 py-8 text-center text-xs text-muted-foreground">{t('common:loading')}</td></tr>
              )}
              {!loading && logs.length === 0 && (
                <tr><td colSpan={6} className="px-3 py-8 text-center text-xs text-muted-foreground">{t('logs.empty')}</td></tr>
              )}
              {logs.map((log, i) => (
                <tr key={log.id ?? i} className="border-b border-border hover:bg-secondary/50 transition-colors">
                  <td className="px-3 py-2 text-[11px] text-muted-foreground whitespace-nowrap">
                    {new Date(log.timestamp).toLocaleTimeString()}
                  </td>
                  <td className="px-3 py-2">
                    <span className="rounded bg-secondary px-1.5 py-0.5 text-[10px] text-muted-foreground border border-border">
                      {log.protocol}
                    </span>
                  </td>
                  <td className="px-3 py-2 text-xs text-foreground">{log.workflowKey}</td>
                  <td className="px-3 py-2 text-xs text-muted-foreground max-w-[200px] truncate">{log.message}</td>
                  <td className="px-3 py-2 text-xs font-mono text-muted-foreground">{log.elapsedMs?.toLocaleString()}</td>
                  <td className="px-3 py-2 text-xs">
                    <span className={log.statusCode >= 400 ? 'text-red-400' : 'text-green-400'}>
                      {log.statusCode}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  )
}

function StatCard({ value, label, color }: { value: string; label: string; color: string }) {
  return (
    <div className="rounded-lg border border-border bg-card p-4 text-center">
      <div className={`text-2xl font-bold ${color}`}>{value}</div>
      <div className="text-[10px] text-muted-foreground mt-0.5">{label}</div>
    </div>
  )
}
