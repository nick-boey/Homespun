import { useState } from 'react'
import { createFileRoute } from '@tanstack/react-router'
import { useBreadcrumbSetter } from '@/hooks/use-breadcrumbs'
import {
  useGitHubInfo,
  useGitConfig,
  useUserSettings,
  useUpdateUserEmail,
} from '@/features/settings'
import { GitHubAuthMethod } from '@/api'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Skeleton } from '@/components/ui/skeleton'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { useAppStore } from '@/stores/app-store'
import {
  AlertCircle,
  CheckCircle2,
  Github,
  GitBranch,
  User,
  Mail,
  Key,
  Sun,
  Moon,
  Monitor,
  Pencil,
} from 'lucide-react'

export const Route = createFileRoute('/settings')({
  component: Settings,
})

function Settings() {
  useBreadcrumbSetter([{ title: 'Settings' }], [])

  const { status, authStatus, isLoading: isGitHubLoading, isError: isGitHubError } = useGitHubInfo()
  const { config, isLoading: isGitConfigLoading, isError: isGitConfigError } = useGitConfig()

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold">Settings</h1>
        <p className="text-muted-foreground mt-1">
          Configure GitHub authentication and Git settings for Homespun.
        </p>
      </div>

      <div className="grid gap-6">
        <UserEmailSection />

        <ThemeSection />

        <GitHubAuthSection
          status={status}
          authStatus={authStatus}
          isLoading={isGitHubLoading}
          isError={isGitHubError}
        />

        <GitConfigSection
          config={config}
          isLoading={isGitConfigLoading}
          isError={isGitConfigError}
        />
      </div>
    </div>
  )
}

interface GitHubAuthSectionProps {
  status:
    | {
        isConfigured?: boolean
        maskedToken?: string | null
      }
    | undefined
  authStatus:
    | {
        isAuthenticated?: boolean
        username?: string | null
        message?: string | null
        errorMessage?: string | null
        authMethod?: (typeof GitHubAuthMethod)[keyof typeof GitHubAuthMethod]
      }
    | undefined
  isLoading: boolean
  isError: boolean
}

function GitHubAuthSection({ status, authStatus, isLoading, isError }: GitHubAuthSectionProps) {
  if (isLoading) {
    return (
      <Card>
        <CardHeader>
          <div className="flex items-center gap-2">
            <Github className="h-5 w-5" />
            <CardTitle>GitHub Authentication</CardTitle>
          </div>
          <CardDescription>Manage your GitHub connection for repository access.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <Skeleton className="h-6 w-32" />
          <Skeleton className="h-4 w-48" />
          <Skeleton className="h-4 w-64" />
        </CardContent>
      </Card>
    )
  }

  if (isError) {
    return (
      <Card>
        <CardHeader>
          <div className="flex items-center gap-2">
            <Github className="h-5 w-5" />
            <CardTitle>GitHub Authentication</CardTitle>
          </div>
          <CardDescription>Manage your GitHub connection for repository access.</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="text-destructive flex items-center gap-2">
            <AlertCircle className="h-4 w-4" />
            <span>Failed to load GitHub status. Please try refreshing the page.</span>
          </div>
        </CardContent>
      </Card>
    )
  }

  const isAuthenticated = authStatus?.isAuthenticated ?? false
  const isConfigured = status?.isConfigured ?? false

  return (
    <Card>
      <CardHeader>
        <div className="flex items-center gap-2">
          <Github className="h-5 w-5" />
          <CardTitle>GitHub Authentication</CardTitle>
        </div>
        <CardDescription>Manage your GitHub connection for repository access.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="flex items-center gap-3">
          <span className="text-muted-foreground w-28 text-sm font-medium">Status:</span>
          {isAuthenticated ? (
            <Badge variant="default" className="gap-1">
              <CheckCircle2 className="h-3 w-3" />
              Authenticated
            </Badge>
          ) : (
            <Badge variant="destructive" className="gap-1">
              <AlertCircle className="h-3 w-3" />
              Not Authenticated
            </Badge>
          )}
        </div>

        {authStatus?.username && (
          <div className="flex items-center gap-3">
            <span className="text-muted-foreground w-28 text-sm font-medium">
              <User className="mr-1 inline h-4 w-4" />
              Username:
            </span>
            <span className="font-mono text-sm">{authStatus.username}</span>
          </div>
        )}

        {authStatus?.message && (
          <div className="flex items-center gap-3">
            <span className="text-muted-foreground w-28 text-sm font-medium">Message:</span>
            <span className="text-muted-foreground text-sm">{authStatus.message}</span>
          </div>
        )}

        {authStatus?.errorMessage && (
          <div className="flex items-center gap-3">
            <span className="text-muted-foreground w-28 text-sm font-medium">Error:</span>
            <span className="text-destructive text-sm">{authStatus.errorMessage}</span>
          </div>
        )}

        {isConfigured && status?.maskedToken && (
          <div className="flex items-center gap-3">
            <span className="text-muted-foreground w-28 text-sm font-medium">
              <Key className="mr-1 inline h-4 w-4" />
              Token:
            </span>
            <code className="bg-muted rounded px-2 py-1 font-mono text-sm">
              {status.maskedToken}
            </code>
          </div>
        )}

        <div className="flex items-center gap-3">
          <span className="text-muted-foreground w-28 text-sm font-medium">Auth Method:</span>
          <span className="text-sm">{getAuthMethodLabel(authStatus?.authMethod)}</span>
        </div>
      </CardContent>
    </Card>
  )
}

