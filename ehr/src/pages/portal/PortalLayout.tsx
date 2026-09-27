import { Outlet } from '@tanstack/react-router'
import { Sidebar } from '../../ui/Sidebar'
import { TopAppBar } from '../../ui/TopAppBar'

export function PortalLayout() {
  return (
    <div className="flex flex-col h-screen w-full overflow-hidden">
      <TopAppBar />
      <div className="flex flex-1 overflow-hidden bg-[#E8EDF2]">
        <Sidebar />
        <main className="flex-1 overflow-y-auto bg-[#E8EDF2]">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
