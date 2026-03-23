import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import type { Components } from 'react-markdown'
import { Prism as SyntaxHighlighter } from 'react-syntax-highlighter'
import { oneDark } from 'react-syntax-highlighter/dist/esm/styles/prism'

interface MarkdownContentProps {
  content: string
}

const components: Components = {
  code({ className, children, ...props }) {
    const match = /language-(\w+)/.exec(className ?? '')
    const codeString = String(children).replace(/\n$/, '')

    if (match) {
      return (
        <SyntaxHighlighter
          style={oneDark}
          language={match[1]}
          PreTag="div"
          className="!rounded-md !text-sm"
        >
          {codeString}
        </SyntaxHighlighter>
      )
    }

    return (
      <code className="bg-muted text-foreground rounded px-1.5 py-0.5 font-mono text-sm" {...props}>
        {children}
      </code>
    )
  },
  h2({ children, ...props }) {
    const text = String(children)
    const id = text
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/(^-|-$)/g, '')
    return (
      <h2 id={id} className="scroll-mt-20" {...props}>
        {children}
      </h2>
    )
  },
  h3({ children, ...props }) {
    const text = String(children)
    const id = text
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/(^-|-$)/g, '')
    return (
      <h3 id={id} className="scroll-mt-20" {...props}>
        {children}
      </h3>
    )
  },
}

export function MarkdownContent({ content }: MarkdownContentProps) {
  return (
    <div className="prose prose-zinc dark:prose-invert max-w-none" data-testid="markdown-content">
      <ReactMarkdown remarkPlugins={[remarkGfm]} components={components}>
        {content}
      </ReactMarkdown>
    </div>
  )
}
