# React client

Contains instructions on how to work with the Homespun.Web React client.

## UI Components

### shadcn/ui

The project uses shadcn/ui for base components. Components are installed into `src/components/ui/`. Use the default shadcn/ui theme (which will be styled later).

Add new components:

```bash
cd src/Homespun.Web
npx shadcn@latest add button
npx shadcn@latest add input
# etc.
```

**DO NOT create custom components if a shadcn/ui component already exists.**

Configuration is in `components.json`:

- Style: new-york
- Base color: zinc
- CSS Variables: enabled
- Icon library: lucide

### prompt-kit (Chat Components)

For AI chat interface components, use the prompt-kit library. These components are built on shadcn/ui and Tailwind.

Add prompt-kit components:

```bash
npx shadcn@latest add "https://prompt-kit.com/c/prompt-input.json"
npx shadcn@latest add "https://prompt-kit.com/c/message.json"
npx shadcn@latest add "https://prompt-kit.com/c/markdown.json"
```

Available prompt-kit components:

- **prompt-input** - Chat input with file attachments
- **message** - Chat message display
- **markdown** - Markdown rendering with syntax highlighting
- **code-block** - Code display with copy button
- **thinking-bar** - AI thinking indicator
- **loader** - Loading animations

**Use prompt-kit components for all chat and AI-related UI.** Do not create custom chat components.
