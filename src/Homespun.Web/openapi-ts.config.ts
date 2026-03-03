import { defineConfig } from '@hey-api/openapi-ts'

export default defineConfig({
  input: process.env.OPENAPI_INPUT || './swagger.json',
  output: {
    path: 'src/api/generated',
  },
  plugins: [
    {
      name: '@hey-api/typescript',
      enums: 'javascript',
    },
    {
      name: '@hey-api/sdk',
      operations: {
        strategy: 'byTags',
      },
    },
    '@hey-api/client-fetch',
  ],
})
