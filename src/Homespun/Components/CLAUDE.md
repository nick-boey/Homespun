## Design System and Component Showcases

The design system at `/design` provides a catalog of all UI components with mock data for visual testing. This is only available in mock mode.

### Browsing Components

Navigate to `http://localhost:{PORT}/design` (in mock mode) to see all registered components organized by category:
- **Core**: WorkItem, PrStatusBadges, NotificationBanner, etc.
- **Forms**: ModelSelector, AgentSelector, QuickIssueCreateBar
- **Chat**: ChatMessage, ChatInput, ToolUseBlock, ThinkingBlock, etc.
- **Panels**: IssueDetailPanel, CurrentPullRequestDetailPanel, etc.

Click any component card to view its showcase with multiple variations and states.

### Adding a New Component to the Design System

When creating a new shared component, add it to the design system for visual testing:

1. **Register the component** in `Features/Design/ComponentRegistryService.cs`:
   ```csharp
   new ComponentMetadata
   {
       Id = "my-component",           // URL slug (kebab-case)
       Name = "MyComponent",          // Display name
       Description = "Brief description of what it does.",
       Category = "Core",             // Core, Forms, Chat, or Panels
       ComponentPath = "Components/Shared/MyComponent.razor",
       Tags = ["tag1", "tag2"]        // For search/filtering
   }
   ```

2. **Create a showcase file** at `Components/Pages/Design/Showcases/MyComponentShowcase.razor`:
   ```razor
   <div class="showcase-section">
       <h3>Default State</h3>
       <div class="showcase-item">
           <div class="showcase-label">Description of this variant</div>
           <div class="showcase-preview">
               <MyComponent Prop1="value1" />
           </div>
       </div>
   </div>

   <div class="showcase-section">
       <h3>Loading State</h3>
       <div class="showcase-item">
           <div class="showcase-label">When loading data</div>
           <div class="showcase-preview">
               <MyComponent IsLoading="true" />
           </div>
       </div>
   </div>

   @code {
       // Add any mock data needed for the showcase
   }
   ```

3. **Add the showcase case** to `Components/Pages/Design/ComponentShowcase.razor`:
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
- Group related variations under descriptive `<h3>` headings

