/**
 * export-package.ts 測試 — gen* 函式是 module-private，
 * 透過 mock JSZip + DOM 攔截 exportDeployPackage 的輸出來驗證。
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import type { Node, Edge } from '@xyflow/react'
import type { NodeData } from '@/types/workflow'

// Capture generated files
let capturedFiles: Record<string, string> = {}

// Mock JSZip
vi.mock('jszip', () => ({
  default: class MockJSZip {
    files: Record<string, string> = {}
    file(name: string, content: string) { this.files[name] = content; capturedFiles[name] = content }
    async generateAsync() { return new Blob(['mock']) }
  },
}))

// Mock stores
vi.mock('@/stores/workflow-store', () => ({
  useWorkflowStore: {
    getState: () => ({ workflowSettings: { type: 'auto', maxTurns: 10 } }),
  },
}))

vi.mock('@/stores/credential-store', () => ({
  useCredentialStore: {
    getState: () => ({
      credentials: {
        openai: { apiKey: 'sk-test', endpoint: '', model: 'gpt-4o' },
        'azure-openai': { apiKey: 'az-key', endpoint: 'https://my.openai.azure.com', model: 'gpt-4o' },
      },
    }),
  },
}))

vi.mock('../workflow-payload', () => ({
  toWorkflowPayloadJson: () => '{"nodes":[],"connections":[]}',
}))

// Mock DOM — only stub static methods, not the URL class itself
vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock')
vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {})
vi.spyOn(document, 'createElement').mockReturnValue({
  set href(_: string) {},
  set download(_: string) {},
  click: vi.fn(),
} as any)
vi.spyOn(document.body, 'appendChild').mockImplementation(() => null as any)
vi.spyOn(document.body, 'removeChild').mockImplementation(() => null as any)

// Import after mocks
const { exportDeployPackage } = await import('../export-package')

function agentNode(id: string, provider = 'openai', model = 'gpt-4o'): Node<NodeData> {
  return {
    id, type: 'agent', position: { x: 0, y: 0 },
    data: { type: 'agent', name: 'TestAgent', provider, model } as any,
  }
}

describe('exportDeployPackage', () => {
  beforeEach(() => {
    capturedFiles = {}
  })

  describe('project mode (default)', () => {
    beforeEach(async () => {
      await exportDeployPackage('MyApp', [agentNode('a1')], [])
    })

    it('generates all required files', () => {
      expect(Object.keys(capturedFiles)).toContain('Program.cs')
      expect(Object.keys(capturedFiles)).toContain('MyApp.csproj')
      expect(Object.keys(capturedFiles)).toContain('workflow.json')
      expect(Object.keys(capturedFiles)).toContain('appsettings.json')
      expect(Object.keys(capturedFiles)).toContain('Dockerfile')
      expect(Object.keys(capturedFiles)).toContain('README.md')
    })

    it('Program.cs contains Web API endpoints', () => {
      const cs = capturedFiles['Program.cs']
      expect(cs).toContain('MapPost("/chat"')
      expect(cs).toContain('MapPost("/chat/stream"')
      expect(cs).toContain('MapPost("/chat/upload"')
      expect(cs).toContain('MapGet("/health"')
      expect(cs).toContain('AddAgentCraftEngine')
    })

    it('Program.cs contains A2A/MCP endpoints', () => {
      const cs = capturedFiles['Program.cs']
      expect(cs).toContain('MapA2AEndpoints')
      expect(cs).toContain('MapMcpEndpoints')
    })

    it('.csproj uses Web SDK', () => {
      const csproj = capturedFiles['MyApp.csproj']
      expect(csproj).toContain('Microsoft.NET.Sdk.Web')
      expect(csproj).toContain('net10.0')
      expect(csproj).toContain('AgentCraftLab.Engine')
    })

    it('.csproj does NOT contain OutputType for web mode', () => {
      const csproj = capturedFiles['MyApp.csproj']
      expect(csproj).not.toContain('<OutputType>')
    })

    it('appsettings.json has provider credentials structure', () => {
      const config = JSON.parse(capturedFiles['appsettings.json'])
      expect(config.AgentCraft.Credentials.openai).toBeDefined()
      expect(config.AgentCraft.Credentials.openai.ApiKey).toBe('(set your key here)')
      expect(config.Logging).toBeDefined()
    })

    it('Dockerfile uses .NET 10 base images', () => {
      const df = capturedFiles['Dockerfile']
      expect(df).toContain('dotnet/sdk:10.0')
      expect(df).toContain('dotnet/aspnet:10.0')
      expect(df).toContain('EXPOSE 8080')
      expect(df).toContain('ENTRYPOINT')
    })

    it('README.md contains endpoint table', () => {
      const readme = capturedFiles['README.md']
      expect(readme).toContain('# MyApp')
      expect(readme).toContain('/chat')
      expect(readme).toContain('/health')
      expect(readme).toContain('docker')
    })
  })

  describe('console mode', () => {
    beforeEach(async () => {
      await exportDeployPackage('ConsoleApp', [agentNode('a1')], [], 'console')
    })

    it('Program.cs has REPL loop', () => {
      const cs = capturedFiles['Program.cs']
      expect(cs).toContain('Console.ReadLine')
      expect(cs).toContain('exit')
      expect(cs).not.toContain('MapPost')
    })

    it('.csproj uses plain SDK with OutputType', () => {
      const csproj = capturedFiles['ConsoleApp.csproj']
      expect(csproj).toContain('Microsoft.NET.Sdk')
      expect(csproj).toContain('<OutputType>Exe</OutputType>')
    })

    it('README.md is console-specific', () => {
      const readme = capturedFiles['README.md']
      expect(readme).toContain('Console')
      expect(readme).not.toContain('/chat')
    })
  })

  describe('name sanitization', () => {
    it('strips non-alphanumeric characters', async () => {
      await exportDeployPackage('My App!@#', [agentNode('a1')], [])
      // safeName = 'MyApp'
      expect(Object.keys(capturedFiles)).toContain('MyApp.csproj')
    })

    it('uses fallback name when empty', async () => {
      await exportDeployPackage('', [agentNode('a1')], [])
      expect(Object.keys(capturedFiles)).toContain('MyWorkflow.csproj')
    })
  })

  describe('provider detection', () => {
    it('detects providers from agent nodes', async () => {
      const nodes = [
        agentNode('a1', 'openai'),
        agentNode('a2', 'azure-openai'),
      ]
      await exportDeployPackage('Multi', nodes, [])

      const config = JSON.parse(capturedFiles['appsettings.json'])
      expect(config.AgentCraft.Credentials.openai).toBeDefined()
      expect(config.AgentCraft.Credentials['azure-openai']).toBeDefined()
    })

    it('defaults to openai when no provider found', async () => {
      const nodes: Node<NodeData>[] = [
        { id: 'c1', type: 'code', position: { x: 0, y: 0 }, data: { type: 'code', name: 'T' } as any },
      ]
      await exportDeployPackage('NoAgent', nodes, [])

      const config = JSON.parse(capturedFiles['appsettings.json'])
      expect(config.AgentCraft.Credentials.openai).toBeDefined()
    })
  })
})
