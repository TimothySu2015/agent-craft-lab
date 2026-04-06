import { Navigate } from 'react-router-dom'

/** Credentials 頁面已整合到 Settings，自動跳轉。 */
export function CredentialsPage() {
  return <Navigate to="/settings" replace />
}
