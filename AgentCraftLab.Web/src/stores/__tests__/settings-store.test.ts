import { describe, it, expect, beforeEach } from 'vitest'
import { useSettingsStore } from '../settings-store'

describe('useSettingsStore', () => {
  beforeEach(() => {
    useSettingsStore.setState({
      displayName: '',
      locale: 'zh-TW',
      theme: 'dark',
      defaultProvider: '',
      defaultModel: '',
      dailyTokenLimit: 0,
      costAlertThreshold: 0,
      httpProxy: '',
    })
  })

  it('has correct defaults', () => {
    const state = useSettingsStore.getState()
    expect(state.locale).toBe('zh-TW')
    expect(state.theme).toBe('dark')
    expect(state.dailyTokenLimit).toBe(0)
  })

  it('setDisplayName updates name', () => {
    useSettingsStore.getState().setDisplayName('Alice')
    expect(useSettingsStore.getState().displayName).toBe('Alice')
  })

  it('setLocale updates locale', () => {
    useSettingsStore.getState().setLocale('en')
    expect(useSettingsStore.getState().locale).toBe('en')
  })

  it('setTheme updates theme', () => {
    useSettingsStore.getState().setTheme('light')
    expect(useSettingsStore.getState().theme).toBe('light')
  })

  it('setDefaultProvider updates provider', () => {
    useSettingsStore.getState().setDefaultProvider('openai')
    expect(useSettingsStore.getState().defaultProvider).toBe('openai')
  })

  it('setDefaultModel updates model', () => {
    useSettingsStore.getState().setDefaultModel('gpt-4o')
    expect(useSettingsStore.getState().defaultModel).toBe('gpt-4o')
  })

  it('setDailyTokenLimit updates limit', () => {
    useSettingsStore.getState().setDailyTokenLimit(100000)
    expect(useSettingsStore.getState().dailyTokenLimit).toBe(100000)
  })

  it('setCostAlertThreshold updates threshold', () => {
    useSettingsStore.getState().setCostAlertThreshold(5.0)
    expect(useSettingsStore.getState().costAlertThreshold).toBe(5.0)
  })

  it('setHttpProxy updates proxy', () => {
    useSettingsStore.getState().setHttpProxy('http://proxy:8080')
    expect(useSettingsStore.getState().httpProxy).toBe('http://proxy:8080')
  })
})
