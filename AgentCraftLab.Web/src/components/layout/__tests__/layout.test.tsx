/**
 * Layout Components 測試 — AppShell / Sidebar
 */
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}))

vi.mock('react-router-dom', () => ({
  useLocation: () => ({ pathname: '/' }),
  Link: ({ children, to, ...rest }: any) => <a href={to} {...rest}>{children}</a>,
}))

vi.mock('@/lib/utils', () => ({
  cn: (...args: any[]) => args.filter(Boolean).join(' '),
}))

// Mock Sidebar for AppShell tests — 用 spy 讓 Sidebar tests 可以 restore
vi.mock('../Sidebar', async () => {
  const actual = await vi.importActual('../Sidebar')
  return actual
})

import { AppShell } from '../AppShell'
import * as SidebarModule from '../Sidebar'

// ── AppShell ──

describe('AppShell', () => {
  let sidebarSpy: ReturnType<typeof vi.spyOn>

  beforeEach(() => {
    sidebarSpy = vi.spyOn(SidebarModule, 'Sidebar').mockImplementation(() => (
      <aside data-testid="sidebar">Sidebar</aside>
    ))
  })

  it('renders sidebar and children', () => {
    render(
      <AppShell>
        <div data-testid="child">Page content</div>
      </AppShell>,
    )
    expect(screen.getByTestId('sidebar')).toBeDefined()
    expect(screen.getByTestId('child')).toBeDefined()
    expect(screen.getByText('Page content')).toBeDefined()
    sidebarSpy.mockRestore()
  })
})

// ── Sidebar ──

describe('Sidebar', () => {
  it('renders brand name', () => {
    render(<SidebarModule.Sidebar />)
    expect(screen.getByText('AgentCraftLab')).toBeDefined()
  })

  it('renders nav links', () => {
    render(<SidebarModule.Sidebar />)
    // nav.studio 和 nav.settings 由 t(key) => key 回傳
    expect(screen.getByText('nav.studio')).toBeDefined()
    expect(screen.getByText('nav.settings')).toBeDefined()
  })

  it('collapse button toggles sidebar', () => {
    render(<SidebarModule.Sidebar />)
    // 初始有品牌名稱
    expect(screen.getByText('AgentCraftLab')).toBeDefined()

    // 點擊 collapse 按鈕
    const collapseBtn = screen.getByTitle('Collapse')
    fireEvent.click(collapseBtn)

    // 收合後品牌名稱不顯示
    expect(screen.queryByText('AgentCraftLab')).toBeNull()
  })
})
