/**
 * AiBuildPanel 測試 — 驗證 AI Build 的核心邏輯：
 * 1. handleApply: AI spec JSON → React Flow nodes/edges 轉換
 * 2. handleSend: SSE 串流解析、history 建構
 * 3. getCredential: 找第一個有 key 的 provider
 * 4. UI 狀態：empty state、no-credential warning、streaming indicator
 */
import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react'
import { AiBuildPanel } from '../AiBuildPanel'

// ── Mocks ──

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key, i18n: { language: 'en' } }),
}))

vi.mock('@/lib/workflow-payload', () => ({
  toWorkflowPayloadJson: () => '{"nodes":[],"connections":[]}',
}))

// Controllable credential store
let mockCredentials: Record<string, { apiKey: string; endpoint: string; model: string }> = {}

vi.mock('@/stores/credential-store', () => ({
  useCredentialStore: Object.assign(
    (selector: any) => selector({ credentials: mockCredentials }),
    { getState: () => ({ credentials: mockCredentials }) },
  ),
}))

// Capture setWorkflow calls
const mockSetWorkflow = vi.fn()
vi.mock('@/stores/workflow-store', () => ({
  useWorkflowStore: Object.assign(
    (selector: any) => selector({
      workflowSettings: { type: 'auto', maxTurns: 10 },
    }),
    {
      getState: () => ({
        nodes: [],
        edges: [],
        workflowSettings: { type: 'auto', maxTurns: 10 },
        setWorkflow: mockSetWorkflow,
        layout: vi.fn().mockResolvedValue(undefined),
      }),
    },
  ),
}))

// Mock fetch for SSE
const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

// ── Helpers ──

function sseResponse(chunks: string[]) {
  let index = 0
  const encoder = new TextEncoder()
  const stream = new ReadableStream({
    pull(controller) {
      if (index < chunks.length) {
        controller.enqueue(encoder.encode(chunks[index++]))
      } else {
        controller.close()
      }
    },
  })
  return new Response(stream, { status: 200, headers: { 'Content-Type': 'text/event-stream' } })
}

// ── Tests ──

