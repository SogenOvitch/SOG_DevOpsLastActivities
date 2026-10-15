# SOG_DevOpsLastActivities

WPF tool (.NET 8, dark theme) for answering "what did I do on Azure DevOps last week?".

Pick a configuration, open a session tab on it, and use that session's features. The first feature,
**Last activities**, lists every work item you did something on between two dates. Leave From empty
for a week ago, and To empty for today.

## Configurations

Each configuration is one JSON file in `%APPDATA%\SOG_DevOpsLastActivities\Configs`. You can create
it from the tool (New / Edit) or write it by hand and press Refresh:

```json
{
  "Name": "Client X",
  "Organization": "contoso",
  "Project": "ERP Rollout",
  "Pat": "…"
}
```

- `Organization` is the name in `dev.azure.com/<name>`. A full URL also works, which covers the old
  `*.visualstudio.com` addresses and Azure DevOps Server collections.
- The PAT is stored in plain text. It needs at least the **Work Items (Read)** scope.

## How "my activities" are found

Azure DevOps has no "my history" call, so the scan runs in two passes.

1. **Find candidate work items** in the project, from four sources:
   - WIQL `[System.ChangedBy] EVER @Me`, for items changed since From.
   - The "My activity" list (`_apis/work/accountmyworkrecentactivity`): items you viewed, edited,
     deleted or restored in about the last 30 days.
   - WIQL `@RecentMentions` and `@Follows`. Microsoft documents these macros as portal-only; if the
     API refuses them, the scan carries on without them and shows a warning.
2. **Read each candidate's history** (`/updates`) and comments (`/comments?$expand=reactions`), and
   keep only what *you* did inside the range.

| Action | Source | Dated |
|---|---|---|
| Created | Revision 1 is yours | exactly |
| State changed, other edits | Your revisions: fields changed, old → new | exactly |
| Comment added / edited | Comments API, with the History field as fallback | exactly |
| Attachment, link (work item, PR, commit, branch) | Relations added or removed in your revisions | exactly |
| Like / reaction | `isCurrentUserEngaged` on the comment's reactions | **window**, see below |
| Mentioned (off by default) | Someone else's comment mentioning you | exactly |
| Deleted, restored, viewed (viewed off by default) | "My activity" list, latest activity only | exactly |

**Reactions have no date.** The tool shows them with a window instead:

- **Start:** when the comment was posted.
- **End:** the end of the day of your latest recorded activity on that work item. Reacting means
  having the item open, and opening it counts as a visit.
- **Kept** when the window overlaps the range.
- **No recorded activity on the item:** the like is kept only if the comment was posted inside the
  range.

**About 30 days of history** is all the "My activity" list holds. If From is further back than that,
viewed, deleted and restored items and likes can be missing. Changes and comments are always complete.

## Results

- **Grid:** one row per work item, sorted by last activity, with who it is currently assigned to.
  Select a row to see its timeline, newest first; double-click it (or its ID) to open the work item
  in the browser.
- **Action filters:** one checkbox per kind of action. A work item with no actions left is hidden.
- **Work item filter:** untick **Assigned to me** to hide the work items currently assigned to you,
  and keep what you did on everyone else's. "You" is matched by identity, not by display name.
- **Copy summary:** one line per work item, ready to paste into a report.
- **Export CSV:** one line per action, with the assignee. It uses the system's list separator and a
  UTF-8 BOM, so Excel opens it correctly in a French locale.

## Adding a feature

A feature is a `FeatureViewModel` subclass, wired up in two places:

1. Add it to the list in `SessionViewModel`.
2. Add a `DataTemplate` in `App.xaml` that maps it to its view.

It reaches Azure DevOps through `Session.Client` (`DevOpsClient`, plain `HttpClient` with the PAT).
`Session.GetCurrentUserAsync` tells it who the PAT belongs to.

## Theme

`Themes/Dark.xaml` is the shared Dark theme, plus styles this tool needed that it lacked:

- Hyperlink
- TabControl / TabItem, and a `TabCloseButton` style for closing tabs
- ProgressBar
- DatePicker and Calendar, fully re-templated. A DatePicker's `Tag` is shown dimmed while it is empty.

All of these are generic and can be copied back into the shared theme.

## License

MIT, see [LICENSE](LICENSE).
