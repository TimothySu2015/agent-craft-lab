/**
 * Node type registration — maps NodeType → React component for React Flow.
 */
import { AgentNode } from './AgentNode'

import { RagNode } from './RagNode'
import { ConditionNode } from './ConditionNode'
import { LoopNode } from './LoopNode'
import { RouterNode } from './RouterNode'
import { A2ANode } from './A2ANode'
import { HumanNode } from './HumanNode'
import { CodeNode } from './CodeNode'
import { IterationNode } from './IterationNode'
import { ParallelNode } from './ParallelNode'
import { HttpRequestNode } from './HttpRequestNode'
import { AutonomousNode } from './AutonomousNode'
import { StartNode } from './StartNode'
import { EndNode } from './EndNode'
import { ConditionGroupNode } from './groups/ConditionGroupNode'
import { LoopGroupNode } from './groups/LoopGroupNode'
import { ParallelGroupNode } from './groups/ParallelGroupNode'
import { IterationGroupNode } from './groups/IterationGroupNode'
import type { NodeTypes } from '@xyflow/react'

export const nodeTypes: NodeTypes = {
  agent: AgentNode,
  rag: RagNode,
  condition: ConditionNode,
  loop: LoopNode,
  router: RouterNode,
  'a2a-agent': A2ANode,
  human: HumanNode,
  code: CodeNode,
  iteration: IterationNode,
  parallel: ParallelNode,
  'http-request': HttpRequestNode,
  autonomous: AutonomousNode,
  start: StartNode,
  end: EndNode,
  'condition-group': ConditionGroupNode,
  'loop-group': LoopGroupNode,
  'parallel-group': ParallelGroupNode,
  'iteration-group': IterationGroupNode,
}
