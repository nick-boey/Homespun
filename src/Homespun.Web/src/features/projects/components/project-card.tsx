import { Link } from '@tanstack/react-router'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { GitBranch, Folder, Trash2 } from 'lucide-react'
import type { Project } from '@/api/generated/types.gen'

interface ProjectCardProps {
  project: Project
  onDelete: (projectId: string) => void
}

export function ProjectCard({ project, onDelete }: ProjectCardProps) {
  const repoDisplay =
    project.gitHubOwner && project.gitHubRepo
      ? `${project.gitHubOwner}/${project.gitHubRepo}`
      : project.localPath

  return (
    <Card className="transition-shadow hover:shadow-md">
      <CardHeader className="pb-3">
        <div className="flex items-start justify-between">
          <Link
            to="/projects/$projectId"
            params={{ projectId: project.id! }}
            className="hover:underline"
          >
            <CardTitle className="text-lg">{project.name}</CardTitle>
          </Link>
          <Button
            variant="ghost"
            size="icon"
            className="text-muted-foreground hover:text-destructive h-8 w-8"
            onClick={() => onDelete(project.id!)}
            aria-label="Delete project"
          >
            <Trash2 className="h-4 w-4" />
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-2">
        <div className="text-muted-foreground flex items-center gap-2 text-sm">
          <Folder className="h-4 w-4" />
          <span>{repoDisplay}</span>
        </div>
        <div className="text-muted-foreground flex items-center gap-2 text-sm">
          <GitBranch className="h-4 w-4" />
          <span>{project.defaultBranch}</span>
        </div>
      </CardContent>
    </Card>
  )
}
