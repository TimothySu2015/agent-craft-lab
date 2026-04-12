/**
 * Node Registry — 單一真相來源，定義所有節點類型的 metadata 和預設資料。
 * 對應 studio-nodes.js 的 NODE_REGISTRY。
 */
import type { LucideIcon } from 'lucide-react'
import {
  Bot, Database, GitBranch, RefreshCw, Route,
  Globe, User, Code, Repeat, Columns3, Globe2, Brain,
  Play, Square,
} from 'lucide-react'
import type { NodeType, NodeData } from '@/types/workflow'

export interface NodeTypeConfig {
  type: NodeType;
  labelKey: string;
  icon: LucideIcon;
  color: string;
  inputs: number;
  outputs: number;
  defaultData: (name: string) => NodeData;
}

export const NODE_REGISTRY: Record<NodeType, NodeTypeConfig> = {
  agent: {
    type: 'agent',
    labelKey: 'node.agent',
    icon: Bot,
    color: 'blue',
    inputs: 1,
    outputs: 1,
    defaultData: (name) => ({
      type: 'agent',
      name,
      instructions: '',
      model: { provider: 'openai', model: 'gpt-4o' },
      tools: [],
      mcpServers: [],
      a2AAgents: [],
      httpApis: [],
      skills: [],
      output: { kind: 'text' },
      history: { provider: 'none', maxMessages: 20 },
      middleware: [],
    }),
  },
  rag: {
    type: 'rag',
    labelKey: 'node.rag',
    icon: Database,
    color: 'violet',
    inputs: 1,
    outputs: 1,
    defaultData: (name) => ({
      type: 'rag',
      name,
      rag: {
        dataSource: 'knowledge-base',
        chunkSize: 512,
        chunkOverlap: 50,
        topK: 5,
        embeddingModel: 'text-embedding-3-small',
        searchMode: 'hybrid',
        minScore: 0.005,
        queryExpansion: true,
        contextCompression: false,
        tokenBudget: 1500,
      },
      knowledgeBaseIds: [],
    }),
  },
  condition: {
    type: 'condition',
    labelKey: 'node.condition',
    icon: GitBranch,
    color: 'amber',
    inputs: 1,
    outputs: 2,
    defaultData: (name) => ({
      type: 'condition',
      name,
      condition: { kind: 'contains', value: '' },
    }),
  },
  loop: {
    type: 'loop',
    labelKey: 'node.loop',
    icon: RefreshCw,
    color: 'amber',
    inputs: 1,
    outputs: 2,
    defaultData: (name) => ({
      type: 'loop',
      name,
      condition: { kind: 'contains', value: '' },
      maxIterations: 5,
    }),
  },
  router: {
    type: 'router',
    labelKey: 'node.router',
    icon: Route,
    color: 'amber',
    inputs: 1,
    outputs: 3,
    defaultData: (name) => ({
      type: 'router',
      name,
      routes: [
        { name: 'A', keywords: [], isDefault: false },
        { name: 'B', keywords: [], isDefault: false },
        { name: 'default', keywords: [], isDefault: true },
      ],
    }),
  },
  'a2a-agent': {
    type: 'a2a-agent',
    labelKey: 'node.a2a',
    icon: Globe,
    color: 'purple',
    inputs: 1,
    outputs: 1,
    defaultData: (name) => ({
      type: 'a2a-agent',
      name,
      instructions: '',
      url: '',
      format: 'auto',
    }),
  },
  human: {
    type: 'human',
    labelKey: 'node.human',
    icon: User,
    color: 'pink',
    inputs: 1,
    outputs: 2,
    defaultData: (name) => ({
      type: 'human',
      name,
      prompt: '',
      kind: 'text',
      timeoutSeconds: 0,
    }),
  },
  code: {
    type: 'code',
    labelKey: 'node.code',
    icon: Code,
    color: 'teal',
    inputs: 1,
    outputs: 1,
    defaultData: (name) => ({
      type: 'code',
      name,
      kind: 'template',
      expression: '{{input}}',
      delimiter: '\n',
      splitIndex: 0,
      maxLength: 0,
    }),
  },
  iteration: {
    type: 'iteration',
    labelKey: 'node.iteration',
    icon: Repeat,
    color: 'teal',
    inputs: 1,
    outputs: 2,
    defaultData: (name) => ({
      type: 'iteration',
      name,
      split: 'jsonArray',
      delimiter: '\n',
      maxItems: 50,
      maxConcurrency: 1,
    }),
  },
  parallel: {
    type: 'parallel',
    labelKey: 'node.parallel',
    icon: Columns3,
    color: 'cyan',
    inputs: 1,
    outputs: 3,
    defaultData: (name) => ({
      type: 'parallel',
      name,
      branches: [
        { name: 'Branch1', goal: '' },
        { name: 'Branch2', goal: '' },
      ],
      merge: 'labeled',
    }),
  },
  'http-request': {
    type: 'http-request',
    labelKey: 'node.http',
    icon: Globe2,
    color: 'orange',
    inputs: 1,
    outputs: 1,
    defaultData: (name) => ({
      type: 'http-request',
      name,
      spec: {
        kind: 'inline',
        url: '',
        method: 'get',
        headers: [],
        contentType: 'application/json',
        auth: { kind: 'none' },
        retry: { count: 0, delayMs: 1000 },
        timeoutSeconds: 15,
        response: { kind: 'text' },
        responseMaxLength: 2000,
      },
    }),
  },
  autonomous: {
    type: 'autonomous',
    labelKey: 'node.autonomous',
    icon: Brain,
    color: 'green',
    inputs: 1,
    outputs: 1,
    defaultData: (name) => ({
      type: 'autonomous',
      name,
      instructions: '',
      model: { provider: 'openai', model: 'gpt-4o', maxOutputTokens: 200000 },
      maxIterations: 25,
      tools: [],
      mcpServers: [],
      a2AAgents: [],
      httpApis: [],
      skills: [],
    }),
  },
  start: {
    type: 'start',
    labelKey: 'node.start',
    icon: Play,
    color: 'green',
    inputs: 0,
    outputs: 1,
    defaultData: (name) => ({ type: 'start', name }),
  },
  end: {
    type: 'end',
    labelKey: 'node.end',
    icon: Square,
    color: 'red',
    inputs: 1,
    outputs: 0,
    defaultData: (name) => ({ type: 'end', name }),
  },
}

