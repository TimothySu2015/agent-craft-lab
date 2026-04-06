import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { Suspense } from 'react'
import { Toaster } from 'sonner'
import { ErrorBoundary } from '@/components/shared/ErrorBoundary'
import { AppShell } from '@/components/layout/AppShell'
import { StudioPage } from '@/components/studio/StudioPage'
import { CredentialsPage } from '@/pages/CredentialsPage'
import { KnowledgeBasePage } from '@/pages/KnowledgeBasePage'
import { RequestLogsPage } from '@/pages/RequestLogsPage'
import { PublishedServicesPage } from '@/pages/PublishedServicesPage'
import { ApiKeysPage } from '@/pages/ApiKeysPage'
import { ServiceTesterPage } from '@/pages/ServiceTesterPage'
import { SkillsPage } from '@/pages/SkillsPage'
import { SettingsPage } from '@/pages/SettingsPage'
import { SchedulesPage } from '@/pages/SchedulesPage'
import { DocRefineryPage } from '@/pages/DocRefineryPage'

export default function App() {
  return (
    <ErrorBoundary>
      <Toaster position="bottom-right" theme="dark" visibleToasts={3} gap={8} offset={16} richColors closeButton />
      <BrowserRouter>
        <Suspense fallback={<div className="flex h-screen items-center justify-center text-muted-foreground">Loading...</div>}>
          <AppShell>
            <Routes>
              <Route path="/" element={<StudioPage />} />
              <Route path="/credentials" element={<CredentialsPage />} />
              <Route path="/knowledge-bases" element={<KnowledgeBasePage />} />
              <Route path="/published-services" element={<PublishedServicesPage />} />
              <Route path="/api-keys" element={<ApiKeysPage />} />
              <Route path="/service-tester" element={<ServiceTesterPage />} />
              <Route path="/request-logs" element={<RequestLogsPage />} />
              <Route path="/skills" element={<SkillsPage />} />
              <Route path="/settings" element={<SettingsPage />} />
              <Route path="/schedules" element={<SchedulesPage />} />
              <Route path="/doc-refinery" element={<DocRefineryPage />} />
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </AppShell>
        </Suspense>
      </BrowserRouter>
    </ErrorBoundary>
  )
}
