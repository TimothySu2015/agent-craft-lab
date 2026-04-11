import { useTranslation } from 'react-i18next'
import { Settings, User, Palette, Cpu, KeyRound, Wallet, Wrench, Shield } from 'lucide-react'
import { CLOUD_PROVIDERS, LOCAL_PROVIDERS, TOOL_CREDENTIAL_PROVIDERS } from '@/lib/providers'
import { useSettingsStore } from '@/stores/settings-store'
import { useCredentialFields } from '@/hooks/useCredentialFields'
import { ProviderRow } from '@/components/shared/ProviderRow'
import { DataSourceSection } from '@/components/settings/DataSourceSection'
import { cn } from '@/lib/utils'
import { useAppConfigStore } from '@/stores/app-config-store'

function SectionCard({ icon: Icon, title, description, children, iconColor = 'text-primary' }: {
  icon: React.ElementType; title: string; description?: string; children: React.ReactNode; iconColor?: string
}) {
  return (
    <div className="rounded-lg border border-border bg-card">
      <div className="px-5 py-3.5 border-b border-border/50">
        <div className="flex items-center gap-2">
          <Icon size={15} className={iconColor} />
          <h2 className="text-sm font-semibold text-foreground">{title}</h2>
        </div>
        {description && <p className="text-xs text-muted-foreground mt-1">{description}</p>}
      </div>
      <div className="px-5 py-4">{children}</div>
    </div>
  )
}

function FieldLabel({ children }: { children: React.ReactNode }) {
  return <label className="block text-[10px] text-muted-foreground mb-1">{children}</label>
}

