# Task Graph Issue Insertion Behaviour

## Purpose

This document specifies how new issues are inserted into the task graph dependency diagram. It is intended for test writers implementing tests against `KeyboardNavigationService` and `TaskGraphView`.

**Source files:**
- `src/Homespun.Client/Services/IKeyboardNavigationService.cs` — `PendingNewIssue` model (properties below)
- `src/Homespun.Client/Services/KeyboardNavigationService.cs` — `CreateIssueBelow`, `CreateIssueAbove`, `IndentAsChild`, `UnindentAsSibling`, `GetInheritedParentInfo`, `AcceptEditAsync`
- `src/Homespun.Shared/Requests/IssueRequests.cs` — `CreateIssueRequest` fields sent to the API
- `tests/Homespun.Tests/Services/KeyboardNavigationServiceTests.cs` — existing test patterns

## Diagram Legend

All diagrams use a text-based notation to represent the task graph:

| Symbol | Meaning |
|--------|---------|
| `o` | An issue node |
| `\|` | A vertical edge connecting a child (above) to a sibling or parent (below) |
| `+--o` | A right-angle connector from a child chain to a parent node |
| `x--o` | Same as `+--o` but indicates the new issue is being inserted into this edge |
| blank line | A gap between unrelated issues (no parent-child relationship) |

Children render **above** their parents in the task graph. Lane 0 is the leftmost column (leaf/child nodes); higher lanes are further right (parent/root nodes). "Above" and "below" in this document always refer to **visual position** on screen (render order), not hierarchy level.

## Keyboard Controls

| Key | Effect | Mode Transition |
|-----|--------|-----------------|
| `o` | Create new issue **below** the selected issue | `Viewing` → `CreatingNew` |
| `Shift+O` | Create new issue **above** the selected issue | `Viewing` → `CreatingNew` |
| `TAB` | Make the new issue a **parent** of the reference issue | Stays in `CreatingNew` |
| `Shift+TAB` | Make the new issue a **child** of the reference issue | Stays in `CreatingNew` |
| `Enter` | Accept the new issue (calls `AcceptEditAsync`) | `CreatingNew` → `Viewing` |
| `Escape` | Cancel and discard the pending issue | `CreatingNew` → `Viewing` |

The **reference issue** is the issue that was selected when `o` or `Shift+O` was pressed.

## Code Properties Quick-Reference

When a new issue is created, the `PendingNewIssue` properties determine what gets sent to the API in `CreateIssueRequest`:

| Action | `PendingChildId` | `PendingParentId` | `InheritedParentIssueId` | API: `ChildIssueId` | API: `ParentIssueId` | API: `ParentSortOrder` |
|--------|-------------------|--------------------|--------------------------|-----------------------|-----------------------|------------------------|
| Default (no TAB) | `null` | `null` | From `GetInheritedParentInfo` | `null` | = `InheritedParentIssueId` | = `InheritedParentSortOrder` |
| TAB | = `ReferenceIssueId` | `null` | `null` (cleared) | = `PendingChildId` | `null` | `null` |
| Shift+TAB | `null` | = `ReferenceIssueId` | `null` (cleared) | `null` | = `PendingParentId` | `null` |

**`GetInheritedParentInfo` logic:** Looks up the reference issue's parent. If the reference issue has a parent, the new issue inherits that parent (sibling creation). If the reference issue has no parent, returns `null` for both fields (orphan/standalone creation).

All issues in this document are drawn as "series" issues. For "parallel" issues the same parent/child rules apply — only the sort order computation differs.

---

## 1. Default (Sibling) — no TAB / Shift+TAB

New issue is created at the same hierarchy level as the reference issue. The new issue inherits the reference issue's parent (if any) via `GetInheritedParentInfo`.

### 1.1 Between two children

#### Location

```
o Child issue 1
| <-- insert here: `o` below Child issue 1, or `Shift+O` above Child issue 2
o Child issue 2
|
+--o Parent issue
```