function getAuthMethodLabel(
  method: (typeof GitHubAuthMethod)[keyof typeof GitHubAuthMethod] | undefined
): string {
  switch (method) {
    case GitHubAuthMethod[0]:
      return 'None'
    case GitHubAuthMethod[1]:
      return 'Token (GITHUB_TOKEN)'
    case GitHubAuthMethod[2]:
      return 'GitHub CLI (gh auth)'
    case GitHubAuthMethod[3]:
      return 'Both (Token + CLI)'
    default:
      return 'Unknown'
  }
}

interface GitConfigSectionProps {
  config:
    | {
        authorName?: string | null
        authorEmail?: string | null
      }
    | undefined
  isLoading: boolean
  isError: boolean
}

function GitConfigSection({ config, isLoading, isError }: GitConfigSectionProps) {
  if (isLoading) {
    return (
      <Card>
        <CardHeader>
          <div className="flex items-center gap-2">
            <GitBranch className="h-5 w-5" />
            <CardTitle>Git Configuration</CardTitle>
          </div>
          <CardDescription>Git author information used for commits.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <Skeleton className="h-4 w-48" />
          <Skeleton className="h-4 w-56" />
        </CardContent>
      </Card>
    )
  }

  if (isError) {
    return (
      <Card>
        <CardHeader>
          <div className="flex items-center gap-2">
            <GitBranch className="h-5 w-5" />
            <CardTitle>Git Configuration</CardTitle>
          </div>
          <CardDescription>Git author information used for commits.</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="text-destructive flex items-center gap-2">
            <AlertCircle className="h-4 w-4" />
            <span>Failed to load Git configuration. Please try refreshing the page.</span>
          </div>
        </CardContent>
      </Card>
    )
  }

  return (
    <Card>
      <CardHeader>
        <div className="flex items-center gap-2">
          <GitBranch className="h-5 w-5" />
          <CardTitle>Git Configuration</CardTitle>
        </div>
        <CardDescription>Git author information used for commits by agents.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="flex items-center gap-3">
          <span className="text-muted-foreground w-28 text-sm font-medium">
            <User className="mr-1 inline h-4 w-4" />
            Author Name:
          </span>
          <span className="font-mono text-sm">{config?.authorName ?? 'Not configured'}</span>
        </div>

        <div className="flex items-center gap-3">
          <span className="text-muted-foreground w-28 text-sm font-medium">
            <Mail className="mr-1 inline h-4 w-4" />
            Author Email:
          </span>
          <span className="font-mono text-sm">{config?.authorEmail ?? 'Not configured'}</span>
        </div>

        <div className="mt-4 border-t pt-4">
          <p className="text-muted-foreground text-xs">
            These values can be configured via environment variables (GIT_AUTHOR_NAME,
            GIT_AUTHOR_EMAIL) or in the application configuration (Git:AuthorName, Git:AuthorEmail).
          </p>
        </div>
      </CardContent>
    </Card>
  )
}

