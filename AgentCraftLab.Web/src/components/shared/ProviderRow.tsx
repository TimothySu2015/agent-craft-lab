import { Eye, EyeOff, ChevronDown, ChevronRight, Loader2, Trash2 } from 'lucide-react'
import type { ProviderConfig } from '@/lib/providers'
import { cn } from '@/lib/utils'

export interface CredentialFieldState {
  apiKey: string
  endpoint: string
  model: string
  showKey: boolean
  saved: boolean
}

interface ProviderRowProps {
  provider: ProviderConfig
  cred: CredentialFieldState
  isExpanded: boolean
  onToggle: () => void
  onUpdate: (field: keyof CredentialFieldState, value: string | boolean) => void
  onSave: () => void
  onRemove: () => void
  saving?: boolean
  hasBorder: boolean
  t: (key: string) => string
}

export function ProviderRow({ provider, cred, isExpanded, onToggle, onUpdate, onSave, onRemove, saving, hasBorder, t }: ProviderRowProps) {
  const isConfigured = !!cred.saved
  return (
    <div className={cn(hasBorder && 'border-t border-border')}>
      <button onClick={onToggle}
        className="flex w-full items-center gap-3 px-4 py-3 hover:bg-accent/30 transition-colors cursor-pointer">
        {isExpanded ? <ChevronDown size={14} className="text-muted-foreground shrink-0" /> : <ChevronRight size={14} className="text-muted-foreground shrink-0" />}
        <span className={cn('w-2 h-2 rounded-full shrink-0', isConfigured ? 'bg-green-500' : 'bg-muted-foreground/30')} />
        <span className="text-sm font-medium text-foreground flex-1 text-left">{provider.name}</span>
        {isConfigured && cred.model && <span className="text-[10px] text-muted-foreground font-mono">{cred.model}</span>}
        <span className={cn('rounded-full px-2 py-0.5 text-[10px] font-medium',
          isConfigured ? 'bg-green-500/10 text-green-400' : 'bg-muted/30 text-muted-foreground')}>
          {isConfigured ? t('studio:credentials.configured') : t('studio:credentials.notSet')}
        </span>
      </button>

      {isExpanded && (
        <div className="px-4 pb-4 pt-1 bg-accent/5">
          <div className="grid grid-cols-1 gap-3 max-w-lg ml-7">
            <div>
              <label className="block text-[10px] font-medium uppercase tracking-wider text-muted-foreground mb-1">API Key</label>
              <div className="relative">
                <input type={cred.showKey ? 'text' : 'password'} className="field-input pr-8 text-xs"
                  value={cred.apiKey} onChange={(e) => onUpdate('apiKey', e.target.value)}
                  placeholder={provider.keyOptional ? '(not required)' : provider.id === 'smtp' ? 'password' : 'sk-...'} />
                <button onClick={() => onUpdate('showKey', !cred.showKey)}
                  className="absolute right-2 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground cursor-pointer">
                  {cred.showKey ? <EyeOff size={13} /> : <Eye size={13} />}
                </button>
              </div>
            </div>
            {provider.needsEndpoint && (
              <div>
                <label className="block text-[10px] font-medium uppercase tracking-wider text-muted-foreground mb-1">
                  {provider.id === 'smtp' ? 'Host:Port' : 'Endpoint'}
                </label>
                <input type="text" className="field-input text-xs" value={cred.endpoint}
                  onChange={(e) => onUpdate('endpoint', e.target.value)} placeholder={provider.defaultEndpoint ?? 'https://...'} />
              </div>
            )}
            {provider.models.length > 0 && (
              <div>
                <label className="block text-[10px] font-medium uppercase tracking-wider text-muted-foreground mb-1">Default Model</label>
                <select className="field-input text-xs" value={cred.model} onChange={(e) => onUpdate('model', e.target.value)}>
                  {provider.models.map((m) => <option key={m} value={m}>{m}</option>)}
                </select>
              </div>
            )}
            <div className="flex items-center gap-2 pt-1">
              <button onClick={onSave} disabled={saving}
                className="rounded-md bg-primary px-3 py-1.5 text-[11px] font-medium text-primary-foreground hover:opacity-90 disabled:opacity-50 cursor-pointer flex items-center gap-1">
                {saving && <Loader2 size={11} className="animate-spin" />}
                {t('common:save')}
              </button>
              {cred.saved && (
                <button onClick={onRemove} disabled={saving}
                  className="rounded-md border border-red-500/30 px-3 py-1.5 text-[11px] font-medium text-red-400 hover:bg-red-500/10 disabled:opacity-50 cursor-pointer flex items-center gap-1">
                  <Trash2 size={11} />
                  {t('common:remove')}
                </button>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