#### Expected result

```
o Child issue 1
|
o New issue
|
o Child issue 2
|
+--o Parent issue
```

#### Assertions

- `PendingNewIssue.InheritedParentIssueId` = "Parent issue"
- `CreateIssueRequest.ParentIssueId` = "Parent issue"
- `CreateIssueRequest.ParentSortOrder` = midpoint between Child issue 1 and Child issue 2 sort orders

### 1.2 Between a child and parent

#### Location

```
o Child issue 1
|
o Child issue 2
| <-- insert here: `o` below Child issue 2, or `Shift+O` above Parent issue
x--o Parent issue
```

#### Expected result

```
o Child issue 1
|
o Child issue 2
|
o New issue
|
x--o Parent issue
```

#### Assertions

- `PendingNewIssue.InheritedParentIssueId` = "Parent issue"
- `CreateIssueRequest.ParentIssueId` = "Parent issue"
- `CreateIssueRequest.ParentSortOrder` = computed sort order after Child issue 2

### 1.3 Between an orphan and a child hierarchy

This depends on whether the new issue is created below the orphan or above the child.

#### Location

```
o Next issue (orphan, no parent)
  <-- insert here: `Shift+O` above Child issue 1, or `o` below Next issue
o Child issue 1
|
o Child issue 2
|
x--o Parent issue
```

#### Expected result — `Shift+O` above Child issue 1

The reference issue is Child issue 1, which has Parent issue as its parent. `GetInheritedParentInfo` returns Parent issue. The new issue becomes a sibling of Child issue 1 under Parent issue.

```
o Next issue (orphan, no parent)

o New issue
|
o Child issue 1
|
o Child issue 2
|
x--o Parent issue
```

**Assertions:**
- `PendingNewIssue.InheritedParentIssueId` = "Parent issue"
- `CreateIssueRequest.ParentIssueId` = "Parent issue"

#### Expected result — `o` below Next issue

The reference issue is Next issue, which has no parent. `GetInheritedParentInfo` returns `null`. The new issue is an orphan.

```
o Next issue (orphan, no parent)

o New issue (orphan)

o Child issue 1
|
o Child issue 2
|
x--o Parent issue
```

**Assertions:**
- `PendingNewIssue.InheritedParentIssueId` = `null`
- `CreateIssueRequest.ParentIssueId` = `null`

### 1.4 After a parent and before an orphan

The reference issue determines the behaviour. The parent issue has no parent of its own, so whether inserting below it or above the orphan, the new issue is an orphan.

#### Location

```
o Child issue 1
|
o Child issue 2
|
x--o Parent issue
      <-- insert here: `o` below Parent issue, or `Shift+O` above Next issue
o Next issue (orphan, no parent)
```

#### Expected result

```
o Child issue 1
|
o Child issue 2
|
x--o Parent issue

o New issue (orphan)

o Next issue (orphan, no parent)
```

#### Assertions

- `PendingNewIssue.InheritedParentIssueId` = `null` (Parent issue has no parent; Next issue has no parent)
- `CreateIssueRequest.ParentIssueId` = `null`

### 1.5 Between two adjacent hierarchies

#### Location

```
o Child issue 1.1
|
o Child issue 1.2
|
x--o Parent issue 1
  <-- insert here: `o` below Parent issue 1, or `Shift+O` above Child issue 2.1
o Child issue 2.1
|
+--o Parent issue 2
```

#### Expected result — `o` below Parent issue 1

The reference issue is Parent issue 1. Parent issue 1 has no parent, so `GetInheritedParentInfo` returns `null`. The new issue is an orphan.

```
o Child issue 1.1
|
o Child issue 1.2
|
x--o Parent issue 1

o New issue (orphan)

o Child issue 2.1
|
+--o Parent issue 2
```

