/**
 * Code Generation — 將 React Flow 畫布轉為 C# 程式碼。
 * 簡化版，涵蓋 single agent / sequential / concurrent / handoff / imperative 五種模式。
 */
import type { Node, Edge } from '@xyflow/react'
import type { NodeData, AgentNodeData } from '@/types/workflow'

export function generateCSharpCode(nodes: Node<NodeData>[], edges: Edge[]): string {
  const workNodes = nodes.filter((n) => n.type !== 'start' && n.type !== 'end')
  const agentNodes = workNodes.filter((n) => n.type === 'agent') as Node<AgentNodeData>[]
  const hasLogicNodes = workNodes.some((n) =>
    ['condition', 'loop', 'human', 'code', 'iteration', 'parallel', 'autonomous'].includes(n.type ?? ''))

  if (agentNodes.length === 0) return '// No agents defined. Add agent nodes to the canvas.'

  const providers = [...new Set(agentNodes.map((a) => a.data.provider || 'openai'))]
  const pattern = hasLogicNodes ? 'imperative'
    : agentNodes.length === 1 ? 'single'
    : detectPattern(agentNodes, edges)

  let code = ''

  // ── Using statements ──
  code += 'using Microsoft.Agents.AI;\n'
  code += 'using Microsoft.Extensions.AI;\n'
  for (const p of providers) code += providerUsings(p)
  if (pattern !== 'single') code += 'using Microsoft.Agents.AI.Workflows;\n'
  if (hasLogicNodes) code += '// For imperative mode, use WorkflowExecutionService from AgentCraftLab.Engine\n'
  code += '\n'

  // ── Provider setup ──
  code += '// ─── LLM Provider Setup ───\n'
  for (const p of providers) code += providerSetup(p, agentNodes)
  code += '\n'

  // ── Agent definitions ──
  code += '// ─── Agent Definitions ───\n'
  for (const agent of agentNodes) {
    const v = camel(agent.data.name)
    const d = agent.data
    const instr = esc(d.instructions || 'You are a helpful assistant.')
    const client = clientExpr(d.provider, d.model)
    const toolsStr = d.tools?.length ? `,\n    tools: [${d.tools.map((t) => camel(t)).join(', ')}]` : ''

    code += `ChatClientAgent ${v} = new(\n`
    code += `    ${client},\n`
    code += `    "${instr}",\n`
    code += `    "${d.name}"${toolsStr});\n\n`
  }

  // ── Execution ──
  if (pattern === 'single') {
    const v = camel(agentNodes[0].data.name)
    code += '// ─── Execute Single Agent ───\n'
    code += `var session = await ${v}.CreateSessionAsync();\n`
    code += `var response = await ${v}.RunAsync("Your message here", session);\n`
    code += 'Console.WriteLine(response);\n'
  } else if (pattern === 'sequential') {
    const ordered = topoSort(agentNodes, edges)
    const names = ordered.map((a) => camel(a.data.name))
    code += '// ─── Sequential Workflow ───\n'
    code += `Workflow workflow = AgentWorkflowBuilder.BuildSequential(\n`
    code += `    ${names.join(',\n    ')});\n\n`
    code += 'var session = await workflow.CreateSessionAsync();\n'
    code += 'var result = await workflow.RunAsync("Your message here", session);\n'
    code += 'Console.WriteLine(result);\n'
  } else if (pattern === 'concurrent') {
    const names = agentNodes.map((a) => camel(a.data.name))
    code += '// ─── Concurrent Workflow ───\n'
    code += `Workflow workflow = AgentWorkflowBuilder.BuildConcurrent(\n`
    code += `    ${names.join(',\n    ')});\n\n`
    code += 'var session = await workflow.CreateSessionAsync();\n'
    code += 'var result = await workflow.RunAsync("Your message here", session);\n'
    code += 'Console.WriteLine(result);\n'
  } else if (pattern === 'handoff') {
    const ordered = topoSort(agentNodes, edges)
    const first = camel(ordered[0].data.name)
    code += '// ─── Handoff Workflow ───\n'
    code += `var workflow = AgentWorkflowBuilder\n`
    code += `    .CreateHandoffBuilderWith(${first})\n`
    for (const agent of agentNodes) {
      const targets = edges
        .filter((e) => e.source === agent.id)
        .map((e) => agentNodes.find((n) => n.id === e.target))
        .filter(Boolean)
      if (targets.length > 0) {
        code += `    .WithHandoffs(${camel(agent.data.name)}, [${targets.map((t) => camel(t!.data.name)).join(', ')}])\n`
      }
    }
    code += '    .Build();\n\n'
    code += 'var session = await workflow.CreateSessionAsync();\n'
    code += 'var result = await workflow.RunAsync("Your message here", session);\n'
    code += 'Console.WriteLine(result);\n'
  } else {
    // imperative — 指向 Engine JSON 執行
    code += '// ─── Imperative Mode (JSON Workflow) ───\n'
    code += '// This workflow uses condition/loop/human/code nodes.\n'
    code += '// Use WorkflowExecutionService from AgentCraftLab.Engine:\n'
    code += '//\n'
    code += '// var engine = serviceProvider.GetRequiredService<WorkflowExecutionService>();\n'
    code += '// var request = new WorkflowExecutionRequest\n'
    code += '// {\n'
    code += '//     WorkflowJson = File.ReadAllText("workflow.json"),\n'
    code += '//     UserMessage = "Your message here",\n'
    code += '//     Credentials = credentials,\n'
    code += '// };\n'
    code += '// await foreach (var evt in engine.ExecuteAsync(request))\n'
    code += '// {\n'
    code += '//     Console.WriteLine($"[{evt.Type}] {evt.Text}");\n'
    code += '// }\n'
  }

  return code
}

