/**
 * ApiKeysPage — API Key 管理：建立 / 列表 / 撤銷，含一次性 raw key 顯示 + 使用說明。
 * 對應後端 /api/keys（ApiKeyManagementExtensions）。
 */
import { useState, useEffect, useCallback } from 'react'
import { useConfirmDialog } from '@/components/shared/ConfirmDialog'
import { useTranslation } from 'react-i18next'
import { Key, Plus, Ban, Copy, Check, AlertTriangle, X } from 'lucide-react'
import { api, type ApiKeyInfo } from '@/lib/api'
import { notify } from '@/lib/notify'

function getStatus(k: ApiKeyInfo): 'active' | 'expired' | 'revoked' {
  if (k.isRevoked) return 'revoked'
  if (k.expiresAt && new Date(k.expiresAt) < new Date()) return 'expired'
  return 'active'
}

const STATUS_STYLE: Record<string, string> = {
  active: 'bg-emerald-500/15 text-emerald-400',
  expired: 'bg-yellow-500/15 text-yellow-400',
  revoked: 'bg-red-500/15 text-red-400',
}

function fmtDate(iso: string | null, fallback: string): string {
  if (!iso) return fallback
  return new Date(iso).toLocaleDateString()
}

function fmtDateTime(iso: string | null, fallback: string): string {
  if (!iso) return fallback
  const d = new Date(iso)
  return `${d.toLocaleDateString()} ${d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`
}

