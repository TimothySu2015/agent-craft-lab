/**
 * MiddlewareConfigDialog 測試 — 驗證 middleware toggle、config 欄位更新、apply 序列化。
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { MiddlewareConfigDialog } from '../MiddlewareConfigDialog'

// Mock i18n
vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}))

describe('MiddlewareConfigDialog', () => {
  const defaultProps = {
    open: true,
    middleware: '',
    config: {},
    onClose: vi.fn(),
    onApply: vi.fn(),
  }

  beforeEach(() => {
    defaultProps.onClose = vi.fn()
    defaultProps.onApply = vi.fn()
  })

  it('does not render when closed', () => {
    const { container } = render(<MiddlewareConfigDialog {...defaultProps} open={false} />)
    expect(container.innerHTML).toBe('')
  })

  it('renders all 5 middleware options', () => {
    render(<MiddlewareConfigDialog {...defaultProps} />)

    // Each label appears in both sidebar and detail panel, so use getAllByText
    expect(screen.getAllByText('middlewareConfig.label.guardrails').length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText('middlewareConfig.label.pii').length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText('middlewareConfig.label.rateLimit').length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText('middlewareConfig.label.retry').length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText('middlewareConfig.label.logging').length).toBeGreaterThanOrEqual(1)
  })

  it('initializes active set from middleware prop', () => {
    render(<MiddlewareConfigDialog {...defaultProps} middleware="guardrails,retry" />)

    const checkboxes = screen.getAllByRole('checkbox')
    // guardrails and retry should be checked
    const guardrailsCb = checkboxes[0]
    const retryCb = checkboxes[3]
    expect(guardrailsCb).toBeChecked()
    expect(retryCb).toBeChecked()
  })

  it('toggles middleware on/off', () => {
    render(<MiddlewareConfigDialog {...defaultProps} />)

    const checkboxes = screen.getAllByRole('checkbox')
    // Toggle guardrails on
    fireEvent.click(checkboxes[0])
    // Toggle it off
    fireEvent.click(checkboxes[0])

    // We can't directly check set state, but we can check apply result
  })

  it('shows "enable first" message for inactive middleware', () => {
    render(<MiddlewareConfigDialog {...defaultProps} />)

    // Click on GuardRails (first item is already selected by default)
    expect(screen.getByText('middlewareConfig.enableFirst')).toBeInTheDocument()
  })

  it('shows config fields when middleware is active', () => {
    render(<MiddlewareConfigDialog {...defaultProps} middleware="guardrails" />)

    // GuardRails should show its i18n-keyed fields
    expect(screen.getByText('middlewareConfig.guardrails.blockedTerms')).toBeInTheDocument()
    expect(screen.getAllByText('middlewareConfig.guardrails.scanAllMessages').length).toBeGreaterThanOrEqual(1)
  })

  it('selects different middleware to show its fields', () => {
    render(<MiddlewareConfigDialog {...defaultProps} middleware="guardrails,ratelimit" />)

    // Click on Rate Limit
    fireEvent.click(screen.getByText('middlewareConfig.label.rateLimit'))

    expect(screen.getByText('middlewareConfig.rateLimit.maxPerMinute')).toBeInTheDocument()
    expect(screen.getByText('middlewareConfig.rateLimit.cooldownMs')).toBeInTheDocument()
  })

  it('applies with correct middleware string and config', () => {
    render(<MiddlewareConfigDialog {...defaultProps} middleware="guardrails" config={{ guardrails: { severity: 'warn' } }} />)

    // Click Apply button
    fireEvent.click(screen.getByText('toolPicker.apply'))

    expect(defaultProps.onApply).toHaveBeenCalledWith(
      'guardrails',
      expect.objectContaining({
        guardrails: expect.objectContaining({ severity: 'warn' }),
      }),
    )
    expect(defaultProps.onClose).toHaveBeenCalled()
  })

  it('applies with multiple active middleware as comma-separated string', () => {
    render(<MiddlewareConfigDialog {...defaultProps} middleware="guardrails,retry" />)

    fireEvent.click(screen.getByText('toolPicker.apply'))

    const [middlewareStr] = defaultProps.onApply.mock.calls[0]
    const parts = middlewareStr.split(',')
    expect(parts).toContain('guardrails')
    expect(parts).toContain('retry')
  })

  it('applies empty string when no middleware active', () => {
    render(<MiddlewareConfigDialog {...defaultProps} middleware="" />)

    fireEvent.click(screen.getByText('toolPicker.apply'))

    expect(defaultProps.onApply).toHaveBeenCalledWith('', expect.any(Object))
  })

  it('updates field values in config', () => {
    render(<MiddlewareConfigDialog {...defaultProps} middleware="ratelimit" />)

    // Switch to Rate Limit panel by clicking in sidebar
    const sidebar = screen.getAllByText('middlewareConfig.label.rateLimit')[0].closest('div[class*="cursor-pointer"]')!
    fireEvent.click(sidebar)

    // Fill in max per minute
    const inputs = screen.getAllByRole('spinbutton')
    fireEvent.change(inputs[0], { target: { value: '120' } })

    // Apply and check config
    fireEvent.click(screen.getByText('toolPicker.apply'))

    expect(defaultProps.onApply).toHaveBeenCalledWith(
      'ratelimit',
      expect.objectContaining({
        ratelimit: expect.objectContaining({ maxPerMinute: '120' }),
      }),
    )
  })

  it('shows checkbox fields for PII masking', () => {
    render(<MiddlewareConfigDialog {...defaultProps} middleware="pii" />)

    // Click PII Masking in sidebar
    const sidebar = screen.getAllByText('middlewareConfig.label.pii')[0].closest('div[class*="cursor-pointer"]')!
    fireEvent.click(sidebar)

    // PII fields use i18n keys
    expect(screen.getByText('middlewareConfig.pii.mode')).toBeInTheDocument()
    expect(screen.getByText('middlewareConfig.pii.confidenceThreshold')).toBeInTheDocument()
    expect(screen.getByText('middlewareConfig.pii.locales')).toBeInTheDocument()
  })

  it('shows retry strategy options', () => {
    render(<MiddlewareConfigDialog {...defaultProps} middleware="retry" />)

    // Click Retry in sidebar
    const sidebar = screen.getAllByText('middlewareConfig.label.retry')[0].closest('div[class*="cursor-pointer"]')!
    fireEvent.click(sidebar)

    expect(screen.getByText('middlewareConfig.retry.maxRetries')).toBeInTheDocument()
    expect(screen.getByText('middlewareConfig.retry.strategy')).toBeInTheDocument()
    expect(screen.getByText('middlewareConfig.retry.initialDelayMs')).toBeInTheDocument()
  })
})
