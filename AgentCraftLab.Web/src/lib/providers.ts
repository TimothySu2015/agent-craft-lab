/** Provider 和 Model 清單 — 單一真相來源，供 CredentialsPage 和 AgentForm 共用。 */

export interface ProviderConfig {
  id: string;
  name: string;
  models: string[];
  needsEndpoint?: boolean;
  defaultEndpoint?: string;
  /** API Key 非必填（如 Ollama 本地模型） */
  keyOptional?: boolean;
  /** keyOptional 時的預設 API Key（後端 OpenAI SDK 需要非空值） */
  defaultApiKey?: string;
}

/** 雲端 LLM 提供者（需要 API Key） */
export const CLOUD_PROVIDERS: ProviderConfig[] = [
  { id: 'openai', name: 'OpenAI', models: ['gpt-4o', 'gpt-4o-mini', 'gpt-4.1', 'gpt-4.1-mini'] },
  { id: 'azure-openai', name: 'Azure OpenAI', models: ['gpt-4o', 'gpt-4o-mini', 'gpt-4.1'], needsEndpoint: true },
  { id: 'anthropic', name: 'Anthropic', models: ['claude-sonnet-4-20250514', 'claude-haiku-4-5-20251001'] },
  { id: 'google', name: 'Google AI', models: ['gemini-2.5-flash', 'gemini-2.5-pro'] },
  { id: 'github-copilot', name: 'GitHub Copilot', models: ['gpt-4o', 'gpt-4o-mini'] },
  { id: 'aws-bedrock', name: 'AWS Bedrock', models: ['anthropic.claude-sonnet-4-20250514-v1:0'] },
]

/** 地端 LLM 提供者（本機推理，API Key 非必填） */
export const LOCAL_PROVIDERS: ProviderConfig[] = [
  { id: 'ollama', name: 'Ollama', models: ['gemma4:e4b', 'llama3.3', 'phi4', 'mistral'], needsEndpoint: true, defaultEndpoint: 'http://localhost:11434/v1', keyOptional: true, defaultApiKey: 'local' },
  { id: 'lm-studio', name: 'LM Studio', models: [], needsEndpoint: true, defaultEndpoint: 'http://localhost:1234/v1', keyOptional: true, defaultApiKey: 'local' },
  { id: 'vllm', name: 'vLLM', models: [], needsEndpoint: true, defaultEndpoint: 'http://localhost:8000/v1', keyOptional: true, defaultApiKey: 'local' },
  { id: 'localai', name: 'LocalAI', models: [], needsEndpoint: true, defaultEndpoint: 'http://localhost:8080/v1', keyOptional: true, defaultApiKey: 'local' },
  { id: 'llamacpp', name: 'llama.cpp', models: [], needsEndpoint: true, defaultEndpoint: 'http://localhost:8080/v1', keyOptional: true, defaultApiKey: 'local' },
  { id: 'jan', name: 'Jan', models: [], needsEndpoint: true, defaultEndpoint: 'http://localhost:1337/v1', keyOptional: true, defaultApiKey: 'local' },
]

/** 所有 LLM 提供者（向後相容） */
export const PROVIDERS: ProviderConfig[] = [...CLOUD_PROVIDERS, ...LOCAL_PROVIDERS]

/** 需要 API Key 的工具（Credentials 頁面「工具」分組用） */
export const TOOL_CREDENTIAL_PROVIDERS: ProviderConfig[] = [
  { id: 'azure-web-search', name: 'Azure Web Search', models: [], needsEndpoint: true, defaultEndpoint: 'https://api.bing.microsoft.com/' },
  { id: 'tavily', name: 'Tavily', models: [], needsEndpoint: false },
  { id: 'brave', name: 'Brave Search', models: [], needsEndpoint: false },
  { id: 'serper', name: 'Serper (Google)', models: [], needsEndpoint: false },
  { id: 'smtp', name: 'SMTP Email', models: [], needsEndpoint: true, defaultEndpoint: 'smtp.gmail.com:587' },
]

/** 全部 credential providers（LLM + 工具） */
export const CREDENTIAL_PROVIDERS: ProviderConfig[] = [
  ...PROVIDERS,
  ...TOOL_CREDENTIAL_PROVIDERS,
]

/** 快速查找 provider 的 models */
export function getModelsForProvider(providerId: string): string[] {
  return PROVIDERS.find((p) => p.id === providerId)?.models ?? ['gpt-4o-mini']
}
