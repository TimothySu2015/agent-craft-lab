/**
 * Node 元件渲染測試 — 驗證各節點的文字輸出、動態 output 計算、條件渲染。
 * 使用 React Testing Library 渲染元件，mock @xyflow/react Handle。
 */
import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'

// Mock @xyflow/react — Handle 和 Position 需要 mock 才能脫離 ReactFlowProvider
vi.mock('@xyflow/react', async () => {
  const actual = await vi.importActual('@xyflow/react')
  return {
    ...actual,
    Handle: ({ id, type, position, style }: any) => (
      <div data-testid={`handle-${type}-${id ?? 'default'}`} data-position={position} style={style} />
    ),
    Position: { Left: 'left', Right: 'right', Top: 'top', Bottom: 'bottom' },
    useNodeId: () => 'test-node',
    useUpdateNodeInternals: () => vi.fn(),
  }
})

// Mock coagent store
vi.mock('@/stores/coagent-store', () => ({
  useCoAgentStore: (selector: any) => selector({ state: { nodeStates: {} } }),
}))

// Mock workflow store
vi.mock('@/stores/workflow-store', () => ({
  useWorkflowStore: (selector: any) => selector({ layoutDirection: 'LR' }),
}))

// Import node components after mocks
import { AgentNode } from '../AgentNode'
import { ConditionNode } from '../ConditionNode'
import { LoopNode } from '../LoopNode'
import { CodeNode } from '../CodeNode'
import { ParallelNode } from '../ParallelNode'
import { RouterNode } from '../RouterNode'
import { IterationNode } from '../IterationNode'
import { AutonomousNode } from '../AutonomousNode'
import { RagNode } from '../RagNode'
import { HttpRequestNode } from '../HttpRequestNode'

// ── AgentNode ──

describe('AgentNode', () => {
  const baseData = {
    type: 'agent' as const, name: 'MyAgent', instructions: 'Be helpful',
    model: 'gpt-4o', provider: 'openai', endpoint: '', deploymentName: '',
    historyProvider: 'none', maxMessages: 20, middleware: '', tools: [] as string[], skills: [],
  }

  it('renders name and provider/model subtitle', () => {
    render(<AgentNode data={baseData} selected={false} id="a1" type="agent" />)
    expect(screen.getByText('MyAgent')).toBeInTheDocument()
    expect(screen.getByText('openai / gpt-4o')).toBeInTheDocument()
  })

  it('renders instructions', () => {
    render(<AgentNode data={baseData} selected={false} id="a1" type="agent" />)
    expect(screen.getByText('Be helpful')).toBeInTheDocument()
  })

  it('hides instructions when empty', () => {
    const data = { ...baseData, instructions: '' }
    render(<AgentNode data={data} selected={false} id="a1" type="agent" />)
    expect(screen.queryByText('Be helpful')).not.toBeInTheDocument()
  })

  it('renders tool badges', () => {
    const data = { ...baseData, tools: ['web_search', 'calculator'] }
    render(<AgentNode data={data} selected={false} id="a1" type="agent" />)
    expect(screen.getByText('web_search')).toBeInTheDocument()
    expect(screen.getByText('calculator')).toBeInTheDocument()
  })

  it('hides tool badges when empty', () => {
    render(<AgentNode data={baseData} selected={false} id="a1" type="agent" />)
    expect(screen.queryByText('web_search')).not.toBeInTheDocument()
  })
})

// ── ConditionNode ──

describe('ConditionNode', () => {
  const data = {
    type: 'condition' as const, name: 'Check', conditionType: 'contains',
    conditionExpression: 'DONE', maxIterations: 5,
  }

  it('renders name and conditionType subtitle', () => {
    render(<ConditionNode data={data} selected={false} id="c1" type="condition" />)
    expect(screen.getByText('Check')).toBeInTheDocument()
    expect(screen.getByText('contains')).toBeInTheDocument()
  })

  it('renders True/False labels', () => {
    render(<ConditionNode data={data} selected={false} id="c1" type="condition" />)
    expect(screen.getByText('True')).toBeInTheDocument()
    expect(screen.getByText('False')).toBeInTheDocument()
  })

  it('renders condition expression', () => {
    render(<ConditionNode data={data} selected={false} id="c1" type="condition" />)
    expect(screen.getByText('DONE')).toBeInTheDocument()
  })
})

// ── LoopNode ──

describe('LoopNode', () => {
  const data = {
    type: 'loop' as const, name: 'Retry', conditionType: 'contains',
    conditionExpression: 'OK', maxIterations: 3,
  }

  it('renders subtitle with conditionType and max iterations', () => {
    render(<LoopNode data={data} selected={false} id="l1" type="loop" />)
    expect(screen.getByText('contains (max 3)')).toBeInTheDocument()
  })
})

// ── CodeNode ──