**Assertions:**
- `PendingNewIssue.InheritedParentIssueId` = `null`
- `CreateIssueRequest.ParentIssueId` = `null`

#### Expected result — `Shift+O` above Child issue 2.1

The reference issue is Child issue 2.1. Its parent is Parent issue 2, so `GetInheritedParentInfo` returns Parent issue 2. The new issue becomes a sibling of Child issue 2.1 under Parent issue 2.

```
o Child issue 1.1
|
o Child issue 1.2
|
x--o Parent issue 1

o New issue
|
o Child issue 2.1
|
+--o Parent issue 2
```

**Assertions:**
- `PendingNewIssue.InheritedParentIssueId` = "Parent issue 2"
- `CreateIssueRequest.ParentIssueId` = "Parent issue 2"

---

## 2. TAB (Become Parent) — new issue becomes parent of reference

Pressing `TAB` after `o`/`Shift+O` sets `PendingChildId = ReferenceIssueId`. The new issue becomes a **parent** of the reference issue. All previously inherited parent info is cleared.

### 2.1 Between two children

#### Location

```
o Child issue 1
|
o Child issue 2
|     <-- insert here: `o` below Child issue 2, or `Shift+O` above Child issue 3
o Child issue 3
|
+--o Parent issue
```

#### Expected result

The new issue becomes the parent of the reference issue. This breaks the relationship between Parent issue and all children above the insertion point (tree behaviour).

```
o Child issue 1
|
o Child issue 2
|
+--o New issue

o Child issue 3
|
+--o Parent issue
```

#### Assertions

- `PendingNewIssue.PendingChildId` = reference issue ID (Child issue 2 or Child issue 3)
- `CreateIssueRequest.ChildIssueId` = reference issue ID
- `CreateIssueRequest.ParentIssueId` = `null`

### 2.2 Between a child and parent

#### Location

```
o Child issue 1
|
o Child issue 2
|     <-- insert here: `o` below Child issue 2, or `Shift+O` above Parent issue
x--o Parent issue
```

#### Expected result

The new issue becomes parent of the reference issue. Parent issue loses its children; the new issue inherits them.

```
o Child issue 1
|
o Child issue 2
|
+--o New issue

o Parent issue
```

#### Assertions

- `PendingNewIssue.PendingChildId` = reference issue ID
- `CreateIssueRequest.ChildIssueId` = reference issue ID
- `CreateIssueRequest.ParentIssueId` = `null`

### 2.3 Between an orphan and a child hierarchy

#### Location

```
o Next issue (orphan, no parent)
  <-- insert here: `Shift+O` above Child issue 1, or `o` below Next issue
o Child issue 1
|
o Child issue 2
|
x--o Parent issue
```

#### Expected result — `Shift+O` above Child issue 1 + TAB

**Blocked (no-op).** `[NOT YET IMPLEMENTED]` Creating a parent above a root-level child makes no sense — the new issue would be inserted between two unrelated hierarchies with no children of its own in that context.

#### Expected result — `o` below Next issue + TAB

The new issue becomes parent of Next issue.

```
o Next issue (orphan, no parent)
|
+--o New issue

o Child issue 1
|
o Child issue 2
|
x--o Parent issue
```

**Assertions:**
- `PendingNewIssue.PendingChildId` = "Next issue"
- `CreateIssueRequest.ChildIssueId` = "Next issue"

### 2.4 After a parent and before an orphan

#### Location

```
o Child issue 1
|
o Child issue 2
|
x--o Parent issue
      <-- insert here: `o` below Parent issue, or `Shift+O` above Next issue
o Next issue (orphan, no parent)
```

#### Expected result — `o` below Parent issue + TAB

The new issue becomes parent of Parent issue.

```
o Child issue 1
|
o Child issue 2
|
x--o Parent issue
   |
   +--o New issue (parent of Parent issue)

o Next issue (orphan, no parent)
```

