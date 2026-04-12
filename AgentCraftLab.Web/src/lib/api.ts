const BASE = ''  // Vite proxy handles /api → localhost:5200

async function request<T>(url: string, options?: RequestInit): Promise<T> {
  const isFormData = options?.body instanceof FormData
  const headers = isFormData
    ? options?.headers  // FormData sets Content-Type automatically (with boundary)
    : { 'Content-Type': 'application/json', ...options?.headers }
  const res = await fetch(`${BASE}${url}`, { ...options, headers })
  if (!res.ok) {
    const err = await res.json().catch(() => ({ code: 'UNKNOWN', message: res.statusText }))
    throw err
  }
  if (res.status === 204) return undefined as T
  const text = await res.text()
  if (!text) return undefined as T
  return JSON.parse(text)
}

export interface WorkflowDocument {
  id: string;
  userId: string;
  name: string;
  description: string;
  type: string;
  workflowJson: string;
  isPublished: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface TemplateDocument {
  id: string;
  userId: string;
  name: string;
  description: string;
  category: string;
  icon: string;
  tags: string;
  workflowJson: string;
  isPublic: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface ApiKeyInfo {
  id: string;
  name: string;
  keyPrefix: string;
  scopedWorkflowIds: string;
  isRevoked: boolean;
  lastUsedAt: string | null;
  expiresAt: string | null;
  createdAt: string;
}

export interface ApiKeyCreateResult extends ApiKeyInfo {
  rawKey: string;
}

export interface CredentialInfo {
  id: string;
  provider: string;
  name: string;
  hasApiKey: boolean;
  endpoint: string;
  model: string;
  createdAt: string;
  updatedAt: string;
}

export interface KbFileDocument {
  id: string;
  knowledgeBaseId: string;
  fileName: string;
  mimeType: string;
  fileSize: number;
  chunkCount: number;
  createdAt: string;
}

export interface DataSourceDocument {
  id: string;
  userId: string;
  name: string;
  provider: string;
  description: string;
  configJson: string;
  createdAt: string;
  updatedAt: string;
}

export interface CreateDataSourceRequest {
  name: string;
  description?: string;
  provider: string;
  configJson?: string;
}

export interface UpdateDataSourceRequest {
  name: string;
  description?: string;
  provider?: string;
  configJson?: string;
}

// DocRefinery types
export interface RefineryProject {
  id: string; name: string; description: string;
  schemaTemplateId?: string; customSchemaJson?: string;
  provider: string; model: string; outputLanguage?: string;
  extractionMode: string; // "fast" | "precise"
  enableChallenge: boolean;
  imageProcessingMode: string; // "skip" | "ocr" | "ai-describe" | "hybrid"
  fileCount: number;
  createdAt: string; updatedAt: string;
}
export interface RefineryFileDoc {
  id: string; refineryProjectId: string;
  fileName: string; mimeType: string; fileSize: number;
  elementCount: number;
  isIncluded: boolean;
  indexStatus: string; // Pending | Indexing | Indexed | Failed | Skipped
  chunkCount: number;
  createdAt: string;
}
export interface RefineryOutputDoc {
  id: string; refineryProjectId: string; version: number;
  schemaTemplateId?: string; schemaName: string;
  outputJson: string; outputMarkdown: string;
  missingFields: string; openQuestions: string;
  challenges: string; overallConfidence: number;
  sourceFiles: string; sourceFileCount: number; createdAt: string;
}
export interface FieldChallengeDoc {
  field: string; originalValue: string; challengeReason: string;
  suggestedValue?: string; confidence: number;
  action: 'Accept' | 'Flag' | 'Reject';
}
export interface SchemaTemplateSummary {
  id: string; name: string; description: string; category: string;
}

export interface ScheduleDocument {
  id: string;
  userId: string;
  workflowId: string;
  workflowName: string;
  cronExpression: string;
  timeZone: string;
  enabled: boolean;
  defaultInput: string;
  lastRunAt: string | null;
  nextRunAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ScheduleLogDocument {
  id: string;
  scheduleId: string;
  workflowId: string;
  userId: string;
  success: boolean;
  output: string | null;
  error: string | null;
  elapsedMs: number;
  createdAt: string;
  statusText: string;
}

export interface ScheduleRequest {
  id?: string;
  workflowId: string;
  cronExpression: string;
  timeZone?: string;
  enabled?: boolean;
  defaultInput?: string;
}

export const api = {
  workflows: {
    list: () => request<WorkflowDocument[]>('/api/workflows'),
    get: (id: string) => request<WorkflowDocument>(`/api/workflows/${id}`),
    create: (data: { name: string; description?: string; type?: string; workflowJson?: string }) =>
      request<WorkflowDocument>('/api/workflows', { method: 'POST', body: JSON.stringify(data) }),
    update: (id: string, data: { name: string; description?: string; type?: string; workflowJson?: string }) =>
      request<WorkflowDocument>(`/api/workflows/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    delete: (id: string) => request<void>(`/api/workflows/${id}`, { method: 'DELETE' }),
    publish: (id: string, isPublished: boolean, inputModes?: string[]) =>
      request<void>(`/api/workflows/${id}/publish`, { method: 'PATCH', body: JSON.stringify({ isPublished, inputModes }) }),
  },
  tools: {
    list: () => request<{ id: string; name: string; description: string; category: string; icon: string }[]>('/api/tools'),
  },
  mcp: {
    discover: (url: string) =>
      request<{ healthy: boolean; tools?: { name: string; description: string }[]; error?: string }>('/api/mcp/discover', { method: 'POST', body: JSON.stringify({ url }) }),
  },
  a2a: {
    discover: (url: string, format = 'auto') =>
      request<{ healthy: boolean; agent?: { name: string; description: string }; error?: string }>('/api/a2a/discover', { method: 'POST', body: JSON.stringify({ url, format }) }),
    test: (url: string, message: string, format = 'auto') =>
      request<{ success: boolean; response?: string; error?: string }>('/api/a2a/test', { method: 'POST', body: JSON.stringify({ url, message, format }) }),
  },
  httpTools: {
    test: (data: { url: string; method?: string; body?: string; input?: string }) =>
      request<{ success: boolean; response?: string; error?: string }>('/api/http-tools/test', { method: 'POST', body: JSON.stringify(data) }),
  },
  knowledgeBases: {
    list: () => request<any[]>('/api/knowledge-bases'),
    get: (id: string) => request<any>(`/api/knowledge-bases/${id}`),
    create: (data: { name: string; description?: string; dataSourceId?: string; embeddingModel?: string; chunkSize?: number; chunkOverlap?: number; chunkStrategy?: string }) =>
      request<any>('/api/knowledge-bases', { method: 'POST', body: JSON.stringify(data) }),
    update: (id: string, data: { name?: string; description?: string }) =>
      request<any>(`/api/knowledge-bases/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    delete: (id: string) => request<void>(`/api/knowledge-bases/${id}`, { method: 'DELETE' }),
    listFiles: (id: string) =>
      request<KbFileDocument[]>(`/api/knowledge-bases/${id}/files`),
    deleteFile: (kbId: string, fileId: string) =>
      request<void>(`/api/knowledge-bases/${kbId}/files/${fileId}`, { method: 'DELETE' }),
    addUrl: async (id: string, url: string, onProgress?: (evt: { type: string; text: string }) => void): Promise<string> => {
      const res = await fetch(`/api/knowledge-bases/${id}/urls`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ url }),
      })
      if (!res.ok) throw new Error(`URL ingest failed: ${res.status}`)
      const reader = res.body?.getReader()
      const decoder = new TextDecoder()
      let lastMsg = ''
      if (reader) {
        while (true) {
          const { done, value } = await reader.read()
          if (done) break
          const text = decoder.decode(value, { stream: true })
          for (const line of text.split('\n')) {
            if (line.startsWith('data: ')) {
              try {
                const evt = JSON.parse(line.slice(6))
                lastMsg = evt.text ?? ''
                onProgress?.(evt)
              } catch { /* ignore parse errors */ }
            }
          }
        }
      }
      return lastMsg
    },
    testSearch: (id: string, data: { query: string; topK?: number; searchMode?: string; minScore?: number; queryExpansion?: boolean }) =>
      request<any>(
        `/api/knowledge-bases/${id}/test-search`, { method: 'POST', body: JSON.stringify(data) }),
    /** 上傳單檔（向下相容）。 */
    uploadFile: async (id: string, file: File, onProgress?: (evt: { type: string; text: string }) => void): Promise<string> => {
      const form = new FormData()
      form.append('file', file)
      const res = await fetch(`${BASE}/api/knowledge-bases/${id}/files`, { method: 'POST', body: form })
      if (!res.ok) {
        const err = await res.json().catch(() => ({ code: 'UNKNOWN', message: res.statusText }))
        throw err
      }
      if (!res.body) return ''
      const reader = res.body.getReader()
      const decoder = new TextDecoder()
      let buffer = ''
      let lastText = ''
      while (true) {
        const { done, value } = await reader.read()
        if (done) break
        buffer += decoder.decode(value, { stream: true })
        const lines = buffer.split('\n')
        buffer = lines.pop() ?? ''
        for (const line of lines) {
          if (!line.startsWith('data: ')) continue
          try {
            const evt = JSON.parse(line.slice(6))
            lastText = evt.text ?? ''
            onProgress?.(evt)
          } catch { /* ignore parse errors */ }
        }
      }
      return lastText
    },
    /** 上傳多檔並透過 SSE 接收 ingest 進度。onProgress 每次收到事件時回呼。 */
    uploadFiles: async (id: string, files: File[], onProgress?: (evt: { type: string; text: string; fileName?: string }) => void): Promise<string> => {
      const form = new FormData()
      for (const file of files) form.append('file', file)
      const res = await fetch(`${BASE}/api/knowledge-bases/${id}/files`, { method: 'POST', body: form })
      if (!res.ok) {
        const err = await res.json().catch(() => ({ code: 'UNKNOWN', message: res.statusText }))
        throw err
      }
      if (!res.body) return ''
      const reader = res.body.getReader()
      const decoder = new TextDecoder()
      let buffer = ''
      let lastText = ''
      while (true) {
        const { done, value } = await reader.read()
        if (done) break
        buffer += decoder.decode(value, { stream: true })
        const lines = buffer.split('\n')
        buffer = lines.pop() ?? ''
        for (const line of lines) {
          if (!line.startsWith('data: ')) continue
          try {
            const evt = JSON.parse(line.slice(6))
            lastText = evt.text ?? ''
            onProgress?.(evt)
          } catch { /* ignore parse errors */ }
        }
      }
      return lastText
    },
  },
  dataSources: {
    list: () => request<DataSourceDocument[]>('/api/data-sources'),
    get: (id: string) => request<DataSourceDocument>(`/api/data-sources/${id}`),
    create: (data: CreateDataSourceRequest) => request<DataSourceDocument>('/api/data-sources', { method: 'POST', body: JSON.stringify(data) }),
    update: (id: string, data: UpdateDataSourceRequest) => request<DataSourceDocument>(`/api/data-sources/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    delete: (id: string) => request<void>(`/api/data-sources/${id}`, { method: 'DELETE' }),
    test: (id: string) => request<{ success: boolean; message: string; latencyMs: number }>(`/api/data-sources/${id}/test`, { method: 'POST' }),
  },
  skills: {
    list: () => request<{ builtin: any[]; custom: any[] }>('/api/skills'),
    create: (data: any) => request<any>('/api/skills', { method: 'POST', body: JSON.stringify(data), headers: { 'Content-Type': 'application/json' } }),
    update: (id: string, data: any) => request<any>(`/api/skills/${id}`, { method: 'PUT', body: JSON.stringify(data), headers: { 'Content-Type': 'application/json' } }),
    delete: (id: string) => request<void>(`/api/skills/${id}`, { method: 'DELETE' }),
    exportMd: (id: string) => fetch(`/api/skills/${id}/export`).then(r => r.text()),
    importMd: (file: File) => {
      const form = new FormData()
      form.append('file', file)
      return request<any>('/api/skills/import', { method: 'POST', body: form })
    },
  },
  humanInput: {
    submit: (data: { threadId: string; runId: string; response: string }) =>
      request<{ success: boolean }>('/ag-ui/human-input', { method: 'POST', body: JSON.stringify(data) }),
  },
  templates: {
    list: () => request<TemplateDocument[]>('/api/templates'),
    create: (data: { name: string; description?: string; category?: string; icon?: string; tags?: string[]; workflowJson?: string }) =>
      request<TemplateDocument>('/api/templates', { method: 'POST', body: JSON.stringify(data) }),
    update: (id: string, data: { name?: string; description?: string; category?: string; icon?: string; tags?: string[]; workflowJson?: string }) =>
      request<TemplateDocument>(`/api/templates/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    delete: (id: string) => request<void>(`/api/templates/${id}`, { method: 'DELETE' }),
  },
  apiKeys: {
    list: () => request<ApiKeyInfo[]>('/api/keys'),
    create: (data: { name: string; scopedWorkflowIds?: string; expiresAt?: string }) =>
      request<ApiKeyCreateResult>('/api/keys', { method: 'POST', body: JSON.stringify(data) }),
    revoke: (id: string) => request<void>(`/api/keys/${id}`, { method: 'DELETE' }),
  },
  credentials: {
    list: () => request<CredentialInfo[]>('/api/credentials'),
    save: (data: { provider: string; name?: string; apiKey?: string; endpoint?: string; model?: string }) =>
      request<CredentialInfo>('/api/credentials', { method: 'POST', body: JSON.stringify(data) }),
    update: (id: string, data: { provider?: string; name?: string; apiKey?: string; endpoint?: string; model?: string }) =>
      request<CredentialInfo>(`/api/credentials/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    delete: (id: string) => request<void>(`/api/credentials/${id}`, { method: 'DELETE' }),
  },
  upload: {
    file: async (file: File): Promise<{ fileId: string; fileName: string; size: number }> => {
      const form = new FormData()
      form.append('file', file)
      return request('/api/upload', { method: 'POST', body: form })
    },
  },
  traces: {
    getByLogId: (logId: string) => request<any>(`/api/traces/log/${logId}`),
  },
  schedules: {
    list: () => request<ScheduleDocument[]>('/api/schedules'),
    get: (id: string) => request<ScheduleDocument>(`/api/schedules/${id}`),
    create: (data: ScheduleRequest) =>
      request<ScheduleDocument>('/api/schedules', { method: 'POST', body: JSON.stringify(data) }),
    toggle: (id: string) =>
      request<ScheduleDocument>(`/api/schedules/${id}/toggle`, { method: 'PATCH' }),
    delete: (id: string) => request<void>(`/api/schedules/${id}`, { method: 'DELETE' }),
    logs: (id: string, limit = 20) =>
      request<ScheduleLogDocument[]>(`/api/schedules/${id}/logs?limit=${limit}`),
  },
  refinery: {
    list: () => request<RefineryProject[]>('/api/refinery'),
    get: (id: string) => request<RefineryProject>(`/api/refinery/${id}`),
    create: (data: { name: string; description?: string; schemaTemplateId?: string; customSchemaJson?: string; provider?: string; model?: string; outputLanguage?: string; extractionMode?: string }) =>
      request<RefineryProject>('/api/refinery', { method: 'POST', body: JSON.stringify(data) }),
    update: (id: string, data: { name?: string; description?: string; schemaTemplateId?: string; customSchemaJson?: string; provider?: string; model?: string; outputLanguage?: string; extractionMode?: string; enableChallenge?: boolean; imageProcessingMode?: string }) =>
      request<RefineryProject>(`/api/refinery/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
    delete: (id: string) => request<void>(`/api/refinery/${id}`, { method: 'DELETE' }),
    listFiles: (id: string) => request<RefineryFileDoc[]>(`/api/refinery/${id}/files`),
    deleteFile: (projectId: string, fileId: string) =>
      request<void>(`/api/refinery/${projectId}/files/${fileId}`, { method: 'DELETE' }),
    toggleFileIncluded: (projectId: string, fileId: string) =>
      request<{ isIncluded: boolean }>(`/api/refinery/${projectId}/files/${fileId}/toggle`, { method: 'PATCH' }),
    previewFile: (projectId: string, fileId: string) =>
      request<any[]>(`/api/refinery/${projectId}/files/${fileId}/preview`),
    reindexFile: async (projectId: string, fileId: string, onProgress?: (evt: { type: string; text: string }) => void): Promise<string> => {
      const res = await fetch(`${BASE}/api/refinery/${projectId}/files/${fileId}/reindex`, { method: 'POST' })
      if (!res.ok) throw new Error(`Reindex failed: ${res.status}`)
      if (!res.body) return ''
      const reader = res.body.getReader()
      const decoder = new TextDecoder()
      let buffer = '', lastText = ''
      while (true) {
        const { done, value } = await reader.read()
        if (done) break
        buffer += decoder.decode(value, { stream: true })
        const lines = buffer.split('\n')
        buffer = lines.pop() ?? ''
        for (const line of lines) {
          if (!line.startsWith('data: ')) continue
          try { const evt = JSON.parse(line.slice(6)); lastText = evt.text ?? ''; onProgress?.(evt) } catch { /* ignore */ }
        }
      }
      return lastText
    },
    uploadFiles: async (id: string, files: File[], onProgress?: (evt: { type: string; text: string; fileName?: string }) => void): Promise<string> => {
      const form = new FormData()
      for (const file of files) form.append('file', file)
      const res = await fetch(`${BASE}/api/refinery/${id}/files`, { method: 'POST', body: form })
      if (!res.ok) throw new Error(`Upload failed: ${res.status}`)
      if (!res.body) return ''
      const reader = res.body.getReader()
      const decoder = new TextDecoder()
      let buffer = '', lastText = ''
      while (true) {
        const { done, value } = await reader.read()
        if (done) break
        buffer += decoder.decode(value, { stream: true })
        const lines = buffer.split('\n')
        buffer = lines.pop() ?? ''
        for (const line of lines) {
          if (!line.startsWith('data: ')) continue
          try { const evt = JSON.parse(line.slice(6)); lastText = evt.text ?? ''; onProgress?.(evt) } catch { /* ignore */ }
        }
      }
      return lastText
    },
    generate: async (id: string, onProgress?: (evt: { type: string; text: string }) => void): Promise<string> => {
      const res = await fetch(`${BASE}/api/refinery/${id}/generate`, { method: 'POST' })
      if (!res.ok) throw new Error(`Generate failed: ${res.status}`)
      if (!res.body) return ''
      const reader = res.body.getReader()
      const decoder = new TextDecoder()
      let buffer = '', lastText = ''
      while (true) {
        const { done, value } = await reader.read()
        if (done) break
        buffer += decoder.decode(value, { stream: true })
        const lines = buffer.split('\n')
        buffer = lines.pop() ?? ''
        for (const line of lines) {
          if (!line.startsWith('data: ')) continue
          try { const evt = JSON.parse(line.slice(6)); lastText = evt.text ?? ''; onProgress?.(evt) } catch { /* ignore */ }
        }
      }
      return lastText
    },
    listOutputs: (id: string) => request<RefineryOutputDoc[]>(`/api/refinery/${id}/outputs`),
    getLatestOutput: (id: string) => request<RefineryOutputDoc>(`/api/refinery/${id}/outputs/latest`),
    getOutput: (id: string, version: number) => request<RefineryOutputDoc>(`/api/refinery/${id}/outputs/${version}`),
    listSchemaTemplates: () => request<SchemaTemplateSummary[]>('/api/schema-templates'),
  },
  debug: {
    submitAction: (data: { threadId?: string; runId?: string; action: 'continue' | 'rerun' | 'skip' }) =>
      request<{ success: boolean }>('/ag-ui/debug-action', { method: 'POST', body: JSON.stringify(data) }),
  },
}
