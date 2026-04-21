import { useQuery } from '@tanstack/react-query'
import { useMemo } from 'react'
import { Models } from '@/api'
import type { ClaudeModelInfo } from '@/api/generated/types.gen'

export const availableModelsQueryKey = ['models'] as const

const ONE_DAY_MS = 24 * 60 * 60 * 1000

export interface UseAvailableModelsResult {
  models: ClaudeModelInfo[]
  defaultModel: ClaudeModelInfo | null
  isLoading: boolean
  isError: boolean
  error: unknown
}

export function useAvailableModels(): UseAvailableModelsResult {
  const { data, isLoading, isError, error } = useQuery({
    queryKey: availableModelsQueryKey,
    queryFn: async (): Promise<ClaudeModelInfo[]> => {
      const response = await Models.getApiModels()
      return (response.data ?? []) as ClaudeModelInfo[]
    },
    staleTime: ONE_DAY_MS,
  })

  const models = useMemo(() => data ?? [], [data])
  const defaultModel = useMemo(() => models.find((m) => m.isDefault) ?? models[0] ?? null, [models])

  return { models, defaultModel, isLoading, isError, error }
}
