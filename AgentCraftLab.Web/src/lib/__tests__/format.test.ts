import { describe, it, expect } from 'vitest'
import { formatDuration } from '../format'

describe('formatDuration', () => {
  it('formats milliseconds below 1000 as "Xms"', () => {
    expect(formatDuration(0)).toBe('0ms')
    expect(formatDuration(1)).toBe('1ms')
    expect(formatDuration(450)).toBe('450ms')
    expect(formatDuration(999)).toBe('999ms')
  })

  it('formats 1000ms and above as seconds with one decimal', () => {
    expect(formatDuration(1000)).toBe('1.0s')
    expect(formatDuration(1500)).toBe('1.5s')
    expect(formatDuration(3200)).toBe('3.2s')
    expect(formatDuration(10000)).toBe('10.0s')
    expect(formatDuration(62500)).toBe('62.5s')
  })

  it('rounds to one decimal place', () => {
    expect(formatDuration(1234)).toBe('1.2s')
    expect(formatDuration(1250)).toBe('1.3s') // .25 rounds up via toFixed
    expect(formatDuration(1260)).toBe('1.3s')
    expect(formatDuration(9999)).toBe('10.0s')
  })
})
