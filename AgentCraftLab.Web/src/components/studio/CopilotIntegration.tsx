/**
 * CopilotKit 整合層 — useCopilotReadable + Actions。
 * Generative UI actions 由 copilotActionsEnabled toggle 控制。
 * 放在 CopilotKit Provider 內部。
 */
import { useCopilotReadable, useCopilotAction } from '@copilotkit/react-core'
import { useWorkflowStore } from '@/stores/workflow-store'
import { useCredentialStore } from '@/stores/credential-store'
import { NODE_REGISTRY } from './nodes/registry'
import { api } from '@/lib/api'
import type { NodeType, NodeData } from '@/types/workflow'

// ─── Generative UI 元件（渲染在 Chat 中） ───

function ToolListCard({ tools }: { tools: { id: string; name: string; description: string }[] }) {
  return (
    <div className="rounded-md border border-border bg-card/50 text-xs overflow-hidden my-1">
      <div className="px-2 py-1.5 border-b border-border bg-muted/30 font-semibold text-foreground">
        Available Tools ({tools.length})
      </div>
      <div className="max-h-[200px] overflow-y-auto">
        {tools.map((t) => (
          <div key={t.id} className="flex items-start gap-2 px-2 py-1 border-b border-border/50 last:border-0">
            <span className="font-mono text-blue-400 shrink-0">{t.id}</span>
            <span className="text-muted-foreground">{t.description}</span>
          </div>
        ))}
      </div>
    </div>
  )
}

