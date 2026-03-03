import { useQuery } from '@tanstack/react-query'
import { GitHubInfo, type GitConfigResponse } from '@/api'

export interface GitConfigResult {
  config: GitConfigResponse | undefined
  isLoading: boolean
  isError: boolean
  error: Error | null
  refetch: () => void
}

export function useGitConfig(): GitConfigResult {
  const query = useQuery({
    queryKey: ['git', 'config'],
    queryFn: async () => {
      const result = await GitHubInfo.getApiGithubGitConfig()
      if (result.error || !result.data) {
        throw new Error('Failed to fetch Git config')
      }
      return result.data
    },
  })

  return {
    config: query.data as GitConfigResponse | undefined,
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}
