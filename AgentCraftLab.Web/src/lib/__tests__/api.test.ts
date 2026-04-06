import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { api } from '../api'

// Mock global fetch
const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

function jsonResponse(data: unknown, status = 200) {
  return new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

describe('api', () => {
  beforeEach(() => {
    mockFetch.mockReset()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  describe('request helper (via api.workflows)', () => {
    it('fetches and parses JSON response', async () => {
      const data = [{ id: '1', name: 'Test' }]
      mockFetch.mockResolvedValueOnce(jsonResponse(data))

      const result = await api.workflows.list()
      expect(result).toEqual(data)
      expect(mockFetch).toHaveBeenCalledWith('/api/workflows', expect.objectContaining({
        headers: expect.objectContaining({ 'Content-Type': 'application/json' }),
      }))
    })

    it('throws on non-ok response', async () => {
      mockFetch.mockResolvedValueOnce(
        new Response(JSON.stringify({ code: 'NOT_FOUND', message: 'Not found' }), { status: 404 }),
      )

      await expect(api.workflows.get('invalid')).rejects.toEqual({
        code: 'NOT_FOUND',
        message: 'Not found',
      })
    })

    it('handles non-JSON error responses', async () => {
      mockFetch.mockResolvedValueOnce(
        new Response('Internal Server Error', { status: 500, statusText: 'Internal Server Error' }),
      )

      await expect(api.workflows.get('x')).rejects.toEqual({
        code: 'UNKNOWN',
        message: 'Internal Server Error',
      })
    })

    it('returns undefined for 204 No Content', async () => {
      mockFetch.mockResolvedValueOnce(new Response(null, { status: 204 }))

      const result = await api.workflows.delete('1')
      expect(result).toBeUndefined()
    })

    it('returns undefined for empty body', async () => {
      mockFetch.mockResolvedValueOnce(new Response('', { status: 200 }))

      const result = await api.workflows.delete('1')
      expect(result).toBeUndefined()
    })

    it('sends POST with JSON body', async () => {
      mockFetch.mockResolvedValueOnce(jsonResponse({ id: 'new', name: 'Test' }))

      await api.workflows.create({ name: 'Test' })

      const [, opts] = mockFetch.mock.calls[0]
      expect(opts.method).toBe('POST')
      expect(JSON.parse(opts.body)).toEqual({ name: 'Test' })
    })

    it('sends PUT with JSON body', async () => {
      mockFetch.mockResolvedValueOnce(jsonResponse({ id: '1', name: 'Updated' }))

      await api.workflows.update('1', { name: 'Updated' })

      const [url, opts] = mockFetch.mock.calls[0]
      expect(url).toBe('/api/workflows/1')
      expect(opts.method).toBe('PUT')
    })

    it('sends PATCH for publish', async () => {
      mockFetch.mockResolvedValueOnce(new Response('', { status: 200 }))

      await api.workflows.publish('1', true, ['text'])

      const [url, opts] = mockFetch.mock.calls[0]
      expect(url).toBe('/api/workflows/1/publish')
      expect(opts.method).toBe('PATCH')
      expect(JSON.parse(opts.body)).toEqual({ isPublished: true, inputModes: ['text'] })
    })
  })

  describe('mcp.discover', () => {
    it('sends POST with url', async () => {
      mockFetch.mockResolvedValueOnce(jsonResponse({ healthy: true, tools: [] }))

      await api.mcp.discover('http://localhost:3001/mcp')

      const [, opts] = mockFetch.mock.calls[0]
      expect(opts.method).toBe('POST')
      expect(JSON.parse(opts.body).url).toBe('http://localhost:3001/mcp')
    })
  })

  describe('a2a.discover', () => {
    it('sends POST with url and format', async () => {
      mockFetch.mockResolvedValueOnce(jsonResponse({ healthy: true }))

      await api.a2a.discover('http://example.com/a2a', 'google')

      const body = JSON.parse(mockFetch.mock.calls[0][1].body)
      expect(body.url).toBe('http://example.com/a2a')
      expect(body.format).toBe('google')
    })
  })

  describe('templates', () => {
    it('list fetches GET /api/templates', async () => {
      mockFetch.mockResolvedValueOnce(jsonResponse([{ id: 'tpl-1', name: 'Test' }]))

      const result = await api.templates.list()
      expect(result).toEqual([{ id: 'tpl-1', name: 'Test' }])
      expect(mockFetch.mock.calls[0][0]).toBe('/api/templates')
    })

    it('create sends POST with template data', async () => {
      mockFetch.mockResolvedValueOnce(jsonResponse({ id: 'tpl-new', name: 'My Tpl' }))

      await api.templates.create({ name: 'My Tpl', category: 'Custom', workflowJson: '{}' })

      const [url, opts] = mockFetch.mock.calls[0]
      expect(url).toBe('/api/templates')
      expect(opts.method).toBe('POST')
      const body = JSON.parse(opts.body)
      expect(body.name).toBe('My Tpl')
      expect(body.category).toBe('Custom')
    })

    it('delete sends DELETE /api/templates/{id}', async () => {
      mockFetch.mockResolvedValueOnce(new Response(null, { status: 204 }))

      await api.templates.delete('tpl-1')
      expect(mockFetch.mock.calls[0][0]).toBe('/api/templates/tpl-1')
      expect(mockFetch.mock.calls[0][1].method).toBe('DELETE')
    })
  })

  describe('apiKeys', () => {
    it('list fetches GET /api/keys', async () => {
      const data = [{ id: 'ak-1', name: 'Prod', keyPrefix: 'ack_abc123' }]
      mockFetch.mockResolvedValueOnce(jsonResponse(data))

      const result = await api.apiKeys.list()
      expect(result).toEqual(data)
      expect(mockFetch.mock.calls[0][0]).toBe('/api/keys')
    })

    it('create sends POST with name, scope, and expiresAt', async () => {
      mockFetch.mockResolvedValueOnce(jsonResponse({ id: 'ak-new', rawKey: 'ack_test123' }))

      await api.apiKeys.create({ name: 'Test', scopedWorkflowIds: 'wf-1', expiresAt: '2026-12-31' })

      const [url, opts] = mockFetch.mock.calls[0]
      expect(url).toBe('/api/keys')
      expect(opts.method).toBe('POST')
      const body = JSON.parse(opts.body)
      expect(body.name).toBe('Test')
      expect(body.scopedWorkflowIds).toBe('wf-1')
      expect(body.expiresAt).toBe('2026-12-31')
    })

    it('revoke sends DELETE /api/keys/{id}', async () => {
      mockFetch.mockResolvedValueOnce(new Response(null, { status: 204 }))

      await api.apiKeys.revoke('ak-1')

      const [url, opts] = mockFetch.mock.calls[0]
      expect(url).toBe('/api/keys/ak-1')
      expect(opts.method).toBe('DELETE')
    })
  })

  describe('knowledgeBases (new methods)', () => {
    it('get fetches GET /api/knowledge-bases/{id}', async () => {
      mockFetch.mockResolvedValueOnce(jsonResponse({ id: 'kb-1', name: 'Test' }))

      const result = await api.knowledgeBases.get('kb-1')
      expect(result).toEqual({ id: 'kb-1', name: 'Test' })
      expect(mockFetch.mock.calls[0][0]).toBe('/api/knowledge-bases/kb-1')
    })

    it('update sends PUT with name and description', async () => {
      mockFetch.mockResolvedValueOnce(jsonResponse({ id: 'kb-1', name: 'Updated' }))

      await api.knowledgeBases.update('kb-1', { name: 'Updated', description: 'New desc' })

      const [url, opts] = mockFetch.mock.calls[0]
      expect(url).toBe('/api/knowledge-bases/kb-1')
      expect(opts.method).toBe('PUT')
      expect(JSON.parse(opts.body)).toEqual({ name: 'Updated', description: 'New desc' })
    })

    it('listFiles fetches GET /api/knowledge-bases/{id}/files', async () => {
      const data = [{ id: 'f-1', fileName: 'doc.pdf', chunkCount: 5 }]
      mockFetch.mockResolvedValueOnce(jsonResponse(data))

      const result = await api.knowledgeBases.listFiles('kb-1')
      expect(result).toEqual(data)
      expect(mockFetch.mock.calls[0][0]).toBe('/api/knowledge-bases/kb-1/files')
    })

    it('deleteFile sends DELETE /api/knowledge-bases/{kbId}/files/{fileId}', async () => {
      mockFetch.mockResolvedValueOnce(new Response(null, { status: 204 }))

      await api.knowledgeBases.deleteFile('kb-1', 'f-1')

      const [url, opts] = mockFetch.mock.calls[0]
      expect(url).toBe('/api/knowledge-bases/kb-1/files/f-1')
      expect(opts.method).toBe('DELETE')
    })
  })

  describe('schedules', () => {
    it('list fetches GET /api/schedules', async () => {
      const data = [{ id: 'sch-1', workflowName: 'Daily Report' }]
      mockFetch.mockResolvedValueOnce(jsonResponse(data))

      const result = await api.schedules.list()
      expect(result).toEqual(data)
      expect(mockFetch).toHaveBeenCalledWith('/api/schedules', expect.objectContaining({
        headers: expect.objectContaining({ 'Content-Type': 'application/json' }),
      }))
    })

    it('get fetches GET /api/schedules/{id}', async () => {
      const data = { id: 'sch-1', workflowName: 'Daily Report' }
      mockFetch.mockResolvedValueOnce(jsonResponse(data))

      const result = await api.schedules.get('sch-1')
      expect(result).toEqual(data)
      expect(mockFetch.mock.calls[0][0]).toBe('/api/schedules/sch-1')
    })

    it('create sends POST with schedule request body', async () => {
      const saved = { id: 'sch-new', workflowId: 'wf-1', cronExpression: '0 9 * * *' }
      mockFetch.mockResolvedValueOnce(jsonResponse(saved))

      await api.schedules.create({
        workflowId: 'wf-1',
        cronExpression: '0 9 * * *',
        timeZone: 'Asia/Taipei',
        defaultInput: 'hello',
      })

      const [url, opts] = mockFetch.mock.calls[0]
      expect(url).toBe('/api/schedules')
      expect(opts.method).toBe('POST')
      const body = JSON.parse(opts.body)
      expect(body.workflowId).toBe('wf-1')
      expect(body.cronExpression).toBe('0 9 * * *')
      expect(body.timeZone).toBe('Asia/Taipei')
      expect(body.defaultInput).toBe('hello')
    })

    it('create sends id when updating existing schedule', async () => {
      mockFetch.mockResolvedValueOnce(jsonResponse({ id: 'sch-1' }))

      await api.schedules.create({ id: 'sch-1', workflowId: 'wf-1', cronExpression: '0 * * * *' })

      const body = JSON.parse(mockFetch.mock.calls[0][1].body)
      expect(body.id).toBe('sch-1')
    })

    it('toggle sends PATCH /api/schedules/{id}/toggle', async () => {
      mockFetch.mockResolvedValueOnce(jsonResponse({ id: 'sch-1', enabled: false }))

      const result = await api.schedules.toggle('sch-1')
      expect(result.enabled).toBe(false)

      const [url, opts] = mockFetch.mock.calls[0]
      expect(url).toBe('/api/schedules/sch-1/toggle')
      expect(opts.method).toBe('PATCH')
    })

    it('delete sends DELETE /api/schedules/{id}', async () => {
      mockFetch.mockResolvedValueOnce(new Response(null, { status: 204 }))

      const result = await api.schedules.delete('sch-1')
      expect(result).toBeUndefined()

      const [url, opts] = mockFetch.mock.calls[0]
      expect(url).toBe('/api/schedules/sch-1')
      expect(opts.method).toBe('DELETE')
    })

    it('logs fetches GET /api/schedules/{id}/logs with default limit', async () => {
      const data = [{ id: 'slog-1', success: true, elapsedMs: 500 }]
      mockFetch.mockResolvedValueOnce(jsonResponse(data))

      const result = await api.schedules.logs('sch-1')
      expect(result).toEqual(data)
      expect(mockFetch.mock.calls[0][0]).toBe('/api/schedules/sch-1/logs?limit=20')
    })

    it('logs passes custom limit', async () => {
      mockFetch.mockResolvedValueOnce(jsonResponse([]))

      await api.schedules.logs('sch-1', 5)
      expect(mockFetch.mock.calls[0][0]).toBe('/api/schedules/sch-1/logs?limit=5')
    })
  })

  describe('knowledgeBases.uploadFile (FormData)', () => {
    it('sends FormData without overriding Content-Type', async () => {
      mockFetch.mockResolvedValueOnce(jsonResponse({ id: 'kb1' }))

      const file = new File(['content'], 'test.txt', { type: 'text/plain' })
      await api.knowledgeBases.uploadFile('kb1', file)

      const [url, opts] = mockFetch.mock.calls[0]
      expect(url).toBe('/api/knowledge-bases/kb1/files')
      expect(opts.body).toBeInstanceOf(FormData)
      // Content-Type should NOT be set (let browser set boundary)
      expect(opts.headers?.['Content-Type']).toBeUndefined()
    })
  })
})