**Assertions:**
- `PendingNewIssue.PendingChildId` = "Parent issue"
- `CreateIssueRequest.ChildIssueId` = "Parent issue"

#### Expected result — `Shift+O` above Next issue + TAB

**Blocked (no-op).** `[NOT YET IMPLEMENTED]` Creating a parent above a root-level orphan with no children makes no structural sense.

### 2.5 Between two adjacent hierarchies

#### Location

```
o Child issue 1.1
|
o Child issue 1.2
|
x--o Parent issue 1
  <-- insert here: `o` below Parent issue 1, or `Shift+O` above Child issue 2.1
o Child issue 2.1
|
+--o Parent issue 2
```

#### Expected result — `o` below Parent issue 1 + TAB

The new issue becomes parent of Parent issue 1.

```
o Child issue 1.1
|
o Child issue 1.2
|
x--o Parent issue 1
   |
   +--o New issue (parent of Parent issue 1)

o Child issue 2.1
|
+--o Parent issue 2
```

**Assertions:**
- `PendingNewIssue.PendingChildId` = "Parent issue 1"
- `CreateIssueRequest.ChildIssueId` = "Parent issue 1"

#### Expected result — `Shift+O` above Child issue 2.1 + TAB

**Blocked (no-op).** `[NOT YET IMPLEMENTED]` The new issue would be inserted between two unrelated hierarchies. Child issue 2.1 already has a parent (Parent issue 2), and the new issue would have no children in this context.

---

## 3. Shift+TAB (Become Child) — new issue becomes child of reference

Pressing `Shift+TAB` after `o`/`Shift+O` sets `PendingParentId = ReferenceIssueId`. The new issue becomes a **child** of the reference issue. All previously inherited parent info is cleared.

### 3.1 Between two children

#### Location

```
o Child issue 1
|
o Child issue 2
|     <-- insert here: `o` below Child issue 2, or `Shift+O` above Child issue 3
o Child issue 3
|
+--o Parent issue
```

#### Expected result

The new issue becomes a child of the reference issue.

```
o Child issue 1
|
o Child issue 2
|
o New issue
|
+--o Child issue 3
   |
   +--o Parent issue
```

#### Assertions

- `PendingNewIssue.PendingParentId` = reference issue ID (Child issue 2 or Child issue 3)
- `CreateIssueRequest.ParentIssueId` = reference issue ID
- `CreateIssueRequest.ChildIssueId` = `null`

### 3.2 Between a child and parent

#### Location

```
o Child issue 1
|
o Child issue 2
|     <-- insert here: `o` below Child issue 2, or `Shift+O` above Parent issue
x--o Parent issue
```

#### Expected result

**Blocked (no-op).** `[NOT YET IMPLEMENTED]` The parent cannot have a child two levels down — the new issue would need to become a grandchild, which is not supported by a single Shift+TAB operation.

### 3.3 Between an orphan and a child hierarchy

#### Location

```
o Next issue (orphan, no parent)
  <-- insert here: `Shift+O` above Child issue 1, or `o` below Next issue
o Child issue 1
|
o Child issue 2
|
x--o Parent issue
```

#### Expected result — `Shift+O` above Child issue 1 + Shift+TAB

The new issue becomes a child of Child issue 1.

```
o Next issue (orphan, no parent)

o    New issue (child of Child issue 1)
|
+--o Child issue 1
   |
   o Child issue 2
   |
   x--o Parent issue
```

**Assertions:**
- `PendingNewIssue.PendingParentId` = "Child issue 1"
- `CreateIssueRequest.ParentIssueId` = "Child issue 1"

#### Expected result — `o` below Next issue + Shift+TAB

**Blocked (no-op).** `[NOT YET IMPLEMENTED]` It doesn't make sense in context to the orphan Next issue — the new issue would become a child of an orphan, but the visual position (between two unrelated groups) is ambiguous.

### 3.4 After a parent and before an orphan