function ThemeSection() {
  const theme = useAppStore((state) => state.theme)
  const setTheme = useAppStore((state) => state.setTheme)

  return (
    <Card>
      <CardHeader>
        <div className="flex items-center gap-2">
          <Sun className="h-5 w-5" />
          <CardTitle>Appearance</CardTitle>
        </div>
        <CardDescription>Customize the application appearance.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="flex items-center gap-3">
          <span className="text-muted-foreground w-28 text-sm font-medium">Theme:</span>
          <Select
            value={theme}
            onValueChange={(value) => setTheme(value as 'light' | 'dark' | 'system')}
          >
            <SelectTrigger className="w-40" aria-label="Select theme">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="light">
                <span className="flex items-center gap-2">
                  <Sun className="h-4 w-4" />
                  Light
                </span>
              </SelectItem>
              <SelectItem value="dark">
                <span className="flex items-center gap-2">
                  <Moon className="h-4 w-4" />
                  Dark
                </span>
              </SelectItem>
              <SelectItem value="system">
                <span className="flex items-center gap-2">
                  <Monitor className="h-4 w-4" />
                  System
                </span>
              </SelectItem>
            </SelectContent>
          </Select>
        </div>
      </CardContent>
    </Card>
  )
}

function UserEmailSection() {
  const { userEmail, isLoading, isError } = useUserSettings()
  const { mutate: updateEmail, isPending } = useUpdateUserEmail()
  const [isEditing, setIsEditing] = useState(false)
  const [editValue, setEditValue] = useState('')

  const handleEdit = () => {
    setEditValue(userEmail ?? '')
    setIsEditing(true)
  }

  const handleSave = () => {
    if (editValue.trim()) {
      updateEmail(editValue.trim())
      setIsEditing(false)
    }
  }

  const handleCancel = () => {
    setIsEditing(false)
    setEditValue('')
  }

  if (isLoading) {
    return (
      <Card>
        <CardHeader>
          <div className="flex items-center gap-2">
            <User className="h-5 w-5" />
            <CardTitle>User Settings</CardTitle>
          </div>
          <CardDescription>Your personal settings for Homespun.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <Skeleton className="h-4 w-48" />
        </CardContent>
      </Card>
    )
  }

  if (isError) {
    return (
      <Card>
        <CardHeader>
          <div className="flex items-center gap-2">
            <User className="h-5 w-5" />
            <CardTitle>User Settings</CardTitle>
          </div>
          <CardDescription>Your personal settings for Homespun.</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="text-destructive flex items-center gap-2">
            <AlertCircle className="h-4 w-4" />
            <span>Failed to load user settings. Please try refreshing the page.</span>
          </div>
        </CardContent>
      </Card>
    )
  }

  return (
    <Card>
      <CardHeader>
        <div className="flex items-center gap-2">
          <User className="h-5 w-5" />
          <CardTitle>User Settings</CardTitle>
        </div>
        <CardDescription>Your personal settings for Homespun.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="flex items-center gap-3">
          <span className="text-muted-foreground w-28 text-sm font-medium">
            <Mail className="mr-1 inline h-4 w-4" />
            Email:
          </span>
          {isEditing ? (
            <div className="flex items-center gap-2">
              <Input
                type="email"
                value={editValue}
                onChange={(e) => setEditValue(e.target.value)}
                placeholder="Enter your email"
                className="w-64"
                autoFocus
                onKeyDown={(e) => {
                  if (e.key === 'Enter') handleSave()
                  if (e.key === 'Escape') handleCancel()
                }}
              />
              <Button size="sm" onClick={handleSave} disabled={isPending || !editValue.trim()}>
                Save
              </Button>
              <Button size="sm" variant="outline" onClick={handleCancel} disabled={isPending}>
                Cancel
              </Button>
            </div>
          ) : (
            <div className="flex items-center gap-2">
              <span className="font-mono text-sm">
                {userEmail ?? <span className="text-muted-foreground italic">Not configured</span>}
              </span>
              <Button size="sm" variant="ghost" onClick={handleEdit} className="h-6 w-6 p-0">
                <Pencil className="h-3 w-3" />
                <span className="sr-only">Edit email</span>
              </Button>
            </div>
          )}
        </div>

        {!userEmail && (
          <div className="mt-4 border-t pt-4">
            <p className="text-muted-foreground text-xs">
              Your email is required. It will be used to assign issues created through Homespun to
              you.
            </p>
          </div>
        )}
      </CardContent>
    </Card>
  )
}