export function SettingsPage() {
  const { t, i18n } = useTranslation(['studio', 'common'])
  const s = useSettingsStore()
  const { creds, expandedId, setExpandedId, updateCred, handleSave, handleRemove, savingId, storedCredentials } = useCredentialFields()
  const credentialMode = useAppConfigStore((s) => s.credentialMode)

  const providerGroups = [
    { labelKey: 'studio:credentials.cloudProviders', providers: CLOUD_PROVIDERS },
    { labelKey: 'studio:credentials.localProviders', providers: LOCAL_PROVIDERS },
    { labelKey: 'studio:credentials.searchTools', providers: TOOL_CREDENTIAL_PROVIDERS },
  ]

  const handleLocaleChange = (locale: 'en' | 'zh-TW' | 'ja') => {
    s.setLocale(locale)
    i18n.changeLanguage(locale)
  }

  // Configured LLM providers for Default Model dropdown
  const allProviders = [...CLOUD_PROVIDERS, ...LOCAL_PROVIDERS]
  const configuredProviders = allProviders.filter((p) => creds[p.id]?.apiKey || storedCredentials[p.id]?.apiKey || storedCredentials[p.id]?.saved)

  const handleProviderChange = (providerId: string) => {
    s.setDefaultProvider(providerId)
    const provider = allProviders.find((p) => p.id === providerId)
    if (provider && provider.models.length > 0) {
      s.setDefaultModel(provider.models[0])
    }
  }

  const selectedProviderModels = allProviders.find((p) => p.id === s.defaultProvider)?.models ?? []

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      {/* Top Bar */}
      <div className="flex items-center gap-2 border-b border-border bg-card px-5 shrink-0 h-[41px]">
        <Settings size={16} className="text-primary" />
        <h1 className="text-sm font-semibold text-foreground">{t('studio:personal.title')}</h1>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-y-auto">
        <div className="max-w-5xl mx-auto px-6 py-5 space-y-4">

          {/* ─── Profile ─── */}
          <SectionCard icon={User} title={t('studio:personal.profile')} iconColor="text-blue-400">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 max-w-lg">
              <div>
                <FieldLabel>{t('studio:personal.displayName')}</FieldLabel>
                <input type="text" className="field-input text-xs"
                  value={s.displayName}
                  onChange={(e) => s.setDisplayName(e.target.value)}
                  placeholder={t('studio:personal.displayNamePlaceholder')} />
              </div>
              <div>
                <FieldLabel>{t('studio:personal.role')}</FieldLabel>
                <input type="text" className="field-input text-xs" value={t('studio:personal.localUser')} disabled />
              </div>
            </div>
          </SectionCard>

          {/* ─── Appearance ─── */}
          <SectionCard icon={Palette} title={t('studio:personal.appearance')} iconColor="text-purple-400">
            <div className="space-y-4">
              {/* Language */}
              <div>
                <FieldLabel>{t('studio:personal.language')}</FieldLabel>
                <div className="flex gap-2">
                  {([['en', 'English'], ['zh-TW', '繁體中文'], ['ja', '日本語']] as const).map(([code, label]) => (
                    <button key={code}
                      onClick={() => handleLocaleChange(code)}
                      className={cn(
                        'rounded-md border px-4 py-1.5 text-xs transition-colors cursor-pointer',
                        s.locale === code
                          ? 'border-primary bg-primary/10 text-primary font-medium'
                          : 'border-border text-muted-foreground hover:border-muted-foreground/50',
                      )}>
                      {label}
                    </button>
                  ))}
                </div>
              </div>

              {/* Theme */}
              <div>
                <FieldLabel>{t('studio:personal.theme')}</FieldLabel>
                <div className="flex gap-2">
                  {([
                    ['dark', t('studio:personal.themeDark'), false],
                    ['light', t('studio:personal.themeLight'), false],
                  ] as const).map(([value, label, comingSoon]) => (
                    <button key={value}
                      onClick={() => !comingSoon && s.setTheme(value as 'dark' | 'light' | 'system')}
                      className={cn(
                        'rounded-md border px-4 py-1.5 text-xs transition-colors',
                        s.theme === value && !comingSoon
                          ? 'border-primary bg-primary/10 text-primary font-medium'
                          : 'border-border text-muted-foreground',
                        comingSoon ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer hover:border-muted-foreground/50',
                      )}>
                      {label}
                      {comingSoon && (
                        <span className="ml-1.5 rounded bg-muted/50 px-1 py-0.5 text-[9px]">
                          {t('studio:personal.comingSoon')}
                        </span>
                      )}
                    </button>
                  ))}
                </div>
              </div>
            </div>
          </SectionCard>

          {/* ─── Default Model ─── */}
          <SectionCard icon={Cpu} title={t('studio:personal.defaultModel')}
            description={t('studio:personal.defaultModelDesc')} iconColor="text-green-400">
            {configuredProviders.length === 0 ? (
              <p className="text-xs text-amber-400">{t('studio:personal.noProviderConfigured')}</p>
            ) : (
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 max-w-lg">
                <div>
                  <FieldLabel>{t('studio:personal.defaultProvider')}</FieldLabel>
                  <select className="field-input text-xs" value={s.defaultProvider}
                    onChange={(e) => handleProviderChange(e.target.value)}>
                    <option value="">{t('studio:personal.selectProvider')}</option>
                    {configuredProviders.map((p) => (
                      <option key={p.id} value={p.id}>{p.name}</option>
                    ))}
                  </select>
                </div>
                {s.defaultProvider && selectedProviderModels.length > 0 && (
                  <div>
                    <FieldLabel>{t('studio:form.model')}</FieldLabel>
                    <select className="field-input text-xs" value={s.defaultModel}
                      onChange={(e) => s.setDefaultModel(e.target.value)}>
                      {selectedProviderModels.map((m) => (
                        <option key={m} value={m}>{m}</option>
                      ))}
                    </select>
                  </div>
                )}
              </div>
            )}
          </SectionCard>

          {/* ─── Credentials ─── */}
          <SectionCard icon={KeyRound} title={t('studio:personal.credentials')}
            description={t('studio:personal.credentialsDesc')} iconColor="text-amber-400">
            {credentialMode === 'browser' && (
              <div className="mb-3 rounded-md bg-amber-500/10 border border-amber-500/30 px-3 py-2 text-xs text-amber-300">
                {t('studio:credentials.browserModeNotice')}
              </div>
            )}
            <div className="flex items-center gap-2 mb-3">
              <Shield size={13} className="text-muted-foreground" />
              <p className="text-[11px] text-muted-foreground flex-1">{t('studio:credentials.description')}</p>
            </div>
            {providerGroups.map((group) => (
              <div key={group.labelKey} className="mb-3">
                <h3 className="text-[10px] font-semibold uppercase tracking-wider text-muted-foreground mb-1.5">{t(group.labelKey)}</h3>
                <div className="rounded-lg border border-border overflow-hidden">
                  {group.providers.map((provider, i) => (
                    <ProviderRow key={provider.id} provider={provider} cred={creds[provider.id]}
                      isExpanded={expandedId === provider.id}
                      onToggle={() => setExpandedId(expandedId === provider.id ? null : provider.id)}
                      onUpdate={(field, value) => updateCred(provider.id, field, value)}
                      onSave={() => handleSave(provider.id)}
                      onRemove={() => handleRemove(provider.id)}
                      saving={savingId === provider.id}
                      hasBorder={i > 0} t={t} />
                  ))}
                </div>
              </div>
            ))}
          </SectionCard>

          {/* ─── Data Sources ─── */}
          <DataSourceSection SectionCard={SectionCard} />

          {/* ─── Budget ─── */}
          <SectionCard icon={Wallet} title={t('studio:personal.budget')}
            description={t('studio:personal.budgetDesc')} iconColor="text-orange-400">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 max-w-lg">
              <div>
                <FieldLabel>{t('studio:personal.dailyTokenLimit')}</FieldLabel>
                <input type="number" className="field-input text-xs" min={0}
                  value={s.dailyTokenLimit || ''}
                  onChange={(e) => s.setDailyTokenLimit(parseInt(e.target.value) || 0)}
                  placeholder="0" />
                <p className="text-[10px] text-muted-foreground mt-0.5">{t('studio:personal.dailyTokenLimitDesc')}</p>
              </div>
              <div>
                <FieldLabel>{t('studio:personal.costAlert')}</FieldLabel>
                <input type="number" className="field-input text-xs" min={0} step={0.1}
                  value={s.costAlertThreshold || ''}
                  onChange={(e) => s.setCostAlertThreshold(parseFloat(e.target.value) || 0)}
                  placeholder="0" />
                <p className="text-[10px] text-muted-foreground mt-0.5">{t('studio:personal.costAlertDesc')}</p>
              </div>
            </div>
          </SectionCard>

          {/* ─── Advanced ─── */}
          <SectionCard icon={Wrench} title={t('studio:personal.advanced')} iconColor="text-gray-400">
            <div className="space-y-4 max-w-lg">
              <div>
                <FieldLabel>{t('studio:personal.proxy')}</FieldLabel>
                <input type="text" className="field-input text-xs"
                  value={s.httpProxy}
                  onChange={(e) => s.setHttpProxy(e.target.value)}
                  placeholder={t('studio:personal.proxyPlaceholder')} />
              </div>
              <div className="flex items-center gap-6 text-xs text-muted-foreground">
                <span>{t('studio:personal.dataDir')}: <code className="bg-muted/30 rounded px-1.5 py-0.5 text-[10px]">./Data</code></span>
                <span>{t('studio:personal.version')}: <code className="bg-muted/30 rounded px-1.5 py-0.5 text-[10px]">1.0.0</code></span>
              </div>
            </div>
          </SectionCard>

        </div>
      </div>
    </div>
  )
}
