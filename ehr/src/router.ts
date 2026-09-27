import { createRouter, createRoute, createRootRoute } from '@tanstack/react-router'
import { Root } from './Root'
import { HomePage } from './pages/HomePage'
import { LoginPage } from './pages/LoginPage'
import { PortalLayout } from './pages/portal/PortalLayout'
import { PortalIndex } from './pages/portal/PortalIndex'
import { PatientView } from './pages/portal/PatientView'

const rootRoute = createRootRoute({ component: Root })

const homeRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/',
  component: HomePage,
})

const loginRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/login',
  component: LoginPage,
})

const portalLayoutRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: '/portal',
  component: PortalLayout,
})

const portalIndexRoute = createRoute({
  getParentRoute: () => portalLayoutRoute,
  path: '/',
  component: PortalIndex,
})

const patientRoute = createRoute({
  getParentRoute: () => portalLayoutRoute,
  path: '/$patientId',
  component: PatientView,
})

const routeTree = rootRoute.addChildren([
  homeRoute,
  loginRoute,
  portalLayoutRoute.addChildren([portalIndexRoute, patientRoute]),
])

// Under /ehr on the coordinator; import.meta.env.BASE_URL is '/ehr/' in both the build and `npm run dev`.
export const router = createRouter({ routeTree, basepath: import.meta.env.BASE_URL.replace(/\/$/, '') || '/' })

declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}