export function ApiKeysPage() {
  const { t } = useTranslation(['studio', 'common'])
  const { t: tn } = useTranslation('notifications')
  const { confirm, confirmDialog } = useConfirmDialog()
  const [keys, setKeys] = useState<ApiKeyInfo[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // create form
  const [showForm, setShowForm] = useState(false)
  const [name, setName] = useState('')
  const [scope, setScope] = useState('')
  const [expiresAt, setExpiresAt] = useState('')
  const [creating, setCreating] = useState(false)

  // one-time raw key display
  const [rawKey, setRawKey] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  const fetchKeys = useCallback(async () => {
    try {
      const data = await api.apiKeys.list()
      setKeys(data)
    } catch (err) {
      console.error('Failed to load API keys:', err)
      notify.error(tn('loadFailed.apiKeys'))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { fetchKeys() }, [fetchKeys])

  const handleCreate = async () => {
    if (!name.trim()) { setError(t('apiKeys.nameRequired')); return }
    setCreating(true)
    setError(null)
    setRawKey(null)
    try {
      const result = await api.apiKeys.create({
        name: name.trim(),
        scopedWorkflowIds: scope.trim() || undefined,
        expiresAt: expiresAt || undefined,
      })
      setRawKey(result.rawKey)
      setName('')
      setScope('')
      setExpiresAt('')
      setShowForm(false)
      await fetchKeys()
    } catch (err: any) {
      setError(err?.error ?? err?.message ?? 'Failed to create API key')
    } finally {
      setCreating(false)
    }
  }

  const handleRevoke = async (id: string) => {
    if (!await confirm(t('apiKeys.confirmRevoke'))) return
    try {
      await api.apiKeys.revoke(id)
      await fetchKeys()
    } catch (err) {
      console.error('Failed to revoke API key:', err)
      notify.error(tn('revokeFailed.apiKey'), { description: (err as any)?.message })
    }
  }

  const handleCopy = async () => {
    if (!rawKey) return
    await navigator.clipboard.writeText(rawKey)
    setCopied(true)
    setTimeout(() => setCopied(false), 3000)
  }

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      {/* Top Bar */}
      <div className="flex items-center justify-between border-b border-border bg-card px-5 shrink-0 h-[41px]">
        <div className="flex items-center gap-2">
          <Key size={16} className="text-yellow-400" />
          <h1 className="text-sm font-semibold text-foreground">{t('common:nav.apiKeys')}</h1>
        </div>
        <button
          onClick={() => { setShowForm(true); setError(null) }}
          className="flex items-center gap-1 rounded-md bg-blue-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-blue-500 transition-colors cursor-pointer"
        >
          <Plus size={13} /> {t('common:create')}
        </button>
      </div>

      <div className="flex-1 overflow-y-auto p-5 space-y-4">
        {/* Error */}
        {error && (
          <div className="flex items-center justify-between rounded-md border border-red-500/30 bg-red-500/10 px-3 py-2 text-xs text-red-400">
            <span>{error}</span>
            <button onClick={() => setError(null)} className="cursor-pointer"><X size={14} /></button>
          </div>
        )}

        {/* New Raw Key Banner */}
        {rawKey && (
          <div className="rounded-md border border-yellow-500/30 bg-yellow-500/10 px-4 py-3">
            <div className="flex items-center gap-2 mb-2">
              <AlertTriangle size={14} className="text-yellow-400" />
              <span className="text-xs font-semibold text-yellow-400">{t('apiKeys.createdBanner')}</span>
            </div>
            <div className="flex items-center gap-2">
              <code className="flex-1 rounded bg-background px-3 py-2 text-xs font-mono text-foreground break-all">{rawKey}</code>
              <button
                onClick={handleCopy}
                className="rounded-md border border-border px-3 py-2 text-xs text-muted-foreground hover:text-foreground transition-colors cursor-pointer shrink-0"
              >
                {copied ? <Check size={13} className="text-green-400" /> : <Copy size={13} />}
              </button>
            </div>
          </div>
        )}

        {/* Create Form */}
        {showForm && (
          <div className="rounded-lg border border-border bg-card">
            <div className="border-b border-border px-4 py-2.5">
              <h2 className="text-xs font-semibold text-foreground">{t('apiKeys.createTitle')}</h2>
            </div>
            <div className="p-4">
              <div className="grid grid-cols-1 md:grid-cols-4 gap-3">
                <div>
                  <label className="block text-[10px] text-muted-foreground mb-1">{t('apiKeys.fieldName')}</label>
                  <input
                    className="field-input"
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    placeholder={t('apiKeys.fieldNamePlaceholder')}
                    autoFocus
                  />
                </div>
                <div className="md:col-span-2">
                  <label className="block text-[10px] text-muted-foreground mb-1">{t('apiKeys.fieldScope')}</label>
                  <input
                    className="field-input font-mono text-[10px]"
                    value={scope}
                    onChange={(e) => setScope(e.target.value)}
                    placeholder={t('apiKeys.fieldScopePlaceholder')}
                  />
                </div>
                <div>
                  <label className="block text-[10px] text-muted-foreground mb-1">{t('apiKeys.fieldExpires')}</label>
                  <input
                    type="date"
                    className="field-input"
                    value={expiresAt}
                    onChange={(e) => setExpiresAt(e.target.value)}
                  />
                </div>
              </div>
              <div className="flex gap-2 mt-3">
                <button
                  onClick={handleCreate}
                  disabled={creating || !name.trim()}
                  className="rounded-md bg-blue-600 px-4 py-1.5 text-xs font-semibold text-white hover:bg-blue-500 disabled:opacity-50 transition-colors cursor-pointer"
                >
                  {creating ? '...' : t('common:create')}
                </button>
                <button
                  onClick={() => setShowForm(false)}
                  className="rounded-md border border-border px-4 py-1.5 text-xs text-muted-foreground hover:text-foreground transition-colors cursor-pointer"
                >
                  {t('common:cancel')}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Key List */}
        {loading ? (
          <p className="text-xs text-muted-foreground text-center py-8">{t('common:loading')}</p>
        ) : keys.length === 0 ? (
          <p className="text-xs text-muted-foreground text-center py-8">{t('apiKeys.empty')}</p>
        ) : (
          <div className="overflow-x-auto rounded-lg border border-border">
            <table className="w-full">
              <thead>
                <tr className="border-b border-border bg-card">
                  <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">{t('apiKeys.colName')}</th>
                  <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">{t('apiKeys.colKeyPrefix')}</th>
                  <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">{t('apiKeys.colScope')}</th>
                  <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">{t('apiKeys.colStatus')}</th>
                  <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">{t('apiKeys.colLastUsed')}</th>
                  <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">{t('apiKeys.colExpires')}</th>
                  <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground">{t('apiKeys.colCreated')}</th>
                  <th className="px-3 py-2 text-left text-[10px] font-medium uppercase text-muted-foreground"></th>
                </tr>
              </thead>
              <tbody>
                {keys.map((k) => {
                  const status = getStatus(k)
                  return (
                    <tr key={k.id} className={`border-b border-border hover:bg-secondary/50 transition-colors ${status === 'revoked' ? 'opacity-50' : ''}`}>
                      <td className="px-3 py-2.5 text-xs font-medium text-foreground">{k.name}</td>
                      <td className="px-3 py-2.5">
                        <code className="text-[10px] text-blue-400">{k.keyPrefix}...</code>
                      </td>
                      <td className="px-3 py-2.5 text-[10px] text-muted-foreground">
                        {k.scopedWorkflowIds || t('apiKeys.allWorkflows')}
                      </td>
                      <td className="px-3 py-2.5">
                        <span className={`inline-block rounded px-1.5 py-0.5 text-[10px] font-medium ${STATUS_STYLE[status]}`}>
                          {t(`apiKeys.status${status.charAt(0).toUpperCase() + status.slice(1)}`)}
                        </span>
                      </td>
                      <td className="px-3 py-2.5 text-[10px] text-muted-foreground">{fmtDateTime(k.lastUsedAt, t('apiKeys.never'))}</td>
                      <td className="px-3 py-2.5 text-[10px] text-muted-foreground">{fmtDate(k.expiresAt, t('apiKeys.never'))}</td>
                      <td className="px-3 py-2.5 text-[10px] text-muted-foreground">{fmtDate(k.createdAt, '-')}</td>
                      <td className="px-3 py-2.5">
                        {!k.isRevoked && (
                          <button
                            onClick={() => handleRevoke(k.id)}
                            title={t('apiKeys.revoke')}
                            className="rounded p-1 text-muted-foreground hover:text-red-400 hover:bg-secondary transition-colors cursor-pointer"
                          >
                            <Ban size={13} />
                          </button>
                        )}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}

        {/* Usage Instructions */}
        <div className="rounded-lg border border-border bg-card p-4">
          <h3 className="text-xs font-semibold text-foreground mb-2">{t('apiKeys.usage')}</h3>
          <p className="text-[10px] text-muted-foreground mb-2">{t('apiKeys.usageDesc')}</p>
          <code className="block rounded bg-background px-3 py-2 text-[10px] font-mono text-blue-400 break-all">
            curl -X POST https://your-host/api/&lt;workflow-id&gt; -H "X-Api-Key: ack_..." -d '{"{"}message":"hello"{"}"}'
          </code>
          <p className="mt-2 text-[10px] text-muted-foreground">
            {t('apiKeys.usageBearer', { header: 'Authorization: Bearer ack_...' })}
          </p>
        </div>
      </div>
      {confirmDialog}
    </div>
  )
}
