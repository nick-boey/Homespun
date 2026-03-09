import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider, createRouter } from '@tanstack/react-router'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { SignalRProvider } from '@/providers/signalr-provider'
import { Toaster } from '@/components/ui/sonner'
import { configureApiClient } from '@/api'
import './index.css'

// Import the generated route tree
import { routeTree } from './routeTree.gen'

// Configure the API client
configureApiClient()

// Create a new router instance
const router = createRouter({ routeTree })

// Register the router instance for type safety
declare module '@tanstack/react-router' {
  interface Register {
    router: typeof router
  }
}

// Create a TanStack Query client
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 1000 * 60, // 1 minute
      retry: 1,
    },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <SignalRProvider>
        <RouterProvider router={router} />
        <Toaster position="bottom-right" closeButton richColors />
      </SignalRProvider>
    </QueryClientProvider>
  </StrictMode>
)