function DiscoveryCard({ title, status, items }: { title: string; status: 'ok' | 'error'; items?: { name: string; description?: string }[] }) {
  return (
    <div className="rounded-md border border-border bg-card/50 text-xs overflow-hidden my-1">
      <div className="flex items-center gap-1.5 px-2 py-1.5 border-b border-border bg-muted/30">
        <span className={status === 'ok' ? 'text-green-400' : 'text-red-400'}>
          {status === 'ok' ? '\u2713' : '\u2717'}
        </span>
        <span className="font-semibold text-foreground">{title}</span>
      </div>
      {items && items.length > 0 && (
        <div className="max-h-[150px] overflow-y-auto">
          {items.map((item, i) => (
            <div key={i} className="flex items-start gap-2 px-2 py-1 border-b border-border/50 last:border-0">
              <span className="font-mono text-blue-400 shrink-0">{item.name}</span>
              {item.description && <span className="text-muted-foreground">{item.description}</span>}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

// ─── 基礎：Readable + Canvas Actions（永遠啟用） ───

export function CopilotReadable() {
  const nodes = useWorkflowStore((s) => s.nodes)
  const edges = useWorkflowStore((s) => s.edges)
  const addNode = useWorkflowStore((s) => s.addNode)
  const updateNodeData = useWorkflowStore((s) => s.updateNodeData)
  const actionsEnabled = useCredentialStore((s) => s.copilotActionsEnabled)

  useCopilotReadable({
    description: 'Current workflow canvas state: nodes and connections',
    value: {
      nodeCount: nodes.length,
      nodes: nodes.map((n) => ({ id: n.id, type: n.type, name: (n.data as NodeData).name })),
      connections: edges.map((e) => ({ from: e.source, to: e.target })),
    },
  })

  useCopilotAction({
    name: 'addNodeToCanvas',
    description: 'Add a new node to the workflow canvas',
    parameters: [
      { name: 'type', type: 'string', description: 'Node type: agent, tool, rag, condition, loop, router, a2a-agent, human, code, iteration, parallel, http-request, autonomous', required: true },
      { name: 'x', type: 'number', description: 'X position (default 400)', required: false },
      { name: 'y', type: 'number', description: 'Y position (default 200)', required: false },
    ],
    handler: async ({ type, x, y }) => {
      const nodeType = type as NodeType
      if (!NODE_REGISTRY[nodeType]) return `Unknown node type: ${type}`
      addNode(nodeType, { x: x ?? 400, y: y ?? 200 })
      return `Added ${type} node to canvas`
    },
  })

  useCopilotAction({
    name: 'updateNodeData',
    description: 'Update properties of an existing node',
    parameters: [
      { name: 'nodeId', type: 'string', required: true },
      { name: 'name', type: 'string', required: false },
      { name: 'instructions', type: 'string', required: false },
      { name: 'model', type: 'string', required: false },
      { name: 'provider', type: 'string', required: false },
      { name: 'tools', type: 'string', description: 'Comma-separated tool IDs', required: false },
    ],
    handler: async ({ nodeId, name, instructions, model, provider, tools }) => {
      const updates: Record<string, unknown> = {}
      if (name) updates.name = name
      if (instructions) updates.instructions = instructions
      if (model) updates.model = model
      if (provider) updates.provider = provider
      if (tools) updates.tools = tools.split(',').map((t: string) => t.trim()).filter(Boolean)
      updateNodeData(nodeId, updates as Partial<NodeData>)
      return `Updated node ${nodeId}`
    },
  })

  return actionsEnabled ? <CopilotGenerativeActions /> : null
}

// ─── Generative UI Actions（toggle 控制） ───

function CopilotGenerativeActions() {
  useCopilotAction({
    name: 'listAvailableTools',
    description: 'List all available built-in tools that can be assigned to agent nodes',
    parameters: [],
    handler: async () => {
      try {
        const tools = await api.tools.list()
        return tools.map((t) => `${t.id}: ${t.name} — ${t.description}`).join('\n')
      } catch {
        return 'Failed to load tools. Is the backend running?'
      }
    },
    render: ({ status, result }: { status: string; result?: unknown }) => {
      if (status === 'executing') {
        return <div className="text-xs text-muted-foreground animate-pulse my-1">Loading tools...</div>
      }
      if (status === 'complete' && result) {
        const lines = (result as string).split('\n').filter(Boolean)
        const tools = lines.map((line) => {
          const [id, rest] = line.split(': ', 2)
          const [name, description] = (rest ?? '').split(' — ', 2)
          return { id: id ?? '', name: name ?? '', description: description ?? '' }
        })
        return <ToolListCard tools={tools} />
      }
      return <></>
    },
  })

  useCopilotAction({
    name: 'testMcpServer',
    description: 'Test connection to an MCP server and discover its tools',
    parameters: [
      { name: 'url', type: 'string', description: 'MCP server URL (e.g. http://localhost:3001/mcp)', required: true },
    ],
    handler: async ({ url }) => {
      try {
        const data = await api.mcp.discover(url)
        if (data.healthy) {
          return JSON.stringify({ status: 'ok', tools: data.tools ?? [] })
        }
        return JSON.stringify({ status: 'error', error: data.error })
      } catch (err) {
        return JSON.stringify({ status: 'error', error: String(err) })
      }
    },
    render: ({ status, args, result }: { status: string; args?: any; result?: unknown }) => {
      if (status === 'executing') {
        return <div className="text-xs text-muted-foreground animate-pulse my-1">Connecting to {args.url}...</div>
      }
      if (status === 'complete' && result) {
        try {
          const data = JSON.parse(result as string)
          return (
            <DiscoveryCard
              title={`MCP: ${args.url}`}
              status={data.status}
              items={data.tools?.map((t: { name: string; description?: string }) => ({ name: t.name, description: t.description }))}
            />
          )
        } catch { return <></> }
      }
      return <></>
    },
  })

  useCopilotAction({
    name: 'testA2AAgent',
    description: 'Discover and test an A2A agent endpoint',
    parameters: [
      { name: 'url', type: 'string', description: 'A2A agent base URL', required: true },
      { name: 'format', type: 'string', description: 'Format: auto, google, microsoft (default auto)', required: false },
    ],
    handler: async ({ url, format }) => {
      try {
        const data = await api.a2a.discover(url, format ?? 'auto')
        if (data.healthy) {
          return JSON.stringify({ status: 'ok', agent: data.agent })
        }
        return JSON.stringify({ status: 'error', error: data.error })
      } catch (err) {
        return JSON.stringify({ status: 'error', error: String(err) })
      }
    },
    render: ({ status, args, result }: { status: string; args?: any; result?: unknown }) => {
      if (status === 'executing') {
        return <div className="text-xs text-muted-foreground animate-pulse my-1">Discovering A2A agent at {args.url}...</div>
      }
      if (status === 'complete' && result) {
        try {
          const data = JSON.parse(result as string)
          return (
            <DiscoveryCard
              title={`A2A: ${args.url}`}
              status={data.status}
              items={data.agent ? [{ name: data.agent.name, description: data.agent.description }] : undefined}
            />
          )
        } catch { return <></> }
      }
      return <></>
    },
  })

  useCopilotAction({
    name: 'testHttpApi',
    description: 'Test a custom HTTP API endpoint',
    parameters: [
      { name: 'url', type: 'string', required: true },
      { name: 'method', type: 'string', description: 'HTTP method (GET/POST/PUT/DELETE)', required: false },
      { name: 'body', type: 'string', description: 'Request body JSON', required: false },
      { name: 'input', type: 'string', description: 'Input text to send', required: false },
    ],
    handler: async ({ url, method, body, input }) => {
      try {
        const data = await api.httpTools.test({ url, method: method ?? 'GET', body, input })
        if (data.success) {
          const preview = data.response?.substring(0, 500) ?? ''
          return `Success! Response: ${preview}`
        }
        return `Failed: ${data.error}`
      } catch (err) {
        return `Error: ${err}`
      }
    },
  })

  return null
}
