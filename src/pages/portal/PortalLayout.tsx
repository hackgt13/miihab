import { Outlet } from '@tanstack/react-router'
import { Sidebar } from '../../ui/Sidebar'

export function PortalLayout() {
  return (
    <div className="flex h-screen w-full bg-[#232B2F] overflow-hidden">
      <Sidebar />
      <main className="flex-1 overflow-y-auto bg-[#232B2F]">
        <Outlet />
      </main>
    </div>
  )
}
