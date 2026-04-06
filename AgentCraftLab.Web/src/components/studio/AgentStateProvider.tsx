/**
 * AgentStateProvider — 放在 CopilotKit Provider 內部。
 * 使用 useCoAgent 接收 AG-UI STATE_SNAPSHOT，同步到 coagent-store。
 * NodeShell 和 HumanInputPanel 從 coagent-store 讀取，不需要輪詢。
 */
import { useEffect } from 'react'
import { useCoAgent } from '@copilotkit/react-core'
import { useCoAgentStore } from '@/stores/coagent-store'
import { INITIAL_AGENT_STATE, type AgentState } from '@/stores/agent-state'

export function AgentStateProvider() {
  const { state } = useCoAgent<AgentState>({
    name: 'craftlab',
    initialState: INITIAL_AGENT_STATE,
  })

  const setStoreState = useCoAgentStore((s) => s.setState)

  useEffect(() => {
    if (state) {
      setStoreState(state)
    }
  }, [state, setStoreState])

  return null
}