// ── Helpers ──

function camel(name: string): string {
  return name.replace(/[^a-zA-Z0-9]/g, '_').replace(/^_+/, '').replace(/_+$/, '') || 'agent'
}

function esc(s: string): string {
  return s.replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\n/g, '\\n')
}

function providerUsings(p: string): string {
  switch (p) {
    case 'openai': return 'using OpenAI;\n'
    case 'azure-openai': return 'using Azure.AI.OpenAI;\nusing Azure;\n'
    case 'anthropic': return '// Anthropic: use Microsoft.Extensions.AI.Anthropic\n'
    case 'ollama': return '// Ollama: use Microsoft.Extensions.AI.Ollama\n'
    default: return ''
  }
}

function providerSetup(p: string, agents: Node<AgentNodeData>[]): string {
  const agent = agents.find((a) => a.data.provider === p)
  const model = agent?.data.model || 'gpt-4o'
  switch (p) {
    case 'openai':
      return `var openAiClient = new OpenAIClient(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));\n`
    case 'azure-openai':
      return (
        `var azureClient = new AzureOpenAIClient(\n` +
        `    new Uri(Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")!),\n` +
        `    new AzureKeyCredential(Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")!));\n`
      )
    case 'ollama':
      return `// Ollama: var ollamaClient = new OllamaChatClient("http://localhost:11434", "${model}");\n`
    default:
      return `// ${p}: configure your LLM client here\n`
  }
}

function clientExpr(provider: string, model: string): string {
  switch (provider) {
    case 'openai': return `openAiClient.GetChatClient("${model}").AsIChatClient()`
    case 'azure-openai': return `azureClient.GetChatClient("${model}").AsIChatClient()`
    case 'ollama': return `ollamaClient`
    default: return `/* ${provider} client */`
  }
}

function detectPattern(agents: Node<AgentNodeData>[], edges: Edge[]): string {
  const hasMultiOut = agents.some((a) => edges.filter((e) => e.source === a.id).length > 1)
  if (hasMultiOut) return 'handoff'
  // Check if all agents form a chain
  const sources = new Set(edges.map((e) => e.source))
  const targets = new Set(edges.map((e) => e.target))
  const roots = agents.filter((a) => !targets.has(a.id))
  if (roots.length === 1) return 'sequential'
  return 'concurrent'
}

function topoSort(agents: Node<AgentNodeData>[], edges: Edge[]): Node<AgentNodeData>[] {
  const adj = new Map<string, string[]>()
  const inDeg = new Map<string, number>()
  for (const a of agents) { adj.set(a.id, []); inDeg.set(a.id, 0) }
  for (const e of edges) {
    if (adj.has(e.source) && inDeg.has(e.target)) {
      adj.get(e.source)!.push(e.target)
      inDeg.set(e.target, (inDeg.get(e.target) ?? 0) + 1)
    }
  }
  const queue = agents.filter((a) => (inDeg.get(a.id) ?? 0) === 0)
  const result: Node<AgentNodeData>[] = []
  while (queue.length) {
    const node = queue.shift()!
    result.push(node)
    for (const next of adj.get(node.id) ?? []) {
      inDeg.set(next, (inDeg.get(next) ?? 0) - 1)
      if ((inDeg.get(next) ?? 0) === 0) {
        const n = agents.find((a) => a.id === next)
        if (n) queue.push(n)
      }
    }
  }
  // Add any not reached
  for (const a of agents) {
    if (!result.includes(a)) result.push(a)
  }
  return result
}
