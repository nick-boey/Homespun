import type { Preview } from '@storybook/react-vite'

import '../src/index.css'

const ZINC_950 = 'hsl(240 10% 3.9%)'

const preview: Preview = {
  parameters: {
    backgrounds: {
      options: {
        light: { name: 'Light', value: '#ffffff' },
        dark: { name: 'Dark', value: ZINC_950 },
      },
    },
  },
  initialGlobals: {
    backgrounds: { value: 'light' },
    theme: 'light',
  },
  globalTypes: {
    theme: {
      description: 'Toggle light/dark class on <html>',
      toolbar: {
        title: 'Theme',
        icon: 'paintbrush',
        items: [
          { value: 'light', title: 'Light' },
          { value: 'dark', title: 'Dark' },
        ],
        dynamicTitle: true,
      },
    },
  },
  decorators: [
    (Story, context) => {
      const theme = context.globals.theme === 'dark' ? 'dark' : 'light'
      if (typeof document !== 'undefined') {
        document.documentElement.classList.toggle('dark', theme === 'dark')
      }
      return Story()
    },
  ],
}

export default preview
