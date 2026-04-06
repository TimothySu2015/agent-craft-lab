/**
 * CoAgent State Store — 由 AgentStateProvider 寫入，NodeShell / HumanInputPanel 讀取。
 * 橋接 CopilotKit useCoAgent（需在 Provider 內）和 Provider 外的元件。
 */
import { create } from 'zustand'
import type { AgentState } from './agent-state'
import { INITIAL_AGENT_STATE } from './agent-state'

interface CoAgentStore {
  state: AgentState
  setState: (state: AgentState) => void
  reset: () => void
}

export const useCoAgentStore = create<CoAgentStore>((set) => ({
  state: INITIAL_AGENT_STATE,
  setState: (state) => set({ state }),
  reset: () => set({ state: INITIAL_AGENT_STATE }),
}))