describe('AiBuildPanel', () => {
  beforeEach(() => {
    mockCredentials = {}
    mockSetWorkflow.mockClear()
    mockFetch.mockReset()
  })

  describe('empty state & credentials', () => {
    it('shows welcome message when no messages', () => {
      mockCredentials = { openai: { apiKey: 'sk-test', endpoint: '', model: 'gpt-4o' } }
      render(<AiBuildPanel />)
      expect(screen.getByText('aiBuild.welcome')).toBeInTheDocument()
    })

    it('shows example buttons', () => {
      mockCredentials = { openai: { apiKey: 'sk-test', endpoint: '', model: 'gpt-4o' } }
      render(<AiBuildPanel />)
      expect(screen.getByText('aiBuild.example1')).toBeInTheDocument()
      expect(screen.getByText('aiBuild.example2')).toBeInTheDocument()
      expect(screen.getByText('aiBuild.example3')).toBeInTheDocument()
    })

    it('clicking example fills input', () => {
      mockCredentials = { openai: { apiKey: 'sk-test', endpoint: '', model: 'gpt-4o' } }
      render(<AiBuildPanel />)
      fireEvent.click(screen.getByText('aiBuild.example1'))

      const input = screen.getByPlaceholderText('aiBuild.placeholder') as HTMLInputElement
      expect(input.value).toBe('aiBuild.example1')
    })

    it('shows no-credential warning when no API key', () => {
      render(<AiBuildPanel />)
      expect(screen.getByText('aiBuild.noCredential')).toBeInTheDocument()
    })

    it('hides warning when credential exists', () => {
      mockCredentials = { openai: { apiKey: 'sk-test', endpoint: '', model: '' } }
      render(<AiBuildPanel />)
      expect(screen.queryByText('aiBuild.noCredential')).not.toBeInTheDocument()
    })

    it('disables input when no credential', () => {
      render(<AiBuildPanel />)
      const input = screen.getByPlaceholderText('aiBuild.placeholder')
      expect(input).toBeDisabled()
    })

    it('enables input when credential exists', () => {
      mockCredentials = { openai: { apiKey: 'sk-test', endpoint: '', model: '' } }
      render(<AiBuildPanel />)
      const input = screen.getByPlaceholderText('aiBuild.placeholder')
      expect(input).not.toBeDisabled()
    })
  })

  describe('getCredential — picks first provider with key', () => {
    it('skips providers without apiKey', () => {
      mockCredentials = {
        empty: { apiKey: '', endpoint: '', model: '' },
        anthropic: { apiKey: 'sk-ant', endpoint: '', model: 'claude' },
      }
      render(<AiBuildPanel />)
      // No warning means a valid credential was found
      expect(screen.queryByText('aiBuild.noCredential')).not.toBeInTheDocument()
    })
  })

  describe('handleSend — SSE streaming', () => {
    beforeEach(() => {
      mockCredentials = { openai: { apiKey: 'sk-test', endpoint: '', model: 'gpt-4o' } }
    })

    it('sends correct request body', async () => {
      mockFetch.mockResolvedValueOnce(sseResponse(['data: "Hello"\n\n', 'data: [DONE]\n\n']))

      render(<AiBuildPanel />)
      const input = screen.getByPlaceholderText('aiBuild.placeholder')

      await act(async () => {
        fireEvent.change(input, { target: { value: 'Build a research pipeline' } })
        fireEvent.keyDown(input, { key: 'Enter' })
      })

      await waitFor(() => expect(mockFetch).toHaveBeenCalled())

      const [url, opts] = mockFetch.mock.calls[0]
      expect(url).toBe('/api/flow-builder')
      expect(opts.method).toBe('POST')

      const body = JSON.parse(opts.body)
      expect(body.message).toBe('Build a research pipeline')
      expect(body.provider).toBe('openai')
      expect(body.apiKey).toBeUndefined() // API key 不再從前端傳送，由後端 CredentialStore 讀取
      expect(body.model).toBe('gpt-4o')
      expect(body.currentPayload).toBeTruthy()
    })

    it('displays user message immediately', async () => {
      mockFetch.mockResolvedValueOnce(sseResponse(['data: "OK"\n\n', 'data: [DONE]\n\n']))

      render(<AiBuildPanel />)
      const input = screen.getByPlaceholderText('aiBuild.placeholder')

      await act(async () => {
        fireEvent.change(input, { target: { value: 'Hello' } })
        fireEvent.keyDown(input, { key: 'Enter' })
      })

      expect(screen.getByText('Hello')).toBeInTheDocument()
    })

    it('accumulates streamed text chunks', async () => {
      mockFetch.mockResolvedValueOnce(sseResponse([
        'data: "Part 1 "\n\n',
        'data: "Part 2"\n\n',
        'data: [DONE]\n\n',
      ]))

      render(<AiBuildPanel />)
      const input = screen.getByPlaceholderText('aiBuild.placeholder')

      await act(async () => {
        fireEvent.change(input, { target: { value: 'Test' } })
        fireEvent.keyDown(input, { key: 'Enter' })
      })

      await waitFor(() => {
        expect(screen.getByText('Part 1 Part 2')).toBeInTheDocument()
      })
    })

    it('extracts ```json block and shows apply button', async () => {
      // Build the SSE chunks that the component will accumulate into assistantText
      // The component regex: /```json\s*([\s\S]*?)```/
      const text = 'Here is your workflow:\n\n```json\n{"nodes":[{"type":"agent","name":"A"}],"connections":[]}\n```'
      mockFetch.mockResolvedValueOnce(sseResponse([
        `data: ${JSON.stringify(text)}\n\n`,
        'data: [DONE]\n\n',
      ]))

      render(<AiBuildPanel />)
      const input = screen.getByPlaceholderText('aiBuild.placeholder')

      await act(async () => {
        fireEvent.change(input, { target: { value: 'Make agents' } })
        fireEvent.keyDown(input, { key: 'Enter' })
      })

      await waitFor(() => {
        expect(screen.getByText('aiBuild.applyToCanvas')).toBeInTheDocument()
      })
    })

    it('does not send when input is empty', async () => {
      render(<AiBuildPanel />)

      const input = screen.getByPlaceholderText('aiBuild.placeholder')
      fireEvent.keyDown(input, { key: 'Enter' })

      expect(mockFetch).not.toHaveBeenCalled()
    })

    it('handles non-ok response without crashing', async () => {
      mockFetch.mockResolvedValueOnce(new Response(null, { status: 500, statusText: 'Internal Server Error' }))

      render(<AiBuildPanel />)
      const input = screen.getByPlaceholderText('aiBuild.placeholder')

      await act(async () => {
        fireEvent.change(input, { target: { value: 'Test' } })
        fireEvent.keyDown(input, { key: 'Enter' })
      })

      // After error, streaming should stop and user message is displayed
      await waitFor(() => {
        expect(screen.getByText('Test')).toBeInTheDocument()
        // Send button should be re-enabled (not streaming)
        expect(input).not.toBeDisabled()
      })
    })
  })

  describe('handleApply — JSON to React Flow conversion', () => {
    beforeEach(() => {
      mockCredentials = { openai: { apiKey: 'sk-test', endpoint: '', model: 'gpt-4o' } }
    })

    async function sendAndGetApplyButton(specJson: string) {
      const text = `Here:\n\`\`\`json\n${specJson}\n\`\`\``
      const chunk = `data: ${JSON.stringify(text)}\n\n`
      mockFetch.mockResolvedValueOnce(sseResponse([chunk, 'data: [DONE]\n\n']))

      render(<AiBuildPanel />)
      const input = screen.getByPlaceholderText('aiBuild.placeholder')

      await act(async () => {
        fireEvent.change(input, { target: { value: 'Build' } })
        fireEvent.keyDown(input, { key: 'Enter' })
      })

      await waitFor(() => {
        expect(screen.getByText('aiBuild.applyToCanvas')).toBeInTheDocument()
      })

      await act(async () => {
        fireEvent.click(screen.getByText('aiBuild.applyToCanvas'))
      })
    }

    it('creates Start + nodes + End with correct structure', async () => {
      const spec = JSON.stringify({
        nodes: [
          { type: 'agent', name: 'Researcher', data: { instructions: 'Research' } },
          { type: 'agent', name: 'Writer', data: { instructions: 'Write' } },
        ],
        connections: [{ from: 0, to: 1 }],
      })

      await sendAndGetApplyButton(spec)

      expect(mockSetWorkflow).toHaveBeenCalled()
      const [nodes, edges] = mockSetWorkflow.mock.calls[0]

      // Start + 2 agents + End = 4 nodes
      expect(nodes).toHaveLength(4)
      expect(nodes[0].type).toBe('start')
      expect(nodes[1].data.name).toBe('Researcher')
      expect(nodes[2].data.name).toBe('Writer')
      expect(nodes[3].type).toBe('end')

      // Start→Researcher, Researcher→Writer, Writer→End = 3 edges
      expect(edges).toHaveLength(3)
      expect(edges[0].source).toBe('start-1')
      expect(edges[0].target).toBe('agent-1')
      expect(edges[1].source).toBe('agent-1')
      expect(edges[1].target).toBe('agent-2')
      expect(edges[2].source).toBe('agent-2')
      expect(edges[2].target).toBe('end-1')
    })

    it('defaults to chain when no connections specified', async () => {
      const spec = JSON.stringify({
        nodes: [
          { type: 'agent', name: 'A' },
          { type: 'agent', name: 'B' },
          { type: 'agent', name: 'C' },
        ],
      })

      await sendAndGetApplyButton(spec)

      const [nodes, edges] = mockSetWorkflow.mock.calls[0]
      expect(nodes).toHaveLength(5) // Start + 3 + End

      // Default chain: Start→A→B→C→End
      expect(edges).toHaveLength(4)
      expect(edges[0].source).toBe('start-1')
      expect(edges[1].source).toBe('agent-1')
      expect(edges[1].target).toBe('agent-2')
      expect(edges[2].source).toBe('agent-2')
      expect(edges[2].target).toBe('agent-3')
      expect(edges[3].target).toBe('end-1')
    })

    it('preserves fromOutput in connections', async () => {
      const spec = JSON.stringify({
        nodes: [
          { type: 'condition', name: 'Check', data: { conditionType: 'contains', conditionExpression: 'OK' } },
          { type: 'agent', name: 'TrueAgent', data: { instructions: 'Handle true' } },
        ],
        connections: [{ from: 0, to: 1, fromOutput: 'output_1' }],
      })

      await sendAndGetApplyButton(spec)

      const [, edges] = mockSetWorkflow.mock.calls[0]
      const condEdge = edges.find((e: any) => e.source === 'condition-1')
      expect(condEdge.sourceHandle).toBe('output_1')
    })

    it('generates IDs when not provided in spec', async () => {
      const spec = JSON.stringify({
        nodes: [{ type: 'agent', name: 'NoId' }],
      })

      await sendAndGetApplyButton(spec)

      const [nodes] = mockSetWorkflow.mock.calls[0]
      expect(nodes[1].id).toBe('agent-1')
    })

    it('uses provided IDs from spec', async () => {
      const spec = JSON.stringify({
        nodes: [{ id: 'custom-id', type: 'agent', name: 'Custom' }],
      })

      await sendAndGetApplyButton(spec)

      const [nodes] = mockSetWorkflow.mock.calls[0]
      expect(nodes[1].id).toBe('custom-id')
    })

    it('flattens AI spec data fields into React Flow node data', async () => {
      // LLM 仍輸出舊 flat shape（`model: 'gpt-4o'` 字串），AiBuildPanel 會在
      // 邊界轉成新 nested Schema shape（`model: { provider, model: 'gpt-4o' }`）。
      const spec = JSON.stringify({
        nodes: [{
          type: 'agent', name: 'Expert',
          data: { instructions: 'Be thorough', tools: ['web_search'], model: 'gpt-4o' },
        }],
      })

      await sendAndGetApplyButton(spec)

      const [nodes] = mockSetWorkflow.mock.calls[0]
      const agent = nodes[1] // index 0 is start
      expect(agent.data.instructions).toBe('Be thorough')
      expect(agent.data.tools).toEqual(['web_search'])
      expect(agent.data.model.model).toBe('gpt-4o')
      expect(agent.data.name).toBe('Expert')
      expect(agent.data.type).toBe('agent')
      // Should NOT have nested data.data
      expect(agent.data.data).toBeUndefined()
    })

    it('handles flat format (Flow Planner output without data wrapper)', async () => {
      const spec = JSON.stringify({
        nodes: [{
          type: 'agent', name: 'Researcher',
          instructions: 'Search thoroughly', tools: ['web_search'], model: 'gpt-4o',
        }],
      })

      await sendAndGetApplyButton(spec)

      const [nodes] = mockSetWorkflow.mock.calls[0]
      const agent = nodes[1]
      expect(agent.data.instructions).toBe('Search thoroughly')
      expect(agent.data.tools).toEqual(['web_search'])
      expect(agent.data.model.model).toBe('gpt-4o')
      expect(agent.data.name).toBe('Researcher')
      // Flat fields should NOT leak type/name/id into data as duplicates
      expect(agent.data.data).toBeUndefined()
    })

    it('handles JSON with comments and trailing commas', async () => {
      const specWithComments = `{
        "nodes": [
          { "type": "agent", "name": "A", "data": { "instructions": "Hi" } }, // first agent
          { "type": "agent", "name": "B", "data": { "instructions": "Go" } }, // second agent
        ],
        "connections": [
          { "from": 0, "to": 1 }, // chain
        ]
      }`

      // Build SSE chunk with this JSON
      const text = 'Here:\n```json\n' + specWithComments + '\n```'
      mockFetch.mockResolvedValueOnce(sseResponse([
        `data: ${JSON.stringify(text)}\n\n`,
        'data: [DONE]\n\n',
      ]))

      render(<AiBuildPanel />)
      const input = screen.getByPlaceholderText('aiBuild.placeholder')

      await act(async () => {
        fireEvent.change(input, { target: { value: 'Build' } })
        fireEvent.keyDown(input, { key: 'Enter' })
      })

      await waitFor(() => {
        expect(screen.getByText('aiBuild.applyToCanvas')).toBeInTheDocument()
      })

      await act(async () => {
        fireEvent.click(screen.getByText('aiBuild.applyToCanvas'))
      })

      expect(mockSetWorkflow).toHaveBeenCalled()
      const [nodes] = mockSetWorkflow.mock.calls[0]
      expect(nodes).toHaveLength(4) // Start + A + B + End
    })

    it('shows applied state after apply', async () => {
      const spec = JSON.stringify({ nodes: [{ type: 'agent', name: 'A' }] })
      await sendAndGetApplyButton(spec)

      expect(screen.getByText('aiBuild.applied')).toBeInTheDocument()
    })
  })
})