describe('CodeNode', () => {
  it('renders template when not default', () => {
    const data = {
      type: 'code' as const, name: 'Format', transformType: 'template',
      pattern: '', replacement: '', template: '## {{input}}',
      maxLength: 0, delimiter: '\\n', splitIndex: 0,
    }
    render(<CodeNode data={data} selected={false} id="code1" type="code" />)
    expect(screen.getByText('## {{input}}')).toBeInTheDocument()
  })

  it('hides default template {{input}}', () => {
    const data = {
      type: 'code' as const, name: 'Format', transformType: 'template',
      pattern: '', replacement: '', template: '{{input}}',
      maxLength: 0, delimiter: '\\n', splitIndex: 0,
    }
    render(<CodeNode data={data} selected={false} id="code1" type="code" />)
    expect(screen.queryByText('{{input}}')).not.toBeInTheDocument()
  })
})

// ── ParallelNode ──

describe('ParallelNode', () => {
  it('computes output count from branches + 1 Done', () => {
    const data = {
      type: 'parallel' as const, name: 'Fan', branches: 'A,B,C', mergeStrategy: 'labeled',
    }
    const { container } = render(<ParallelNode data={data} selected={false} id="p1" type="parallel" />)
    // 3 branches + 1 Done = 4 output handles
    const sourceHandles = container.querySelectorAll('[data-testid^="handle-source-output_"]')
    expect(sourceHandles).toHaveLength(4)
  })

  it('handles empty branches', () => {
    const data = {
      type: 'parallel' as const, name: 'Fan', branches: '', mergeStrategy: 'labeled',
    }
    const { container } = render(<ParallelNode data={data} selected={false} id="p1" type="parallel" />)
    // 0 branches + 1 Done = 1 output
    const sourceHandles = container.querySelectorAll('[data-testid^="handle-source"]')
    expect(sourceHandles).toHaveLength(1)
  })

  it('renders merge strategy as subtitle', () => {
    const data = {
      type: 'parallel' as const, name: 'Fan', branches: 'A,B', mergeStrategy: 'json',
    }
    render(<ParallelNode data={data} selected={false} id="p1" type="parallel" />)
    expect(screen.getByText('json')).toBeInTheDocument()
  })
})

// ── RouterNode ──

describe('RouterNode', () => {
  it('computes output count from routes', () => {
    const data = {
      type: 'router' as const, name: 'Route', conditionExpression: '', routes: 'billing,technical,general',
    }
    const { container } = render(<RouterNode data={data} selected={false} id="r1" type="router" />)
    // 3 routes = 3 outputs
    const sourceHandles = container.querySelectorAll('[data-testid^="handle-source-output_"]')
    expect(sourceHandles).toHaveLength(3)
  })

  it('has minimum 2 outputs even for single route', () => {
    const data = {
      type: 'router' as const, name: 'Route', conditionExpression: '', routes: 'only',
    }
    const { container } = render(<RouterNode data={data} selected={false} id="r1" type="router" />)
    const sourceHandles = container.querySelectorAll('[data-testid^="handle-source-output_"]')
    expect(sourceHandles).toHaveLength(2)
  })
})

// ── IterationNode ──

describe('IterationNode', () => {
  it('renders max items', () => {
    const data = {
      type: 'iteration' as const, name: 'ForEach', splitMode: 'json-array',
      iterationDelimiter: '\\n', maxItems: 50,
    }
    render(<IterationNode data={data} selected={false} id="i1" type="iteration" />)
    expect(screen.getByText('Max: 50')).toBeInTheDocument()
  })
})

// ── AutonomousNode ──

describe('AutonomousNode', () => {
  it('renders provider/model subtitle', () => {
    const data = {
      type: 'autonomous' as const, name: 'Auto', instructions: 'Research',
      model: 'gpt-4o', provider: 'openai', maxIterations: 25,
      maxOutputTokens: 200000, tools: [], skills: [], mcpServers: [], a2AAgents: [],
    }
    render(<AutonomousNode data={data} selected={false} id="au1" type="autonomous" />)
    expect(screen.getByText('openai / gpt-4o')).toBeInTheDocument()
    expect(screen.getByText('Research')).toBeInTheDocument()
  })
})

// ── RagNode ──

describe('RagNode', () => {
  it('renders topK and chunkSize', () => {
    const data = {
      type: 'rag' as const, name: 'RAG', ragDataSource: 'upload',
      ragChunkSize: 512, ragChunkOverlap: 50, ragTopK: 5,
      ragEmbeddingModel: 'text-embedding-3-small', knowledgeBaseIds: [],
    }
    render(<RagNode data={data} selected={false} id="rag1" type="rag" />)
    expect(screen.getByText('TopK: 5 / Chunk: 512')).toBeInTheDocument()
  })
})

// ── HttpRequestNode ──

describe('HttpRequestNode', () => {
  it('renders API ID when set', () => {
    const data = {
      type: 'http-request' as const, name: 'API', httpApiId: 'weather-api', httpArgsTemplate: '{}',
    }
    render(<HttpRequestNode data={data} selected={false} id="h1" type="http-request" />)
    expect(screen.getByText('weather-api')).toBeInTheDocument()
  })

  it('hides API ID when empty', () => {
    const data = {
      type: 'http-request' as const, name: 'API', httpApiId: '', httpArgsTemplate: '{}',
    }
    render(<HttpRequestNode data={data} selected={false} id="h1" type="http-request" />)
    expect(screen.queryByText('weather-api')).not.toBeInTheDocument()
  })
})