#### Location

```
o Child issue 1
|
o Child issue 2
|
x--o Parent issue
      <-- insert here: `o` below Parent issue, or `Shift+O` above Next issue
o Next issue (orphan, no parent)
```

#### Expected result — `o` below Parent issue + Shift+TAB

**Blocked (no-op).** `[NOT YET IMPLEMENTED]` There could be many levels to travel down from Parent issue.

#### Expected result — `Shift+O` above Next issue + Shift+TAB

The new issue becomes a child of Next issue.

```
o Child issue 1
|
o Child issue 2
|
x--o Parent issue

o New issue (child of Next issue)
|
+--o Next issue
```

**Assertions:**
- `PendingNewIssue.PendingParentId` = "Next issue"
- `CreateIssueRequest.ParentIssueId` = "Next issue"

### 3.5 Between two adjacent hierarchies

#### Location

```
o Child issue 1.1
|
o Child issue 1.2
|
x--o Parent issue 1
  <-- insert here: `o` below Parent issue 1, or `Shift+O` above Child issue 2.1
o Child issue 2.1
|
+--o Parent issue 2
```

#### Expected result — `o` below Parent issue 1 + Shift+TAB

**Blocked (no-op).** `[NOT YET IMPLEMENTED]` There could be many levels to travel down from Parent issue 1 — the system cannot determine the correct depth.

#### Expected result — `Shift+O` above Child issue 2.1 + Shift+TAB

The new issue becomes a child of Child issue 2.1.

```
o Child issue 1.1
|
o Child issue 1.2
|
x--o Parent issue 1

o New issue (child of Child issue 2.1)
|
+--o Child issue 2.1
   |
   +--o Parent issue 2
```

**Assertions:**
- `PendingNewIssue.PendingParentId` = "Child issue 2.1"
- `CreateIssueRequest.ParentIssueId` = "Child issue 2.1"

---

## 4. Edge Cases

| Scenario | Expected Behaviour |
|----------|-------------------|
| Empty graph (no issues) | `CreateIssueBelow`/`CreateIssueAbove` returns immediately (`SelectedIndex < 0`) — no issue created |
| Single issue in graph | Normal behaviour — `o` creates at index 1 (end), `Shift+O` at index 0 (start) |
| TAB then Shift+TAB (switching) | Mutually exclusive — Shift+TAB overwrites TAB state. No way to return to default sibling mode without `Escape` + restart |
| Pressing TAB twice | Idempotent — `PendingChildId` is set to the same reference issue ID again |
| Escape during creation | Clears `PendingNewIssue`, returns to `Viewing` mode, no API call |
| Empty title then Enter | `AcceptEditAsync` returns without creating, stays in `CreatingNew` mode |
| Series vs Parallel parents | Same parent/child rules apply; only difference is sort order computation for sibling creation |

---

## Summary of Blocked Scenarios

All "blocked" scenarios mean the key press is a **no-op** — nothing happens. The code does not currently implement blocking validation in `IndentAsChild()` or `UnindentAsSibling()`. These are marked `[NOT YET IMPLEMENTED]` throughout this document. Test writers should write TDD-style failing tests for these scenarios.

| Section | Scenario | Reason |
|---------|----------|--------|
| 2.3 | `Shift+O` above root child + TAB | New issue would have no children |
| 2.4 | `Shift+O` above orphan + TAB | New issue would have no children |
| 2.5 | `Shift+O` above Child issue 2.1 + TAB | New issue would be between unrelated hierarchies with no children |
| 3.2 | Both directions + Shift+TAB | Cannot create grandchild in one operation |
| 3.3 | `o` below orphan + Shift+TAB | Ambiguous context between unrelated groups |
| 3.4 | `o` below parent + Shift+TAB | Many levels to travel down |
| 3.5 | `o` below Parent issue 1 + Shift+TAB | Many levels to travel down |
