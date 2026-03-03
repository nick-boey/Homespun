import { useQuery } from '@tanstack/react-query'
import { GitHubInfo, type GitHubStatusResponse, type GitHubAuthStatus } from '@/api'

export interface GitHubInfoResult {
  status: GitHubStatusResponse | undefined
  authStatus: GitHubAuthStatus | undefined
  isLoading: boolean
  isError: boolean
  error: Error | null
  refetch: () => void
}

export function useGitHubInfo(): GitHubInfoResult {
  const statusQuery = useQuery({
    queryKey: ['github', 'status'],
    queryFn: async () => {
      const result = await GitHubInfo.getApiGithubStatus()
      if (result.error || !result.data) {
        throw new Error('Failed to fetch GitHub status')
      }
      return result.data
    },
  })

  const authStatusQuery = useQuery({
    queryKey: ['github', 'auth-status'],
    queryFn: async () => {
      const result = await GitHubInfo.getApiGithubAuthStatus()
      if (result.error || !result.data) {
        throw new Error('Failed to fetch GitHub auth status')
      }
      return result.data
    },
  })

  const refetch = () => {
    statusQuery.refetch()
    authStatusQuery.refetch()
  }

  return {
    status: statusQuery.data as GitHubStatusResponse | undefined,
    authStatus: authStatusQuery.data as GitHubAuthStatus | undefined,
    isLoading: statusQuery.isLoading || authStatusQuery.isLoading,
    isError: statusQuery.isError || authStatusQuery.isError,
    error: statusQuery.error || authStatusQuery.error,
    refetch,
  }
}
