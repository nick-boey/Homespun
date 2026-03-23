import { HashRouter, Routes, Route } from 'react-router-dom'
import { ThemeProvider } from 'next-themes'
import { Layout } from '@/components/Layout'
import { HomePage } from '@/pages/HomePage'
import { DocPage } from '@/pages/DocPage'
import { NotFoundPage } from '@/pages/NotFoundPage'

export function App() {
  return (
    <ThemeProvider attribute="class" defaultTheme="system" enableSystem>
      <HashRouter>
        <Routes>
          <Route element={<Layout />}>
            <Route index element={<HomePage />} />
            <Route path="docs/:slug" element={<DocPage />} />
            <Route path="*" element={<NotFoundPage />} />
          </Route>
        </Routes>
      </HashRouter>
    </ThemeProvider>
  )
}
