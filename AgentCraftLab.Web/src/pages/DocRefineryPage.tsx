import { useState, useEffect, useCallback, useRef, useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import { Factory, Plus, Trash2, Upload, FileText, Eye, FileOutput, Settings, X, Download, Copy, ChevronDown, RefreshCw, Clock } from 'lucide-react'
import Markdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { markdownComponents } from '@/components/shared/markdown-components'
import { JsonView, darkStyles } from 'react-json-view-lite'
import 'react-json-view-lite/dist/index.css'
import { api } from '@/lib/api'
import type { RefineryProject, RefineryFileDoc, RefineryOutputDoc, SchemaTemplateSummary, FieldChallengeDoc } from '@/lib/api'

// ── Shared utilities ──

function safeJsonParse<T>(str: string, fallback: T): T {
  try { return JSON.parse(str) as T } catch { return fallback }
}

function downloadAsFile(content: string, filename: string) {
  const blob = new Blob([content], { type: 'text/plain' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url; a.download = filename; a.click()
  URL.revokeObjectURL(url)
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

function IndexStatusBadge({ status }: { status: string }) {
  const config: Record<string, { text: string; color: string }> = {
    Indexed: { text: '✅', color: 'text-green-400' },
    Indexing: { text: '🔄', color: 'text-blue-400' },
    Pending: { text: '⏳', color: 'text-yellow-400' },
    Failed: { text: '⚠️', color: 'text-red-400' },
    Skipped: { text: '⏭️', color: 'text-gray-400' },
  }
  const c = config[status] ?? { text: '?', color: 'text-gray-400' }
  return <span className={`text-xs ${c.color}`} title={status}>{c.text}</span>
}

function LogPanel({ logs }: { logs: string[] }) {
  return (
    <div className="mx-3 mb-2 max-h-32 space-y-0.5 overflow-y-auto rounded-md border border-border/50 bg-background/50 p-2">
      {logs.map((msg, i) => (
        <div key={i} className={`rounded px-2 py-0.5 text-[11px] ${
          msg.startsWith('Error') ? 'text-red-400'
            : msg.startsWith('Cleaned') || msg.startsWith('Indexed') || msg.startsWith('✅') ? 'text-green-400'
            : 'text-muted-foreground'
        }`}>
          {msg}
        </div>
      ))}
    </div>
  )
}

function FileLogCard({ fileName, status, logs, isExpanded, onToggle }: {
  fileName: string; status: string; logs: string[];
  isExpanded: boolean; onToggle: () => void
}) {
  return (
    <div className="mb-1">
      <div className="flex items-center justify-between rounded-md px-3 py-2 bg-secondary/30 cursor-pointer"
        onClick={onToggle}>
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm">{fileName}</p>
          <p className="text-xs text-muted-foreground">Processing...</p>
        </div>
        <div className="flex items-center gap-1.5">
          <IndexStatusBadge status={status} />
          <ChevronDown className={`h-3.5 w-3.5 text-muted-foreground transition-transform ${isExpanded ? 'rotate-180' : ''}`} />
        </div>
      </div>
      {isExpanded && logs.length > 0 && <LogPanel logs={logs} />}
    </div>
  )
}

// ── Section name mapping (for Challenge display) ──
const SECTION_LABELS: Record<string, string> = {
  document: '📄 Document',
  project_overview: '📋 Project Overview',
  stakeholders: '👥 Stakeholders',
  functional_requirements: '⚙️ Functional Requirements',
  non_functional_requirements: '🔒 Non-Functional Requirements',
  data_model: '🗄️ Data Model',
  api_endpoints: '🔌 API Endpoints',
  ui_screens: '🖥️ UI Screens',
  timeline: '📅 Timeline',
  budget: '💰 Budget',
  risks: '⚠️ Risks',
  glossary: '📖 Glossary',
  open_questions: '❓ Open Questions',
}

function ChallengePanel({ challenges }: { challenges: FieldChallengeDoc[] }) {
  const { t } = useTranslation(['studio'])
  const [expandedGroups, setExpandedGroups] = useState<Set<string>>(new Set())

  // 按區塊分組（取 field 的第一段作為 group key）
  const grouped = challenges.reduce<Record<string, FieldChallengeDoc[]>>((acc, c) => {
    const section = c.field.split('.')[0] ?? 'other'
    ;(acc[section] ??= []).push(c)
    return acc
  }, {})

  const toggleGroup = (key: string) => {
    setExpandedGroups(prev => {
      const next = new Set(prev)
      next.has(key) ? next.delete(key) : next.add(key)
      return next
    })
  }

  // 取得欄位的簡短名稱（去掉區塊前綴）
  const shortField = (field: string) => {
    const parts = field.split('.')
    return parts.slice(1).join('.') || parts[0]
  }

  return (
    <div className="mb-3">
      <div className="mb-2 text-xs font-medium text-purple-400">
        {t('docRefinery.output.challenges')} ({challenges.length})
      </div>
      <div className="space-y-1">
        {Object.entries(grouped).map(([section, items]) => (
          <div key={section} className="rounded-md border border-purple-500/20 overflow-hidden">
            {/* Group header — 可收折 */}
            <button className="flex w-full items-center justify-between bg-purple-500/5 px-3 py-2 text-xs text-purple-300 hover:bg-purple-500/10"
              onClick={() => toggleGroup(section)}>
              <span className="font-medium">{SECTION_LABELS[section] ?? section} ({items.length})</span>
              <ChevronDown className={`h-3.5 w-3.5 transition-transform ${expandedGroups.has(section) ? 'rotate-180' : ''}`} />
            </button>

            {/* Group content */}
            {expandedGroups.has(section) && (
              <div className="divide-y divide-purple-500/10">
                {items.map((c, i) => (
                  <div key={i} className={`px-3 py-2 text-xs ${
                    c.action === 'Reject' ? 'bg-red-500/5' : 'bg-purple-500/5'
                  }`}>
                    {/* Field + confidence */}
                    <div className="flex items-center gap-2 mb-1">
                      <span>{c.action === 'Reject' ? '❌' : '⚠️'}</span>
                      <span className="font-mono text-[10px] font-medium text-purple-300">{shortField(c.field)}</span>
                      <span className={`ml-auto rounded-full px-1.5 py-0.5 text-[9px] font-medium ${
                        c.confidence >= 0.8 ? 'bg-green-500/20 text-green-400'
                          : c.confidence >= 0.5 ? 'bg-yellow-500/20 text-yellow-400'
                          : 'bg-red-500/20 text-red-400'
                      }`}>{(c.confidence * 100).toFixed(0)}%</span>
                    </div>

                    {/* Challenge reason */}
                    <div className="text-muted-foreground leading-relaxed">{c.challengeReason}</div>

                    {/* Original vs Suggested — 對比顯示 */}
                    {c.suggestedValue && (
                      <div className="mt-2 grid grid-cols-2 gap-2 text-[10px]">
                        <div className="rounded bg-red-500/10 px-2 py-1">
                          <div className="mb-0.5 font-medium text-red-400">{t('docRefinery.output.original')}</div>
                          <div className="text-red-300/80">{c.originalValue || '—'}</div>
                        </div>
                        <div className="rounded bg-green-500/10 px-2 py-1">
                          <div className="mb-0.5 font-medium text-green-400">{t('docRefinery.output.suggested')}</div>
                          <div className="text-green-300/80">{c.suggestedValue}</div>
                        </div>
                      </div>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  )
}

// Element type → badge color mapping
const ELEMENT_COLORS: Record<string, string> = {
  Title: 'bg-blue-500/20 text-blue-400',
  NarrativeText: 'bg-gray-500/20 text-gray-400',
  Table: 'bg-green-500/20 text-green-400',
  ListItem: 'bg-yellow-500/20 text-yellow-400',
  CodeSnippet: 'bg-purple-500/20 text-purple-400',
  Image: 'bg-pink-500/20 text-pink-400',
  Header: 'bg-red-500/20 text-red-300',
  Footer: 'bg-red-500/20 text-red-300',
  PageNumber: 'bg-gray-500/20 text-gray-500',
  UncategorizedText: 'bg-gray-500/20 text-gray-500',
  FigureCaption: 'bg-pink-500/20 text-pink-300',
  Address: 'bg-teal-500/20 text-teal-400',
  EmailAddress: 'bg-teal-500/20 text-teal-400',
  Formula: 'bg-indigo-500/20 text-indigo-400',
  FormKeyValue: 'bg-orange-500/20 text-orange-400',
  PageBreak: 'bg-gray-500/20 text-gray-600',
}

export function DocRefineryPage() {
  const { t } = useTranslation(['studio', 'common'])
  const [projects, setProjects] = useState<RefineryProject[]>([])
  const [loading, setLoading] = useState(true)
  const [selected, setSelected] = useState<RefineryProject | null>(null)
  const [showCreate, setShowCreate] = useState(false)
  const [newName, setNewName] = useState('')
  const [newDesc, setNewDesc] = useState('')

  const fetchProjects = useCallback(async () => {
    try {
      const data = await api.refinery.list()
      setProjects(data)
    } catch { /* ignore */ } finally { setLoading(false) }
  }, [])

  useEffect(() => { fetchProjects() }, [fetchProjects])

  const handleCreate = async () => {
    if (!newName.trim()) return
    await api.refinery.create({ name: newName, description: newDesc })
    setShowCreate(false)
    setNewName('')
    setNewDesc('')
    fetchProjects()
  }

  const handleDelete = async (id: string) => {
    await api.refinery.delete(id)
    if (selected?.id === id) setSelected(null)
    fetchProjects()
  }

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      {/* Top bar — 跟知識庫一致 */}
      <div className="flex items-center justify-between border-b border-border bg-card px-5 shrink-0 h-[41px]">
        <div className="flex items-center gap-2">
          <Factory className="h-5 w-5 text-orange-400" />
          <span className="text-sm font-semibold">{t('docRefinery.title')}</span>
        </div>
        <button className="flex items-center gap-1 rounded-md bg-blue-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-blue-500 transition-colors cursor-pointer"
          onClick={() => setShowCreate(true)}>
          <Plus className="h-3.5 w-3.5" />{t('common:create')}
        </button>
      </div>

      <div className="flex flex-1 overflow-hidden">
        {/* Left: Project list */}
        <div className={`${selected ? 'w-[300px]' : 'flex-1'} shrink-0 overflow-y-auto p-4 transition-all`}>
          {loading ? <p className="text-xs text-muted-foreground">{t('common:loading')}</p>
            : projects.length === 0 ? (
              <div className="flex flex-col items-center justify-center gap-2 py-20 text-muted-foreground">
                <Factory className="h-10 w-10 opacity-30" />
                <p className="text-sm">{t('docRefinery.empty')}</p>
              </div>
            ) : (
              <div className={`grid gap-3 ${selected ? 'grid-cols-1' : 'grid-cols-1 md:grid-cols-2 xl:grid-cols-3'}`}>
                {projects.map(p => (
                  <div key={p.id}
                    className={`group cursor-pointer rounded-lg border border-border px-4 py-3 transition-colors hover:bg-secondary/30 ${selected?.id === p.id ? 'border-blue-500/60 bg-blue-500/5' : ''}`}
                    onClick={() => setSelected(p)}>
                    <div className="flex items-start justify-between mb-2">
                      <h3 className="text-sm font-semibold text-foreground flex-1 min-w-0 truncate">{p.name}</h3>
                      <div className="flex gap-1 shrink-0 ml-2">
                        <button className="hidden text-red-400 hover:text-red-300 group-hover:block"
                          onClick={(e) => { e.stopPropagation(); if (confirm(t('docRefinery.confirmDelete', { name: p.name }))) handleDelete(p.id) }}>
                          <Trash2 className="h-3.5 w-3.5" />
                        </button>
                      </div>
                    </div>
                    {p.description && <p className="text-[11px] text-muted-foreground truncate mb-2">{p.description}</p>}
                    <div className="flex items-center gap-4 text-[10px] text-muted-foreground mb-2">
                      <span className="flex items-center gap-1"><FileText size={11} /> {p.fileCount} {t('docRefinery.files')}</span>
                    </div>
                    <div className="flex items-center gap-1 text-[9px] text-muted-foreground">
                      <Clock size={10} /> {new Date(p.updatedAt).toLocaleDateString()}
                    </div>
                  </div>
                ))}
              </div>
            )}
        </div>

        {/* Right: Detail panel */}
        {selected && (
          <DetailPanel project={selected} onClose={() => setSelected(null)} onRefresh={fetchProjects}
            onProjectUpdate={setSelected} />
        )}
      </div>

      {/* Create dialog */}
      {showCreate && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={() => setShowCreate(false)}>
          <div className="w-[400px] rounded-lg border bg-card p-4 shadow-xl" onClick={e => e.stopPropagation()}>
            <h3 className="mb-3 text-sm font-semibold">{t('docRefinery.newProject')}</h3>
            <input className="field-input mb-2 w-full" placeholder={t('common:name')} value={newName}
              onChange={e => setNewName(e.target.value)} autoFocus />
            <textarea className="field-textarea mb-3 w-full" rows={2} placeholder={t('common:description')}
              value={newDesc} onChange={e => setNewDesc(e.target.value)} />
            <div className="flex justify-end gap-2">
              <button className="rounded-md px-3 py-1.5 text-xs hover:bg-secondary" onClick={() => setShowCreate(false)}>{t('common:cancel')}</button>
              <button className="rounded-md bg-blue-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-blue-500"
                onClick={handleCreate}>{t('common:create')}</button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

// ═══════════════════════════════════════
// Detail Panel (4 Tabs)
// ═══════════════════════════════════════

function DetailPanel({ project, onClose, onRefresh, onProjectUpdate }: {
  project: RefineryProject; onClose: () => void; onRefresh: () => void;
  onProjectUpdate: (p: RefineryProject) => void;
}) {
  const { t } = useTranslation(['studio', 'common'])
  const [tab, setTab] = useState<'files' | 'preview' | 'output' | 'settings'>('files')
  // 提升到 DetailPanel 層級，不隨 tab 切換銷毀
  const [fileLogs, setFileLogs] = useState<Record<string, string[]>>({})
  const [expandedFiles, setExpandedFiles] = useState<Set<string>>(new Set())

  const tabs = [
    { key: 'files' as const, icon: FileText, label: t('docRefinery.tabs.files') },
    { key: 'preview' as const, icon: Eye, label: t('docRefinery.tabs.preview') },
    { key: 'output' as const, icon: FileOutput, label: t('docRefinery.tabs.output') },
    { key: 'settings' as const, icon: Settings, label: t('docRefinery.tabs.settings') },
  ]

  return (
    <div className="flex-1 border-l border-border flex flex-col overflow-hidden">
      {/* Header — 跟知識庫一致 */}
      <div className="flex items-center justify-between border-b border-border bg-card px-4 py-2.5 shrink-0">
        <div className="min-w-0 flex-1">
          <div className="text-xs font-semibold text-foreground truncate">{project.name}</div>
          {project.description && <div className="text-[9px] text-muted-foreground truncate">{project.description}</div>}
        </div>
        <button className="ml-2 text-muted-foreground hover:text-foreground" onClick={onClose}><X className="h-4 w-4" /></button>
      </div>

      {/* Tab bar */}
      <div className="flex border-b border-border px-2 shrink-0">
        {tabs.map(({ key, icon: Icon, label }) => (
          <button key={key}
            className={`flex items-center gap-1.5 border-b-2 px-3 py-2 text-xs font-medium transition-colors ${tab === key ? 'border-blue-500 text-blue-400' : 'border-transparent text-muted-foreground hover:text-foreground'}`}
            onClick={() => setTab(key)}>
            <Icon className="h-3.5 w-3.5" />{label}
          </button>
        ))}
      </div>

      {/* Tab content */}
      <div className="flex-1 overflow-y-auto p-4">
        {tab === 'files' && <FilesTab project={project} onRefresh={onRefresh}
          fileLogs={fileLogs} setFileLogs={setFileLogs}
          expandedFiles={expandedFiles} setExpandedFiles={setExpandedFiles} />}
        {tab === 'preview' && <PreviewTab project={project} />}
        {tab === 'output' && <OutputTab project={project} />}
        {tab === 'settings' && <SettingsTab project={project} onRefresh={onRefresh} onProjectUpdate={onProjectUpdate} />}
      </div>
    </div>
  )
}

// ═══════════════════════════════════════
// Files Tab
// ═══════════════════════════════════════

function FilesTab({ project, onRefresh, fileLogs, setFileLogs, expandedFiles, setExpandedFiles }: {
  project: RefineryProject; onRefresh: () => void;
  fileLogs: Record<string, string[]>; setFileLogs: React.Dispatch<React.SetStateAction<Record<string, string[]>>>;
  expandedFiles: Set<string>; setExpandedFiles: React.Dispatch<React.SetStateAction<Set<string>>>;
}) {
  const { t } = useTranslation(['studio'])
  const [files, setFiles] = useState<RefineryFileDoc[]>([])
  const fileInputRef = useRef<HTMLInputElement>(null)
  const expanded = expandedFiles
  const setExpanded = setExpandedFiles

  const fetchFiles = useCallback(async () => {
    try { setFiles(await api.refinery.listFiles(project.id)) } catch { /* ignore */ }
  }, [project.id])

  useEffect(() => { fetchFiles() }, [fetchFiles])

  const toggleExpand = (key: string) => {
    setExpanded(prev => {
      const next = new Set(prev)
      next.has(key) ? next.delete(key) : next.add(key)
      return next
    })
  }

  const handleUpload = async (fileList: FileList) => {
    // 不鎖按鈕 — 可以邊上傳邊加檔案
    const fileArray = Array.from(fileList)
    // 為每個檔案建立 log 項 + 自動展開
    for (const f of fileArray) {
      setFileLogs(prev => ({ ...prev, [f.name]: [] }))
      setExpanded(prev => new Set(prev).add(f.name))
    }

    try {
      await api.refinery.uploadFiles(project.id, fileArray, evt => {
        const fn = evt.fileName ?? fileArray[0]?.name ?? 'unknown'
        setFileLogs(prev => ({
          ...prev,
          [fn]: [...(prev[fn] ?? []), evt.text ?? ''],
        }))
      })
      fetchFiles()
      onRefresh()
      // 完成後收折已完成的檔案
      for (const f of fileArray) {
        setExpanded(prev => { const next = new Set(prev); next.delete(f.name); return next })
      }
    } catch (err: any) {
      const fn = fileArray[0]?.name ?? 'unknown'
      setFileLogs(prev => ({
        ...prev,
        [fn]: [...(prev[fn] ?? []), `Error: ${err.message}`],
      }))
    }
  }

  const handleDelete = async (fileId: string) => {
    await api.refinery.deleteFile(project.id, fileId)
    fetchFiles()
    onRefresh()
  }

  // 合併：已上傳的檔案 + 正在上傳中的檔案（還沒出現在 files 裡）
  const uploadingNames = Object.keys(fileLogs).filter(
    name => !files.some(f => f.fileName === name) && fileLogs[name].length > 0
  )

  return (
    <div>
      {/* Upload area — 不鎖定，隨時可上傳 */}
      <div className="mb-4 flex cursor-pointer items-center justify-center rounded-lg border-2 border-dashed border-border py-8 text-muted-foreground transition-colors hover:border-blue-500 hover:text-blue-400"
        onClick={() => fileInputRef.current?.click()}
        onDragOver={e => { e.preventDefault(); e.currentTarget.classList.add('border-blue-500') }}
        onDragLeave={e => e.currentTarget.classList.remove('border-blue-500')}
        onDrop={e => { e.preventDefault(); e.currentTarget.classList.remove('border-blue-500'); if (e.dataTransfer.files.length) handleUpload(e.dataTransfer.files) }}>
        <Upload className="mr-2 h-5 w-5" />
        <span className="text-sm">{t('docRefinery.uploadArea')}</span>
        <input ref={fileInputRef} type="file" multiple hidden
          onChange={e => { if (e.target.files?.length) handleUpload(e.target.files); e.target.value = '' }} />
      </div>

      {/* 正在上傳中的檔案（還沒出現在 files 列表裡） */}
      {uploadingNames.map(name => (
        <FileLogCard key={`uploading-${name}`} fileName={name} status="Indexing"
          logs={fileLogs[name]} isExpanded={expanded.has(name)}
          onToggle={() => toggleExpand(name)} />
      ))}

      {/* File list */}
      {files.length === 0 && uploadingNames.length === 0 ? (
        <p className="text-center text-xs text-muted-foreground">{t('docRefinery.noFiles')}</p>
      ) : (
        <div className="space-y-1">
          {files.map(f => (
            <div key={f.id}>
              <div className={`group flex items-center justify-between rounded-md px-3 py-2 hover:bg-secondary/50 ${!f.isIncluded ? 'opacity-50' : ''}`}>
                {/* Checkbox */}
                <input type="checkbox" checked={f.isIncluded}
                  className="mr-2 h-3.5 w-3.5 shrink-0 cursor-pointer accent-blue-500"
                  onChange={async () => {
                    await api.refinery.toggleFileIncluded(project.id, f.id)
                    fetchFiles()
                  }} />
                <div className="min-w-0 flex-1 cursor-pointer" onClick={() => fileLogs[f.fileName]?.length ? toggleExpand(f.fileName) : null}>
                  <p className={`truncate text-sm ${!f.isIncluded ? 'line-through' : ''}`}>{f.fileName}</p>
                  <p className="text-xs text-muted-foreground">
                    {formatFileSize(f.fileSize)} &middot; {f.elementCount} {t('docRefinery.elements')}
                    {f.indexStatus === 'Indexed' && ` · ${f.chunkCount} chunks`}
                  </p>
                </div>
                <div className="flex items-center gap-1.5">
                  <IndexStatusBadge status={f.indexStatus} />
                  {fileLogs[f.fileName]?.length > 0 && (
                    <button className="text-muted-foreground hover:text-foreground"
                      onClick={() => toggleExpand(f.fileName)}>
                      <ChevronDown className={`h-3.5 w-3.5 transition-transform ${expanded.has(f.fileName) ? 'rotate-180' : ''}`} />
                    </button>
                  )}
                  {f.indexStatus === 'Failed' && (
                    <button className="text-yellow-400 hover:text-yellow-300"
                      title={t('docRefinery.retry')}
                      onClick={async () => {
                        await api.refinery.reindexFile(project.id, f.id, () => {})
                        fetchFiles()
                      }}>
                      <RefreshCw className="h-3.5 w-3.5" />
                    </button>
                  )}
                  <button className="hidden text-red-400 hover:text-red-300 group-hover:block"
                    onClick={() => handleDelete(f.id)}>
                    <Trash2 className="h-3.5 w-3.5" />
                  </button>
                </div>
              </div>
              {/* 收折式 log */}
              {expanded.has(f.fileName) && fileLogs[f.fileName]?.length > 0 && (
                <LogPanel logs={fileLogs[f.fileName]} />
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

// ═══════════════════════════════════════
// Preview Tab
// ═══════════════════════════════════════

function PreviewTab({ project }: { project: RefineryProject }) {
  const { t } = useTranslation(['studio'])
  const [files, setFiles] = useState<RefineryFileDoc[]>([])
  const [selectedFileId, setSelectedFileId] = useState<string | null>(null)
  const [elements, setElements] = useState<any[]>([])
  const [loadingPreview, setLoadingPreview] = useState(false)

  useEffect(() => {
    api.refinery.listFiles(project.id).then(setFiles).catch(() => {})
  }, [project.id])

  useEffect(() => {
    if (!selectedFileId) { setElements([]); return }
    setLoadingPreview(true)
    api.refinery.previewFile(project.id, selectedFileId)
      .then(data => setElements(Array.isArray(data) ? data : []))
      .catch(() => setElements([]))
      .finally(() => setLoadingPreview(false))
  }, [project.id, selectedFileId])

  return (
    <div>
      <select className="field-input mb-4 w-full" value={selectedFileId ?? ''}
        onChange={e => setSelectedFileId(e.target.value || null)}>
        <option value="">{t('docRefinery.preview.selectFile')}</option>
        {files.map(f => <option key={f.id} value={f.id}>{f.fileName}</option>)}
      </select>

      {loadingPreview ? <p className="text-xs text-muted-foreground">{t('common:loading')}</p>
        : elements.length === 0 ? (
          selectedFileId && <p className="text-xs text-muted-foreground">{t('docRefinery.preview.noElements')}</p>
        ) : (
          <div className="space-y-1">
            {elements.map((el, i) => (
              <div key={i} className="flex gap-2 rounded-md border px-3 py-2">
                <span className={`shrink-0 rounded px-1.5 py-0.5 text-[10px] font-medium ${ELEMENT_COLORS[el.type] ?? 'bg-gray-500/20 text-gray-400'}`}>
                  {el.type}
                </span>
                <p className="min-w-0 flex-1 text-xs leading-relaxed whitespace-pre-wrap">{el.text}</p>
              </div>
            ))}
          </div>
        )}
    </div>
  )
}

// ═══════════════════════════════════════
// Output Tab
// ═══════════════════════════════════════

function OutputTab({ project }: { project: RefineryProject }) {
  const { t } = useTranslation(['studio'])
  const [outputs, setOutputs] = useState<RefineryOutputDoc[]>([])
  const [selectedVersion, setSelectedVersion] = useState<number | null>(null)
  const [currentOutput, setCurrentOutput] = useState<RefineryOutputDoc | null>(null)
  const [viewMode, setViewMode] = useState<'markdown' | 'json'>('markdown')

  useEffect(() => {
    api.refinery.listOutputs(project.id).then(data => {
      setOutputs(data)
      if (data.length > 0) setSelectedVersion(data[0].version)
    }).catch(() => {})
  }, [project.id])

  useEffect(() => {
    if (selectedVersion === null) { setCurrentOutput(null); return }
    api.refinery.getOutput(project.id, selectedVersion)
      .then(setCurrentOutput)
      .catch(() => setCurrentOutput(null))
  }, [project.id, selectedVersion])

  const parseMissing = (s: string) => safeJsonParse<string[]>(s, [])
  const parseQuestions = (s: string) => safeJsonParse<string[]>(s, [])

  if (outputs.length === 0) {
    return <p className="text-sm text-muted-foreground">{t('docRefinery.output.noOutput')}</p>
  }

  const missing = currentOutput ? parseMissing(currentOutput.missingFields) : []
  const questions = currentOutput ? parseQuestions(currentOutput.openQuestions) : []
  const challenges = currentOutput ? safeJsonParse<FieldChallengeDoc[]>(currentOutput.challenges, []) : []
  const flaggedChallenges = challenges.filter(c => c.action === 'Flag' || c.action === 'Reject')

  return (
    <div>
      {/* Version selector + view toggle + confidence */}
      <div className="mb-4 flex items-center gap-3">
        <select className="field-input w-40" value={selectedVersion ?? ''}
          onChange={e => setSelectedVersion(Number(e.target.value))}>
          {outputs.map(o => <option key={o.version} value={o.version}>v{o.version} — {new Date(o.createdAt).toLocaleDateString()}</option>)}
        </select>
        {currentOutput && currentOutput.overallConfidence < 1.0 && (
          <span className={`rounded-full px-2 py-0.5 text-[10px] font-medium ${
            currentOutput.overallConfidence >= 0.8 ? 'bg-green-500/20 text-green-400'
              : currentOutput.overallConfidence >= 0.5 ? 'bg-yellow-500/20 text-yellow-400'
              : 'bg-red-500/20 text-red-400'
          }`}>
            {t('docRefinery.output.confidence')}: {(currentOutput.overallConfidence * 100).toFixed(0)}%
          </span>
        )}
        <div className="flex rounded-md border">
          <button className={`px-2 py-1 text-xs ${viewMode === 'markdown' ? 'bg-blue-500/20 text-blue-400' : 'text-muted-foreground'}`}
            onClick={() => setViewMode('markdown')}>Markdown</button>
          <button className={`px-2 py-1 text-xs ${viewMode === 'json' ? 'bg-blue-500/20 text-blue-400' : 'text-muted-foreground'}`}
            onClick={() => setViewMode('json')}>JSON</button>
        </div>
        <button className="ml-auto text-xs text-muted-foreground hover:text-foreground"
          onClick={() => { if (currentOutput) navigator.clipboard.writeText(viewMode === 'json' ? currentOutput.outputJson : currentOutput.outputMarkdown) }}>
          <Copy className="mr-1 inline h-3.5 w-3.5" />{t('common:copy')}
        </button>
        <button className="text-xs text-muted-foreground hover:text-foreground"
          onClick={() => {
            if (!currentOutput) return
            const content = viewMode === 'json' ? currentOutput.outputJson : currentOutput.outputMarkdown
            const ext = viewMode === 'json' ? 'json' : 'md'
            downloadAsFile(content, `${project.name}_v${selectedVersion}.${ext}`)
          }}>
          <Download className="mr-1 inline h-3.5 w-3.5" />{t('common:download')}
        </button>
      </div>

      {/* Source files */}
      {currentOutput && (() => {
        const srcFiles = safeJsonParse<string[]>(currentOutput.sourceFiles, [])
        return srcFiles.length > 0 ? (
          <div className="mb-3 flex items-center gap-2 rounded-md bg-secondary/50 px-3 py-2 text-xs text-muted-foreground">
            <FileText className="h-3.5 w-3.5 shrink-0" />
            <span className="font-medium">{t('docRefinery.output.sourceFiles')}:</span>
            <span>{srcFiles.join(', ')}</span>
          </div>
        ) : null
      })()}

      {/* Warnings */}
      {missing.length > 0 && (
        <div className="mb-3 rounded-md bg-yellow-500/10 px-3 py-2 text-xs text-yellow-400">
          <span className="font-medium">{t('docRefinery.output.missingFields')}:</span> {missing.join(', ')}
        </div>
      )}
      {questions.length > 0 && (
        <div className="mb-3 rounded-md bg-orange-500/10 px-3 py-2 text-xs text-orange-400">
          <span className="font-medium">{t('docRefinery.output.openQuestions')}:</span>
          <ul className="mt-1 list-disc pl-4">{questions.map((q, i) => <li key={i}>{q}</li>)}</ul>
        </div>
      )}

      {/* Challenges — 按區塊分組 */}
      {flaggedChallenges.length > 0 && (
        <ChallengePanel challenges={flaggedChallenges} />
      )}

      {/* Content */}
      {currentOutput && (
        <div className="rounded-lg border bg-card p-4 overflow-auto">
          {viewMode === 'markdown' ? (
            <div className="refinery-markdown text-sm leading-relaxed text-muted-foreground">
              <Markdown remarkPlugins={[remarkGfm]} components={markdownComponents}>
                {currentOutput.outputMarkdown}
              </Markdown>
            </div>
          ) : (
            <JsonView data={safeJsonParse(currentOutput.outputJson, {})} style={darkStyles} />
          )}
        </div>
      )}
    </div>
  )
}

// ═══════════════════════════════════════
// Settings Tab
// ═══════════════════════════════════════

function SettingsTab({ project, onRefresh, onProjectUpdate }: {
  project: RefineryProject; onRefresh: () => void;
  onProjectUpdate: (p: RefineryProject) => void;
}) {
  const { t } = useTranslation(['studio', 'common'])
  const [templates, setTemplates] = useState<SchemaTemplateSummary[]>([])
  const [schemaTemplateId, setSchemaTemplateId] = useState(project.schemaTemplateId ?? '')
  const [provider, setProvider] = useState(project.provider)
  const [model, setModel] = useState(project.model)
  const [outputLanguage, setOutputLanguage] = useState(project.outputLanguage ?? '')
  const [extractionMode, setExtractionMode] = useState(project.extractionMode ?? 'fast')
  const [enableChallenge, setEnableChallenge] = useState(project.enableChallenge ?? false)
  const [imageProcessingMode, setImageProcessingMode] = useState(project.imageProcessingMode ?? 'skip')
  const [generating, setGenerating] = useState(false)
  const [genLogs, setGenLogs] = useState<string[]>([])
  const logEndRef = useRef<HTMLDivElement>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    api.refinery.listSchemaTemplates().then(setTemplates).catch(() => {})
  }, [])

  const [saveError, setSaveError] = useState('')

  const handleSave = async () => {
    setSaving(true)
    setSaveError('')
    try {
      const updated = await api.refinery.update(project.id, {
        name: project.name,
        description: project.description,
        schemaTemplateId: schemaTemplateId || undefined,
        provider, model,
        outputLanguage: outputLanguage || undefined,
        extractionMode,
        enableChallenge,
        imageProcessingMode,
      })
      onProjectUpdate(updated)
      onRefresh()
    } catch (err: any) {
      setSaveError(err?.message ?? JSON.stringify(err))
    } finally { setSaving(false) }
  }

  const handleGenerate = async () => {
    setGenerating(true)
    setGenLogs([])
    try {
      await api.refinery.generate(project.id, evt => {
        setGenLogs(prev => [...prev, evt.text ?? ''])
        setTimeout(() => logEndRef.current?.scrollIntoView({ behavior: 'smooth' }), 50)
      })
      onRefresh()
    } catch (err: any) {
      setGenLogs(prev => [...prev, `Error: ${err.message}`])
    } finally {
      setGenerating(false)
    }
  }

  return (
    <div className="space-y-4">
      {/* Schema template */}
      <div>
        <label className="mb-1 block text-xs font-medium">{t('docRefinery.settings.schemaTemplate')}</label>
        <select className="field-input w-full" value={schemaTemplateId}
          onChange={e => setSchemaTemplateId(e.target.value)}>
          <option value="">— {t('common:select')} —</option>
          {templates.map(tmpl => (
            <option key={tmpl.id} value={tmpl.id}>{tmpl.name}</option>
          ))}
        </select>
      </div>

      {/* Provider / Model */}
      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className="mb-1 block text-xs font-medium">{t('docRefinery.settings.provider')}</label>
          <select className="field-input w-full" value={provider} onChange={e => setProvider(e.target.value)}>
            <option value="openai">OpenAI</option>
            <option value="azure-openai">Azure OpenAI</option>
            <option value="anthropic">Anthropic</option>
            <option value="google">Google</option>
          </select>
        </div>
        <div>
          <label className="mb-1 block text-xs font-medium">{t('docRefinery.settings.model')}</label>
          <input className="field-input w-full" value={model} onChange={e => setModel(e.target.value)} />
        </div>
      </div>

      {/* Output language */}
      <div>
        <label className="mb-1 block text-xs font-medium">{t('docRefinery.settings.outputLanguage')}</label>
        <input className="field-input w-full" placeholder="auto" value={outputLanguage}
          onChange={e => setOutputLanguage(e.target.value)} />
      </div>

      {/* Extraction mode */}
      <div>
        <label className="mb-1 block text-xs font-medium">{t('docRefinery.settings.extractionMode')}</label>
        <div className="flex gap-2">
          <button className={`flex-1 rounded-md border px-3 py-2 text-xs ${extractionMode === 'fast' ? 'border-blue-500 bg-blue-500/10 text-blue-400' : 'border-border text-muted-foreground'}`}
            onClick={() => setExtractionMode('fast')}>
            <div className="font-medium">{t('docRefinery.settings.fast')}</div>
            <div className="mt-0.5 text-[10px] opacity-70">{t('docRefinery.settings.fastDesc')}</div>
          </button>
          <button className={`flex-1 rounded-md border px-3 py-2 text-xs ${extractionMode === 'precise' ? 'border-orange-500 bg-orange-500/10 text-orange-400' : 'border-border text-muted-foreground'}`}
            onClick={() => setExtractionMode('precise')}>
            <div className="font-medium">{t('docRefinery.settings.precise')}</div>
            <div className="mt-0.5 text-[10px] opacity-70">{t('docRefinery.settings.preciseDesc')}</div>
          </button>
        </div>
      </div>

      {/* Image Processing Mode */}
      <div>
        <label className="mb-1 block text-xs font-medium">{t('docRefinery.settings.imageMode')}</label>
        <div className="grid grid-cols-2 gap-2">
          {([
            { key: 'skip', active: 'border-gray-500 bg-gray-500/10 text-gray-400', i18n: 'skip' },
            { key: 'ocr', active: 'border-cyan-500 bg-cyan-500/10 text-cyan-400', i18n: 'ocr' },
            { key: 'ai-describe', active: 'border-purple-500 bg-purple-500/10 text-purple-400', i18n: 'ai_describe' },
            { key: 'hybrid', active: 'border-emerald-500 bg-emerald-500/10 text-emerald-400', i18n: 'hybrid' },
          ] as const).map(({ key, active, i18n }) => (
            <button key={key}
              className={`rounded-md border px-3 py-2 text-xs ${imageProcessingMode === key ? active : 'border-border text-muted-foreground'}`}
              onClick={() => setImageProcessingMode(key)}>
              <div className="font-medium">{t(`docRefinery.settings.imageMode_${i18n}`)}</div>
              <div className="mt-0.5 text-[10px] opacity-70">{t(`docRefinery.settings.imageMode_${i18n}_desc`)}</div>
            </button>
          ))}
        </div>
      </div>

      {/* LLM Challenge */}
      {extractionMode === 'precise' && (
        <div className="flex items-center justify-between rounded-md border px-3 py-2">
          <div>
            <div className="text-xs font-medium">{t('docRefinery.settings.enableChallenge')}</div>
            <div className="text-[10px] text-muted-foreground">{t('docRefinery.settings.challengeDesc')}</div>
          </div>
          <button className={`relative h-5 w-9 rounded-full transition-colors ${enableChallenge ? 'bg-blue-500' : 'bg-gray-600'}`}
            onClick={() => setEnableChallenge(!enableChallenge)}>
            <span className={`absolute top-0.5 h-4 w-4 rounded-full bg-white transition-transform ${enableChallenge ? 'left-[18px]' : 'left-0.5'}`} />
          </button>
        </div>
      )}

      {/* Save */}
      <button className="rounded-md bg-secondary px-3 py-1.5 text-xs hover:bg-secondary/80"
        onClick={handleSave} disabled={saving}>
        {saving ? '...' : t('common:save')}
      </button>
      {saveError && (
        <div className="mt-1 rounded-md bg-red-500/10 px-3 py-1.5 text-xs text-red-400">{saveError}</div>
      )}

      {/* Generate */}
      <div className="border-t pt-4">
        <button className="relative w-full overflow-hidden rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-500 disabled:opacity-60"
          onClick={handleGenerate} disabled={generating || !schemaTemplateId}>
          {generating && (
            <span className="absolute inset-0 bg-gradient-to-r from-blue-600 via-blue-400 to-blue-600 animate-[shimmer_2s_infinite] bg-[length:200%_100%]" />
          )}
          <span className="relative flex items-center justify-center gap-2">
            {generating && <span className="h-4 w-4 animate-spin rounded-full border-2 border-white/30 border-t-white" />}
            {generating ? t('docRefinery.output.generating') : t('docRefinery.settings.generate')}
          </span>
        </button>
        {genLogs.length > 0 && (
          <div className="mt-2 max-h-48 space-y-1 overflow-y-auto rounded-md border p-2">
            {/* 進度指示 */}
            {generating && (
              <div className="mb-1 h-1 w-full overflow-hidden rounded-full bg-blue-500/20">
                <div className="h-full w-1/3 animate-[indeterminate_1.5s_infinite] rounded-full bg-blue-500" />
              </div>
            )}
            {genLogs.map((msg, i) => (
              <div key={i} className={`rounded px-2 py-1 text-xs ${
                msg.startsWith('Error') ? 'bg-red-500/10 text-red-400'
                  : msg.startsWith('✅') ? 'bg-green-500/10 text-green-400'
                  : 'bg-blue-500/10 text-blue-400'
              }`}>
                {msg}
              </div>
            ))}
            <div ref={logEndRef} />
          </div>
        )}
      </div>
    </div>
  )
}