/** Node color mapping → Tailwind classes */
export const NODE_COLORS: Record<string, { border: string; iconBg: string; iconText: string }> = {
  blue:   { border: 'border-blue-500/30',   iconBg: 'bg-blue-500/15',   iconText: 'text-blue-400' },
  yellow: { border: 'border-yellow-500/30', iconBg: 'bg-yellow-500/15', iconText: 'text-yellow-400' },
  violet: { border: 'border-violet-500/30', iconBg: 'bg-violet-500/15', iconText: 'text-violet-400' },
  amber:  { border: 'border-amber-500/30',  iconBg: 'bg-amber-500/15',  iconText: 'text-amber-400' },
  purple: { border: 'border-purple-500/30', iconBg: 'bg-purple-500/15', iconText: 'text-purple-400' },
  pink:   { border: 'border-pink-500/30',   iconBg: 'bg-pink-500/15',   iconText: 'text-pink-400' },
  teal:   { border: 'border-teal-500/30',   iconBg: 'bg-teal-500/15',   iconText: 'text-teal-400' },
  cyan:   { border: 'border-cyan-500/30',   iconBg: 'bg-cyan-500/15',   iconText: 'text-cyan-400' },
  green:  { border: 'border-green-500/30',  iconBg: 'bg-green-500/15',  iconText: 'text-green-400' },
  orange: { border: 'border-orange-500/30', iconBg: 'bg-orange-500/15', iconText: 'text-orange-400' },
  red:    { border: 'border-red-500/30',    iconBg: 'bg-red-500/15',    iconText: 'text-red-400' },
}
