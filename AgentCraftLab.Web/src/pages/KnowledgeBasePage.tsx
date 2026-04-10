/**
 * KnowledgeBasePage — 知識庫管理：CRUD + 檔案列表 + 單檔刪除 + KB 編輯。
 */
import { useState, useEffect, useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { useConfirmDialog } from '@/components/shared/ConfirmDialog'
import { BookOpen, Plus, Trash2, FileText, Database, Clock, Upload, Loader2, Edit3, X, File, Settings, Search, ChevronDown, ChevronUp } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { api, type KbFileDocument, type DataSourceDocument } from '@/lib/api'
import { EMBEDDING_MODELS } from '@/types/workflow'
import { notify } from '@/lib/notify'

interface KbDocument {
  id: string;
  name: string;
  description: string;
  embeddingModel: string;
  chunkSize: number;
  chunkStrategy: string;
  fileCount: number;
  totalChunks: number;
  dataSourceId?: string;
  createdAt: string;
  updatedAt: string;
}

function fmtSize(bytes: number): string {
  if (bytes < 1024) return `${bytes}B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)}KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)}MB`
}

function fmtDate(s: string): string {
  const d = new Date(s)
  return d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

export function KnowledgeBasePage() {
  const { t } = useTranslation(['studio', 'common'])
  const { t: tn } = useTranslation('notifications')
  const { confirm, confirmDialog } = useConfirmDialog()
  const navigate = useNavigate()
  const [kbs, setKbs] = useState<KbDocument[]>([])
  const [loading, setLoading] = useState(true)

  // data sources for dropdown
  const [dataSources, setDataSources] = useState<DataSourceDocument[]>([])

  // create dialog
  const [showCreate, setShowCreate] = useState(false)
  const [newName, setNewName] = useState('')
  const [newDesc, setNewDesc] = useState('')
  const [newDataSourceId, setNewDataSourceId] = useState<string>('')
  const [newChunkSize, setNewChunkSize] = useState(512)
  const [newChunkOverlap, setNewChunkOverlap] = useState(50)
  const [newEmbeddingModel, setNewEmbeddingModel] = useState('text-embedding-3-small')
  const [newChunkStrategy, setNewChunkStrategy] = useState('fixed')

  // edit dialog
  const [editing, setEditing] = useState<KbDocument | null>(null)
  const [editName, setEditName] = useState('')
  const [editDesc, setEditDesc] = useState('')

  // upload
  const [uploading, setUploading] = useState<string | null>(null)
  const [uploadMsg, setUploadMsg] = useState('')
  const [chunkPreviews, setChunkPreviews] = useState<string[]>([])

  // detail panel (files)
  const [selectedKb, setSelectedKb] = useState<KbDocument | null>(null)
  const [files, setFiles] = useState<KbFileDocument[]>([])
  const [filesLoading, setFilesLoading] = useState(false)

  const fetchKbs = useCallback(async () => {
    try {
      const data = await api.knowledgeBases.list()
      setKbs(data)
    } catch (err) {
      console.error('Failed to load knowledge bases:', err)
      notify.error(tn('loadFailed.knowledgeBases'))
    } finally {
      setLoading(false)
    }
  }, [])

  const fetchDataSources = useCallback(async () => {
    try {
      const data = await api.dataSources.list()
      setDataSources(data)
    } catch { /* silent */ }
  }, [])

  useEffect(() => { fetchKbs(); fetchDataSources() }, [fetchKbs, fetchDataSources])

  const dsName = (dsId?: string) => {
    if (!dsId) return 'SQLite (Legacy)'
    const ds = dataSources.find(d => d.id === dsId)
    return ds ? `${ds.name} (${ds.provider})` : dsId
  }

  const fetchFiles = useCallback(async (kbId: string) => {
    setFilesLoading(true)
    try {
      const data = await api.knowledgeBases.listFiles(kbId)
      setFiles(data)
    } catch {
      setFiles([])
    } finally {
      setFilesLoading(false)
    }
  }, [])

  const handleSelectKb = (kb: KbDocument) => {
    setSelectedKb(kb)
    fetchFiles(kb.id)
  }

  const handleCreate = async () => {
    if (!newName.trim()) return
    if (!newDataSourceId) return
    try {
      await api.knowledgeBases.create({
        name: newName, description: newDesc, dataSourceId: newDataSourceId,
        embeddingModel: newEmbeddingModel, chunkSize: newChunkSize, chunkOverlap: newChunkOverlap,
        chunkStrategy: newChunkStrategy,
      })
      setShowCreate(false)
      setNewName('')
      setNewDesc('')
      setNewDataSourceId('')
      setNewChunkSize(512)
      setNewChunkOverlap(50)
      setNewEmbeddingModel('text-embedding-3-small')
      setNewChunkStrategy('fixed')
      await fetchKbs()
    } catch (err) {
      console.error('Failed to create knowledge base:', err)
      notify.error(tn('createFailed.knowledgeBase'), { description: (err as any)?.message })
    }
  }

  const handleEdit = async () => {
    if (!editing || !editName.trim()) return
    try {
      await api.knowledgeBases.update(editing.id, { name: editName, description: editDesc })
      setEditing(null)
      await fetchKbs()
      if (selectedKb?.id === editing.id) {
        setSelectedKb((prev) => prev ? { ...prev, name: editName, description: editDesc } : prev)
      }
    } catch (err) {
      console.error('Failed to update knowledge base:', err)
      notify.error(tn('updateFailed.knowledgeBase'), { description: (err as any)?.message })
    }
  }

  const handleDelete = async (kb: KbDocument) => {
    if (!await confirm(t('kb.confirmDelete', { name: kb.name }))) return
    try {
      await api.knowledgeBases.delete(kb.id)
      if (selectedKb?.id === kb.id) {
        setSelectedKb(null)
        setFiles([])
      }
      await fetchKbs()
    } catch (err) {
      console.error('Failed to delete knowledge base:', err)
      notify.error(tn('deleteFailed.knowledgeBase'), { description: (err as any)?.message })
    }
  }

  const handleDeleteFile = async (fileId: string, fileName: string) => {
    if (!selectedKb) return
    if (!await confirm(t('kb.confirmDeleteFile', { name: fileName }))) return
    try {
      await api.knowledgeBases.deleteFile(selectedKb.id, fileId)
      setFiles((prev) => prev.filter((f) => f.id !== fileId))
      await fetchKbs()
    } catch (err) {
      console.error('Failed to delete file:', err)
      notify.error(tn('deleteFailed.file'), { description: (err as any)?.message })
    }
  }

  const handleFileUpload = async (kbId: string, fileList: globalThis.File[]) => {
    if (fileList.length === 0) return
    setUploading(kbId)
    setUploadMsg('')
    setChunkPreviews([])
    try {
      const finalMsg = await api.knowledgeBases.uploadFiles(kbId, fileList, (evt: any) => {
        setUploadMsg(evt.text)
        // 擷取 RagReady 事件中的 chunk 預覽
        if (evt.metadata?.chunkPreviews) {
          try { setChunkPreviews(JSON.parse(evt.metadata.chunkPreviews)) } catch { /* ignore */ }
        }
      })
      setUploadMsg(finalMsg || t('kb.uploaded'))
      await fetchKbs()
      if (selectedKb?.id === kbId) fetchFiles(kbId)
    } catch {
      setUploadMsg(t('kb.uploadFailed'))
    } finally {
      setUploading(null)
      setTimeout(() => { setUploadMsg(''); setChunkPreviews([]) }, 10000)
    }
  }

  return (
    <div className="flex flex-1 flex-col overflow-hidden">
      {/* Top Bar */}
      <div className="flex items-center justify-between border-b border-border bg-card px-5 shrink-0 h-[41px]">
        <div className="flex items-center gap-2">
          <BookOpen size={16} className="text-green-500" />
          <h1 className="text-sm font-semibold text-foreground">{t('kb.title')}</h1>
        </div>
        <button
          onClick={() => setShowCreate(true)}
          className="flex items-center gap-1 rounded-md bg-blue-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-blue-500 transition-colors cursor-pointer"
        >
          <Plus size={13} /> {t('common:create')}
        </button>
      </div>

      <div className="flex flex-1 overflow-hidden">
        {/* ─── Left: KB List ─── */}
        <div className={`${selectedKb ? 'w-[300px]' : 'flex-1'} shrink-0 overflow-y-auto p-4 transition-all`}>
          {uploadMsg && (
            <div className="mb-3 rounded-md bg-blue-500/10 border border-blue-500/20 px-3 py-2">
              <div className="text-xs text-blue-400">{uploadMsg}</div>
              {chunkPreviews.length > 0 && (
                <div className="mt-2 space-y-1">
                  <div className="text-[9px] text-blue-400/70 font-medium">{t('kb.chunkPreview')}</div>
                  {chunkPreviews.map((chunk, i) => (
                    <div key={i} className="text-[9px] text-blue-300/80 bg-blue-500/5 rounded px-2 py-1 line-clamp-2">
                      <span className="text-blue-400/50 mr-1">#{i + 1}</span>{chunk}
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}

          {loading && <p className="text-xs text-muted-foreground text-center py-8">{t('loading')}</p>}

          {!loading && kbs.length === 0 && (
            <div className="flex flex-col items-center justify-center py-20 text-center">
              <Database size={40} className="text-muted-foreground mb-3" />
              <p className="text-xs text-muted-foreground mb-4">{t('kb.empty')}</p>
              <button
                onClick={() => setShowCreate(true)}
                className="rounded-md bg-blue-600 px-4 py-1.5 text-xs font-semibold text-white hover:bg-blue-500 transition-colors cursor-pointer"
              >
                {t('kb.createFirst')}
              </button>
            </div>
          )}

          <div className={`grid gap-3 ${selectedKb ? 'grid-cols-1' : 'grid-cols-1 md:grid-cols-2 xl:grid-cols-3'}`}>
            {kbs.map((kb) => {
              const active = selectedKb?.id === kb.id
              return (
                <div
                  key={kb.id}
                  onClick={() => handleSelectKb(kb)}
                  className={`rounded-lg border p-4 transition-colors cursor-pointer group ${active ? 'border-blue-500/50 bg-blue-500/5' : 'border-border bg-card hover:border-muted-foreground/30'}`}
                >
                  <div className="flex items-start justify-between mb-2">
                    <h3 className="text-sm font-semibold text-foreground flex-1 min-w-0 truncate">{kb.name}</h3>
                    <div className="flex gap-1 shrink-0 ml-2">
                      <button
                        onClick={(e) => { e.stopPropagation(); setEditing(kb); setEditName(kb.name); setEditDesc(kb.description) }}
                        className="opacity-0 group-hover:opacity-100 text-muted-foreground hover:text-foreground cursor-pointer p-1"
                      >
                        <Edit3 size={12} />
                      </button>
                      <button
                        onClick={(e) => { e.stopPropagation(); handleDelete(kb) }}
                        className="opacity-0 group-hover:opacity-100 text-muted-foreground hover:text-red-400 cursor-pointer p-1"
                      >
                        <Trash2 size={12} />
                      </button>
                    </div>
                  </div>
                  {kb.description && (
                    <p className="text-xs text-muted-foreground mb-3 line-clamp-2">{kb.description}</p>
                  )}
                  <div className="flex items-center gap-4 text-[10px] text-muted-foreground mb-1">
                    <span className="flex items-center gap-1"><FileText size={11} /> {kb.fileCount ?? 0} {t('kb.files')}</span>
                    <span className="flex items-center gap-1"><Database size={11} /> {kb.totalChunks ?? 0} {t('kb.chunks')}</span>
                  </div>
                  <div className="text-[9px] text-muted-foreground/70 mb-1 truncate">
                    {dsName(kb.dataSourceId)}
                  </div>
                  <div className="flex items-center gap-1 text-[9px] text-muted-foreground">
                    <Clock size={10} /> {fmtDate(kb.updatedAt)}
                  </div>
                  <label
                    onClick={(e) => e.stopPropagation()}
                    className="mt-2 flex items-center gap-1 rounded-md border border-dashed border-border px-2.5 py-1.5 text-[10px] text-muted-foreground hover:border-blue-500/40 hover:text-blue-400 transition-colors cursor-pointer w-fit"
                  >
                    {uploading === kb.id ? <Loader2 size={11} className="animate-spin" /> : <Upload size={11} />}
                    {t('kb.uploadFile')}
                    <input
                      type="file"
                      multiple
                      accept=".pdf,.docx,.pptx,.html,.txt,.md,.csv,.json"
                      className="hidden"
                      disabled={uploading === kb.id}
                      onChange={(e) => {
                        const files = e.target.files
                        if (files && files.length > 0) handleFileUpload(kb.id, Array.from(files))
                        e.target.value = ''
                      }}
                    />
                  </label>
                </div>
              )
            })}
          </div>
        </div>

        {/* ─── Right: File Detail Panel ─── */}
        {selectedKb && (
          <div className="flex-1 border-l border-border flex flex-col overflow-hidden">
            <div className="flex items-center justify-between border-b border-border bg-card px-4 py-2.5 shrink-0">
              <div className="min-w-0 flex-1">
                <div className="text-xs font-semibold text-foreground truncate">{selectedKb.name}</div>
                <div className="text-[9px] text-muted-foreground">{selectedKb.fileCount ?? 0} files, {selectedKb.totalChunks ?? 0} chunks</div>
                <div className="text-[8px] text-muted-foreground/70 mt-0.5">
                  {selectedKb.embeddingModel} · {selectedKb.chunkStrategy === 'structural' ? t('kb.chunkStructural') : t('kb.chunkFixed')} · {selectedKb.chunkSize}
                </div>
                <div className="text-[8px] text-muted-foreground/70 mt-0.5">
                  {t('dataSource.title')}: {dsName(selectedKb.dataSourceId)}
                </div>
                {files.length > 0 && (
                  <div className="text-[8px] text-muted-foreground/70 mt-0.5">
                    {Object.entries(
                      files.reduce<Record<string, number>>((acc, f) => {
                        const ext = (f.fileName.split('.').pop() ?? '?').toUpperCase()
                        acc[ext] = (acc[ext] ?? 0) + 1
                        return acc
                      }, {})
                    ).map(([ext, count]) => `${ext}: ${count}`).join(' · ')}
                  </div>
                )}
              </div>
              <button
                onClick={() => { setSelectedKb(null); setFiles([]) }}
                className="text-muted-foreground hover:text-foreground cursor-pointer shrink-0 ml-2"
              >
                <X size={14} />
              </button>
            </div>

            {/* URL 輸入行 */}
            <UrlIngestInput kbId={selectedKb.id} onComplete={() => { fetchKbs(); fetchFiles(selectedKb.id) }} />

            <div className="flex-1 overflow-y-auto">
              {filesLoading ? (
                <p className="px-4 py-8 text-xs text-muted-foreground text-center">{t('loading')}</p>
              ) : files.length === 0 ? (
                <div className="px-4 py-8 text-center">
                  <p className="text-xs text-muted-foreground mb-2">{t('kb.noFiles')}</p>
                  <label className="inline-flex items-center gap-1 rounded-md bg-blue-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-blue-500 transition-colors cursor-pointer">
                    <Upload size={12} /> Upload File
                    <input
                      type="file"
                      multiple
                      accept=".pdf,.docx,.pptx,.html,.txt,.md,.csv,.json"
                      className="hidden"
                      onChange={(e) => {
                        const files = e.target.files
                        if (files && files.length > 0) handleFileUpload(selectedKb.id, Array.from(files))
                        e.target.value = ''
                      }}
                    />
                  </label>
                </div>
              ) : (
                <div className="divide-y divide-border">
                  {files.map((f) => (
                    <div key={f.id} className="flex items-center gap-3 px-4 py-2.5 hover:bg-secondary/50 transition-colors group">
                      <File size={14} className="text-blue-400 shrink-0" />
                      <div className="flex-1 min-w-0">
                        <div className="text-xs font-medium text-foreground truncate">{f.fileName}</div>
                        <div className="text-[9px] text-muted-foreground">
                          {fmtSize(f.fileSize)} &middot; {f.chunkCount} chunks &middot; {fmtDate(f.createdAt)}
                        </div>
                      </div>
                      <button
                        onClick={() => handleDeleteFile(f.id, f.fileName)}
                        className="opacity-0 group-hover:opacity-100 text-muted-foreground hover:text-red-400 cursor-pointer shrink-0 p-1"
                        title="Delete file"
                      >
                        <Trash2 size={12} />
                      </button>
                    </div>
                  ))}
                </div>
              )}
            </div>

            {/* ─── Retrieval Test (固定底部) ─── */}
            <RetrievalTestPanel kbId={selectedKb.id} hasFiles={(selectedKb.fileCount ?? 0) > 0} />
          </div>
        )}
      </div>

      {/* Create Dialog */}
      {showCreate && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={() => setShowCreate(false)}>
          <div className="w-[400px] rounded-lg border border-border bg-card shadow-xl" onClick={(e) => e.stopPropagation()}>
            <div className="flex items-center justify-between border-b border-border px-4 py-3">
              <h2 className="text-sm font-semibold text-foreground">{t('kb.newKb')}</h2>
              <button onClick={() => setShowCreate(false)} className="text-muted-foreground hover:text-foreground cursor-pointer"><X size={16} /></button>
            </div>
            <div className="p-4 space-y-3">
              <div>
                <label className="block text-[10px] text-muted-foreground mb-1">Name</label>
                <input className="field-input" value={newName} onChange={(e) => setNewName(e.target.value)} autoFocus />
              </div>
              <div>
                <label className="block text-[10px] text-muted-foreground mb-1">Description</label>
                <textarea className="field-textarea" value={newDesc} onChange={(e) => setNewDesc(e.target.value)} rows={2} />
              </div>
              <div>
                <label className="block text-[10px] text-muted-foreground mb-1">{t('dataSource.field.provider')}</label>
                {dataSources.length === 0 ? (
                  <div className="rounded border border-yellow-500/30 bg-yellow-500/5 p-2 text-[10px] text-yellow-400">
                    {t('kb.noDataSources')}
                    <button
                      type="button"
                      onClick={() => navigate('/settings')}
                      className="ml-1 underline hover:text-yellow-300 transition-colors cursor-pointer"
                    >
                      {t('dataSource.goToSettings')}
                    </button>
                  </div>
                ) : (
                  <>
                    <select className="field-input" value={newDataSourceId} onChange={(e) => setNewDataSourceId(e.target.value)}>
                      <option value="" disabled>{t('kb.selectDataSource')}</option>
                      {dataSources.map(ds => (
                        <option key={ds.id} value={ds.id}>{ds.name} ({ds.provider})</option>
                      ))}
                    </select>
                    <button
                      type="button"
                      onClick={() => navigate('/settings')}
                      className="mt-1 flex items-center gap-1 text-[9px] text-muted-foreground hover:text-blue-400 transition-colors cursor-pointer"
                    >
                      <Settings size={10} /> {t('dataSource.goToSettings')}
                    </button>
                  </>
                )}
              </div>
              <div>
                <label className="block text-[10px] text-muted-foreground mb-1">{t('studio:form.embeddingModel')}</label>
                <select className="field-input" value={newEmbeddingModel} onChange={(e) => setNewEmbeddingModel(e.target.value)}>
                  {EMBEDDING_MODELS.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
                </select>
              </div>
              <div>
                <label className="block text-[10px] text-muted-foreground mb-1">{t('kb.chunkStrategy')}</label>
                <select className="field-input" value={newChunkStrategy} onChange={(e) => {
                  const strategy = e.target.value
                  setNewChunkStrategy(strategy)
                  // 智慧預設：根據策略推薦 chunk size
                  if (strategy === 'structural') {
                    setNewChunkSize(1024)  // 結構切割段落較大
                    setNewChunkOverlap(100)
                  } else {
                    setNewChunkSize(512)
                    setNewChunkOverlap(50)
                  }
                }}>
                  <option value="fixed">{t('kb.chunkFixed')}</option>
                  <option value="structural">{t('kb.chunkStructural')}</option>
                </select>
                <p className="text-[8px] text-muted-foreground/70 mt-0.5">
                  {newChunkStrategy === 'structural' ? t('kb.chunkStructuralHint') : t('kb.chunkFixedHint')}
                </p>
              </div>
              <div className="grid grid-cols-2 gap-2">
                <div>
                  <label className="block text-[10px] text-muted-foreground mb-1">{t('studio:form.chunkSize')}</label>
                  <input type="number" className="field-input" value={newChunkSize} min={64} max={4096} step={64}
                    onChange={(e) => setNewChunkSize(Number(e.target.value))} />
                </div>
                <div>
                  <label className="block text-[10px] text-muted-foreground mb-1">{t('studio:form.chunkOverlap')}</label>
                  <input type="number" className="field-input" value={newChunkOverlap} min={0} max={500} step={10}
                    onChange={(e) => setNewChunkOverlap(Number(e.target.value))} />
                </div>
              </div>
              <p className="text-[9px] text-muted-foreground/70 italic">{t('kb.settingsImmutableHint')}</p>
              <button
                onClick={handleCreate}
                disabled={!newName.trim() || !newDataSourceId}
                className="w-full rounded-md bg-blue-600 px-3 py-2 text-xs font-semibold text-white hover:bg-blue-500 disabled:opacity-50 transition-colors cursor-pointer"
              >
                {t('common:create')}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Edit Dialog */}
      {editing && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50" onClick={() => setEditing(null)}>
          <div className="w-[400px] rounded-lg border border-border bg-card shadow-xl" onClick={(e) => e.stopPropagation()}>
            <div className="flex items-center justify-between border-b border-border px-4 py-3">
              <h2 className="text-sm font-semibold text-foreground">{t('kb.editTitle')}</h2>
              <button onClick={() => setEditing(null)} className="text-muted-foreground hover:text-foreground cursor-pointer"><X size={16} /></button>
            </div>
            <div className="p-4 space-y-3">
              <div>
                <label className="block text-[10px] text-muted-foreground mb-1">Name</label>
                <input className="field-input" value={editName} onChange={(e) => setEditName(e.target.value)} autoFocus />
              </div>
              <div>
                <label className="block text-[10px] text-muted-foreground mb-1">Description</label>
                <textarea className="field-textarea" value={editDesc} onChange={(e) => setEditDesc(e.target.value)} rows={2} />
              </div>
              <button
                onClick={handleEdit}
                disabled={!editName.trim()}
                className="w-full rounded-md bg-blue-600 px-3 py-2 text-xs font-semibold text-white hover:bg-blue-500 disabled:opacity-50 transition-colors cursor-pointer"
              >
                {t('save')}
              </button>
            </div>
          </div>
        </div>
      )}
      {confirmDialog}
    </div>
  )
}

// ─── URL Ingest Input ───

function UrlIngestInput({ kbId, onComplete }: { kbId: string; onComplete: () => void }) {
  const { t } = useTranslation('studio')
  const [url, setUrl] = useState('')
  const [loading, setLoading] = useState(false)
  const [msg, setMsg] = useState('')

  const handleSubmit = async () => {
    if (!url.trim()) return
    setLoading(true)
    setMsg('')
    try {
      await api.knowledgeBases.addUrl(kbId, url.trim(), (evt) => {
        setMsg(evt.text)
      })
      setUrl('')
      onComplete()
    } catch (err: any) {
      setMsg(err?.message ?? 'Failed')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="px-4 py-2 border-b border-border/50 shrink-0">
      <div className="flex gap-1.5">
        <input
          className="field-input flex-1 text-[10px]"
          placeholder={t('kb.urlPlaceholder')}
          value={url}
          onChange={(e) => setUrl(e.target.value)}
          onKeyDown={(e) => e.key === 'Enter' && handleSubmit()}
        />
        <button
          onClick={handleSubmit}
          disabled={loading || !url.trim()}
          className="rounded-md bg-blue-600 px-2.5 py-1 text-[10px] font-semibold text-white hover:bg-blue-500 disabled:opacity-50 transition-colors cursor-pointer shrink-0"
        >
          {loading ? <Loader2 size={12} className="animate-spin" /> : t('kb.addUrl')}
        </button>
      </div>
      {msg && <p className="text-[9px] text-muted-foreground mt-1">{msg}</p>}
    </div>
  )
}

// ─── Retrieval Test Panel ───

interface SearchResult {
  content: string
  fileName: string
  chunkIndex: number
  score: number
}

function RetrievalTestPanel({ kbId, hasFiles }: { kbId: string; hasFiles: boolean }) {
  const { t } = useTranslation('studio')
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<SearchResult[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [expandedIdx, setExpandedIdx] = useState<number | null>(null)
  const [showParams, setShowParams] = useState(false)
  const [topK, setTopK] = useState(5)
  const [searchMode, setSearchMode] = useState('hybrid')
  const [minScore, setMinScore] = useState(0.005)
  const [queryExpansion, setQueryExpansion] = useState(false)
  const [expandedQueriesResult, setExpandedQueriesResult] = useState<string[]>([])

  // 切換 KB 時清空搜尋狀態
  useEffect(() => {
    setResults([])
    setError('')
    setQuery('')
    setExpandedIdx(null)
  }, [kbId])

  const handleSearch = async () => {
    if (!query.trim()) return
    setLoading(true)
    setError('')
    setResults([])
    setExpandedIdx(null)
    setExpandedQueriesResult([])
    try {
      const res = await api.knowledgeBases.testSearch(kbId, {
        query: query.trim(), topK, searchMode,
        minScore: minScore > 0 ? minScore : undefined,
        queryExpansion,
      })
      // API 回傳 { results, expandedQueries } 或直接 array（向下相容）
      if (Array.isArray(res)) {
        setResults(res)
      } else {
        setResults((res as any).results ?? [])
        setExpandedQueriesResult((res as any).expandedQueries ?? [])
      }
    } catch (err: any) {
      setError(err?.message ?? 'Search failed')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="border-t border-border bg-card shrink-0">
      <div className="px-4 py-2">
        <div className="flex items-center gap-1.5 mb-2">
          <Search size={11} className="text-muted-foreground" />
          <span className="text-[9px] font-semibold uppercase tracking-wider text-muted-foreground">
            {t('kb.retrievalTest')}
          </span>
          <button
            type="button"
            className="ml-auto text-[8px] text-muted-foreground hover:text-foreground cursor-pointer flex items-center gap-0.5"
            onClick={() => setShowParams(!showParams)}
          >
            {showParams ? <ChevronUp size={10} /> : <ChevronDown size={10} />}
            {t('kb.params')}
          </button>
        </div>

        {showParams && (<>
          <div className="grid grid-cols-3 gap-1.5 mb-2">
            <div>
              <label className="block text-[8px] text-muted-foreground mb-0.5">Top K</label>
              <input type="number" className="field-input text-[10px] py-0.5" value={topK} min={1} max={20}
                onChange={(e) => setTopK(Number(e.target.value))} />
            </div>
            <div>
              <label className="block text-[8px] text-muted-foreground mb-0.5">{t('form.searchMode')}</label>
              <select className="field-input text-[10px] py-0.5" value={searchMode}
                onChange={(e) => setSearchMode(e.target.value)}>
                <option value="hybrid">Hybrid</option>
                <option value="vector">Vector</option>
                <option value="fulltext">Full Text</option>
              </select>
            </div>
            <div>
              <label className="block text-[8px] text-muted-foreground mb-0.5">{t('form.minScore')}</label>
              <input type="number" className="field-input text-[10px] py-0.5" value={minScore} min={0} max={1} step={0.001}
                onChange={(e) => setMinScore(Number(e.target.value))} />
            </div>
          </div>
          <label className="flex items-center gap-1.5 mb-2 cursor-pointer">
            <input type="checkbox" checked={queryExpansion} onChange={(e) => setQueryExpansion(e.target.checked)} />
            <span className="text-[8px] text-muted-foreground">{t('form.queryExpansion')}</span>
          </label>
        </>)}

        {!hasFiles ? (
          <p className="text-[9px] text-muted-foreground italic">{t('kb.uploadFirst')}</p>
        ) : (
          <div className="flex gap-1.5">
            <input
              className="field-input flex-1 text-[10px]"
              placeholder={t('kb.testPlaceholder')}
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
            />
            <button
              onClick={handleSearch}
              disabled={loading || !query.trim()}
              className="rounded-md bg-blue-600 px-2.5 py-1 text-[10px] font-semibold text-white hover:bg-blue-500 disabled:opacity-50 transition-colors cursor-pointer shrink-0"
            >
              {loading ? <Loader2 size={12} className="animate-spin" /> : <Search size={12} />}
            </button>
          </div>
        )}

        {error && <p className="text-[9px] text-red-400 mt-1">{error}</p>}
      </div>

      {results.length > 0 && (
        <div className="max-h-[200px] overflow-y-auto border-t border-border/50">
          {expandedQueriesResult.length > 0 && (
            <div className="px-4 py-1.5 border-b border-border/30 bg-accent/10">
              <span className="text-[8px] text-muted-foreground font-medium">Query Expansion: </span>
              {expandedQueriesResult.map((q, i) => (
                <span key={i} className="text-[8px] text-blue-400/80">{i > 0 ? ' · ' : ''}{q}</span>
              ))}
            </div>
          )}
          {results.map((r, i) => (
            <div
              key={i}
              className="px-4 py-2 border-b border-border/30 hover:bg-secondary/50 cursor-pointer transition-colors"
              onClick={() => setExpandedIdx(expandedIdx === i ? null : i)}
            >
              <div className="flex items-center justify-between">
                <span className="text-[9px] text-muted-foreground">
                  {r.fileName && <>{r.fileName} · </>}Section {r.chunkIndex + 1}
                </span>
                <span className={`text-[9px] font-mono ${
                  searchMode === 'hybrid'
                    ? (r.score >= 0.02 ? 'text-green-400' : r.score >= 0.01 ? 'text-yellow-400' : 'text-muted-foreground')
                    : (r.score >= 0.5 ? 'text-green-400' : r.score >= 0.2 ? 'text-yellow-400' : 'text-muted-foreground')
                }`}>
                  {r.score.toFixed(3)}
                </span>
              </div>
              <p className={`text-[10px] text-foreground mt-0.5 ${expandedIdx === i ? '' : 'line-clamp-2'}`}>
                {r.content}
              </p>
            </div>
          ))}
        </div>
      )}

      {!loading && results.length === 0 && query && !error && (
        <p className="px-4 py-2 text-[9px] text-muted-foreground italic border-t border-border/50">
          {t('kb.noResults')}
        </p>
      )}
    </div>
  )
}
