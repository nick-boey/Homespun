import { createRootRoute } from '@tanstack/react-router'
import { TanStackRouterDevtools } from '@tanstack/router-devtools'
import { RootLayout } from '@/components/layout'
import { NotFound } from '@/components/not-found'
import { TelemetryProvider } from '@/providers/telemetry-provider'

export const Route = createRootRoute({
  component: () => (
    <TelemetryProvider>
      <RootLayout />
      <TanStackRouterDevtools />
    </TelemetryProvider>
  ),
  notFoundComponent: NotFound,
})
