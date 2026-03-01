## Design System and Component Showcases

The design system at `/design` provides a catalog of all UI components with mock data for visual testing. This is only available in mock mode.

### Browsing Components

Navigate to `http://localhost:{PORT}/design` (in mock mode) to see all registered components organized by category:
- **Core**: WorkItem, PrStatusBadges, NotificationBanner, etc.
- **Forms**: ModelSelector, AgentSelector, FormMessage
- **Chat**: ChatMessage, ChatInput, ToolUseBlock, ThinkingBlock, etc.
- **Panels**: IssueDetailPanel, CurrentPullRequestDetailPanel, etc.

Click any component card to view its showcase with multiple variations and states.

### Adding a New Component to the Design System

When creating a new shared component, add it to the design system for visual testing:

1. **Register the component** in `src/Homespun.Server/Features/Design/ComponentRegistryService.cs`:
   ```csharp
   new ComponentMetadata
   {
       Id = "my-component",           // URL slug (kebab-case)
       Name = "MyComponent",          // Display name
       Description = "Brief description of what it does.",
       Category = "Core",             // Core, Forms, Chat, or Panels
       ComponentPath = "Components/MyComponent.razor",
       Tags = ["tag1", "tag2"]        // For search/filtering
   }
   ```

2. **Create a showcase file** at `Features/{Feature}/Components/Showcases/MyComponentShowcase.razor`:
   ```razor
   <BbCard>
       <BbCardHeader>
           <BbCardTitle>Default State</BbCardTitle>
           <BbCardDescription>Description of this variant</BbCardDescription>
       </BbCardHeader>
       <BbCardContent>
           <MyComponent Prop1="value1" />
       </BbCardContent>
   </BbCard>

   <BbCard>
       <BbCardHeader>
           <BbCardTitle>Loading State</BbCardTitle>
           <BbCardDescription>When loading data</BbCardDescription>
       </BbCardHeader>
       <BbCardContent>
           <MyComponent IsLoading="true" />
       </BbCardContent>
   </BbCard>

   @code {
       // Add any mock data needed for the showcase
   }
   ```

3. **Add the showcase case** to `Features/Design/Pages/ComponentShowcase.razor`:
   ```csharp
   case "my-component":
       <MyComponentShowcase />
       break;
   ```

4. **For components with service dependencies**, create a mock wrapper component (e.g., `MockMyComponent.razor`) that accepts parameters instead of injecting services, then use that in the showcase.

### Showcase Best Practices

- Show multiple states: default, loading, error, empty, disabled
- Use realistic mock data that demonstrates the component's purpose
- Include edge cases: long text, missing data, extreme values
- For interactive components, show both enabled and disabled states
- Group related variations under descriptive `<BbCardTitle>` headings inside `<BbCard>` components
