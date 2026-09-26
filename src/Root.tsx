import { Outlet } from '@tanstack/react-router'
import { GrainOverlay } from './ui/GrainOverlay'

export function Root() {
  return (
    <>
      <GrainOverlay />
      <Outlet />
    </>
  )
}
