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

The classification of every file in `components/ui/` — native shadcn primitive, intentionally-custom, or prompt-kit (chat-owned) — is captured in `src/components/ui/INVENTORY.md`. Check that file before editing or replacing anything there.

### Chat surface

The chat surface under `features/sessions/` is currently built on prompt-kit primitives (`message`, `markdown`, `code-block`, `prompt-input`, `thinking-bar`, `loader`, `scroll-to-bottom`, `text-shimmer`). Those components are being replaced wholesale by the Assistant UI runtime under the `chat-assistant-ui` OpenSpec change — **do not author new chat components in `components/ui/`**, and prefer waiting for the Assistant UI migration to land before extending the chat surface.

### Storybook

Storybook 10 runs against the real Tailwind v4 theme (`src/index.css`). Stories are co-located with their components as `*.stories.tsx` under `src/**`. Every shadcn-native and `divergent-keep` primitive in `components/ui/` has a story.

```bash
cd src/Homespun.Web

# Dev server
npm run storybook        # http://localhost:6006

# Static build (part of the pre-PR checklist)
npm run build-storybook
```

When adding a new shadcn primitive via `npx shadcn@latest add <name>`, add a co-located `<name>.stories.tsx` with at minimum a Default story, and an interactive `play` story for anything portal-rendered or with toggle state. The `build-storybook` step in the pre-PR checklist in the project-root `CLAUDE.md` catches story drift at author time.
