import { describe, it, expect } from 'vitest'
import { PROVIDERS, TOOL_CREDENTIAL_PROVIDERS, CREDENTIAL_PROVIDERS, getModelsForProvider } from '../providers'

describe('PROVIDERS', () => {
  it('has required fields for each provider', () => {
    for (const p of PROVIDERS) {
      expect(p.id).toBeTruthy()
      expect(p.name).toBeTruthy()
      expect(Array.isArray(p.models)).toBe(true)
      // keyOptional providers (local inference) may have empty models
      if (!p.keyOptional) {
        expect(p.models.length).toBeGreaterThan(0)
      }
    }
  })

  it('includes major providers', () => {
    const ids = PROVIDERS.map((p) => p.id)
    expect(ids).toContain('openai')
    expect(ids).toContain('azure-openai')
    expect(ids).toContain('anthropic')
    expect(ids).toContain('google')
  })

  it('has unique IDs', () => {
    const ids = PROVIDERS.map((p) => p.id)
    expect(new Set(ids).size).toBe(ids.length)
  })
})

describe('TOOL_CREDENTIAL_PROVIDERS', () => {
  it('has empty models array (tools not LLM)', () => {
    for (const p of TOOL_CREDENTIAL_PROVIDERS) {
      expect(p.models).toEqual([])
    }
  })
})

describe('CREDENTIAL_PROVIDERS', () => {
  it('combines PROVIDERS and TOOL_CREDENTIAL_PROVIDERS', () => {
    expect(CREDENTIAL_PROVIDERS.length).toBe(PROVIDERS.length + TOOL_CREDENTIAL_PROVIDERS.length)
  })
})

describe('getModelsForProvider', () => {
  it('returns models for known provider', () => {
    const models = getModelsForProvider('openai')
    expect(models).toContain('gpt-4o')
    expect(models.length).toBeGreaterThan(0)
  })

  it('returns fallback for unknown provider', () => {
    const models = getModelsForProvider('nonexistent')
    expect(models).toEqual(['gpt-4o-mini'])
  })

  it('does not search TOOL_CREDENTIAL_PROVIDERS', () => {
    // getModelsForProvider only searches PROVIDERS, not tools
    const models = getModelsForProvider('tavily')
    expect(models).toEqual(['gpt-4o-mini']) // fallback
  })
})
