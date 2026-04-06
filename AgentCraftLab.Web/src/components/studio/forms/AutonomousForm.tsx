import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Wrench, Zap, Plus, X as XIcon } from 'lucide-react'
import { Field } from '../PropertiesPanel'
import { PROVIDERS, getModelsForProvider } from '@/lib/providers'
import { useCredentialStore } from '@/stores/credential-store'
import { ToolPickerDialog } from './ToolPickerDialog'
import { SkillPickerDialog } from './SkillPickerDialog'
import { ExpandableTextarea } from '@/components/shared/ExpandableTextarea'
import type { AutonomousNodeData, NodeData } from '@/types/workflow'

interface Props {
  data: AutonomousNodeData
  onUpdate: (partial: Partial<NodeData>) => void
}

export function AutonomousForm({ data, onUpdate }: Props) {
  const { t } = useTranslation('studio')
  const credentials = useCredentialStore((s) => s.credentials)
  const hasKey = (id: string) => !!credentials[id]?.apiKey || !!credentials[id]?.saved
  const currentHasKey = hasKey(data.provider)
  const [showToolPicker, setShowToolPicker] = useState(false)
  const [showSkillPicker, setShowSkillPicker] = useState(false)
  const [newMcp, setNewMcp] = useState('')
  const [newA2a, setNewA2a] = useState('')

  return (
    <>
      <Field label={t('form.provider')}>
        <select className="field-input" value={data.provider}
          onChange={(e) => onUpdate({ provider: e.target.value, model: getModelsForProvider(e.target.value)[0] ?? 'gpt-4o-mini' })}>
          {PROVIDERS.map((p) => (
            <option key={p.id} value={p.id}>{hasKey(p.id) ? '\u25CF' : '\u25CB'} {p.name}</option>
          ))}
        </select>
        {!currentHasKey && <p className="text-[9px] text-yellow-400 mt-0.5">{t('form.noKeyWarning')}</p>}
      </Field>

      <Field label={t('form.model')}>
        <select className="field-input" value={data.model} onChange={(e) => onUpdate({ model: e.target.value })}>
          {getModelsForProvider(data.provider).map((m) => <option key={m} value={m}>{m}</option>)}
        </select>
      </Field>

      <Field label={t('form.goal')}>
        <ExpandableTextarea
          value={data.instructions}
          onChange={(v) => onUpdate({ instructions: v })}
          rows={4}
          placeholder="Describe the goal for this autonomous agent..."
          label={`${data.name || 'Autonomous'} — Goal`}
          language="markdown"
        />
      </Field>

      <Field label={t('form.maxIterations')}>
        <input type="number" className="field-input" value={data.maxIterations} min={1} max={100}
          onChange={(e) => onUpdate({ maxIterations: Number(e.target.value) })} />
      </Field>

      <Field label={t('form.maxOutputTokens')}>
        <input type="number" className="field-input" value={data.maxOutputTokens ?? 200000} placeholder="200000"
          onChange={(e) => onUpdate({ maxOutputTokens: Number(e.target.value) })} />
        <p className="text-[8px] text-muted-foreground mt-0.5">Token budget for the entire ReAct session</p>
      </Field>

      {/* Tools */}
      <Field label={t('form.tools')}>
        <div className="flex items-center gap-2">
          <button onClick={() => setShowToolPicker(true)}
            className="flex items-center gap-1 rounded-md border border-border bg-secondary px-2.5 py-1 text-[10px] text-muted-foreground hover:text-foreground hover:bg-accent transition-colors cursor-pointer">
            <Wrench size={11} /> {t('form.manage')}
          </button>
          {data.tools.length > 0 && <span className="text-[10px] text-blue-400">{data.tools.length} {t('form.selected')}</span>}
        </div>
        {data.tools.length > 0 && (
          <div className="flex flex-wrap gap-1 mt-1.5">
            {data.tools.map((id) => <span key={id} className="rounded bg-blue-500/10 border border-blue-500/20 px-1.5 py-0.5 text-[9px] text-blue-400 font-mono">{id}</span>)}
          </div>
        )}
        <ToolPickerDialog open={showToolPicker} selected={data.tools} onClose={() => setShowToolPicker(false)} onApply={(tools) => onUpdate({ tools })} />
      </Field>

      {/* Skills */}
      <Field label={t('form.skills')}>
        <div className="flex items-center gap-2">
          <button onClick={() => setShowSkillPicker(true)}
            className="flex items-center gap-1 rounded-md border border-border bg-secondary px-2.5 py-1 text-[10px] text-muted-foreground hover:text-foreground hover:bg-accent transition-colors cursor-pointer">
            <Zap size={11} /> {t('form.manage')}
          </button>
          {(data.skills?.length ?? 0) > 0 && <span className="text-[10px] text-violet-400">{data.skills.length} {t('form.selected')}</span>}
        </div>
        {(data.skills?.length ?? 0) > 0 && (
          <div className="flex flex-wrap gap-1 mt-1.5">
            {data.skills.map((id) => <span key={id} className="rounded bg-violet-500/10 border border-violet-500/20 px-1.5 py-0.5 text-[9px] text-violet-400 font-mono">{id}</span>)}
          </div>
        )}
        <SkillPickerDialog open={showSkillPicker} selected={data.skills ?? []} onClose={() => setShowSkillPicker(false)} onApply={(skills) => onUpdate({ skills })} />
      </Field>

      {/* MCP Servers */}
      <Field label={t('form.mcpServers')}>
        <div className="flex gap-1.5 mb-1.5">
          <input className="field-input flex-1 text-[10px]" value={newMcp} onChange={(e) => setNewMcp(e.target.value)}
            placeholder="http://localhost:3001/mcp" onKeyDown={(e) => {
              if (e.key === 'Enter' && newMcp.trim()) {
                onUpdate({ mcpServers: [...(data.mcpServers ?? []), newMcp.trim()] })
                setNewMcp('')
              }
            }} />
          <button onClick={() => { if (newMcp.trim()) { onUpdate({ mcpServers: [...(data.mcpServers ?? []), newMcp.trim()] }); setNewMcp('') } }}
            className="rounded-md border border-border bg-secondary px-2 py-1 text-muted-foreground hover:text-foreground cursor-pointer"><Plus size={12} /></button>
        </div>
        {(data.mcpServers ?? []).map((url, i) => (
          <div key={`${url}-${i}`} className="flex items-center gap-1 mb-0.5">
            <span className="text-[9px] text-teal-400 font-mono truncate flex-1">{url}</span>
            <button onClick={() => onUpdate({ mcpServers: (data.mcpServers ?? []).filter((_, j) => j !== i) })}
              className="text-muted-foreground hover:text-red-400 cursor-pointer shrink-0"><XIcon size={11} /></button>
          </div>
        ))}
      </Field>

      {/* A2A Agents */}
      <Field label={t('form.a2aAgents')}>
        <div className="flex gap-1.5 mb-1.5">
          <input className="field-input flex-1 text-[10px]" value={newA2a} onChange={(e) => setNewA2a(e.target.value)}
            placeholder="http://localhost:5000/.well-known/agent.json" onKeyDown={(e) => {
              if (e.key === 'Enter' && newA2a.trim()) {
                onUpdate({ a2AAgents: [...(data.a2AAgents ?? []), newA2a.trim()] })
                setNewA2a('')
              }
            }} />
          <button onClick={() => { if (newA2a.trim()) { onUpdate({ a2AAgents: [...(data.a2AAgents ?? []), newA2a.trim()] }); setNewA2a('') } }}
            className="rounded-md border border-border bg-secondary px-2 py-1 text-muted-foreground hover:text-foreground cursor-pointer"><Plus size={12} /></button>
        </div>
        {(data.a2AAgents ?? []).map((url, i) => (
          <div key={`${url}-${i}`} className="flex items-center gap-1 mb-0.5">
            <span className="text-[9px] text-orange-400 font-mono truncate flex-1">{url}</span>
            <button onClick={() => onUpdate({ a2AAgents: (data.a2AAgents ?? []).filter((_, j) => j !== i) })}
              className="text-muted-foreground hover:text-red-400 cursor-pointer shrink-0"><XIcon size={11} /></button>
          </div>
        ))}
      </Field>
    </>
  )
}
