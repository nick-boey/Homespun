export interface ClonesTabProps {
  projectId: string
}

export function ClonesTab({ projectId }: ClonesTabProps) {
  return (
    <div className="space-y-6">
      <h2 className="text-lg font-semibold">Clones</h2>
      <p className="text-muted-foreground">Clone management coming soon. Project ID: {projectId}</p>
    </div>
  )
}
