# YUCP Design Guidelines

Every YUCP inspector and window follows this document, in every YUCP package. It adapts Apple's
[Human Interface Guidelines](https://developer.apple.com/design/human-interface-guidelines) (HIG)
and Apple's WWDC design sessions to Unity inspectors. Each rule cites the page or session it comes
from. Where an AppKit-specific convention conflicts with how Unity's own inspectors look, we follow
Unity and say so. YUCP inspectors sit next to Unity's built-in components in the same window, so
consistency with that platform comes first.

**The look comes from YUCP DevTools.** Its windows (the Package Exporter above all) are the
reference design: flat sections on a dark window, tinted teal for the primary action and the
selection, tinted messages, pill-shaped switchers. Inspectors use the same look at Unity's
inspector density, so they still line up with Unity's own components.

The design system is its own package, `com.yucp.ui`, with no dependencies. Every YUCP package
depends on it:
- `Editor/YUCPInspector.cs` builds inspectors; `Editor/YUCPUIToolkitHelper.cs` holds the building
  blocks (styles, sections, buttons, messages, disclosures) for inspectors and windows.
- `Editor/Styles/YUCPDesignSystem.uss`: the tokens and the DevTools controls and window layout,
  rule for rule as the Package Exporter had them. `YUCPControls.uss`: controls DevTools didn't
  have (groups, badges, segmented controls, tabs, lists). `YUCPInspector.uss`: inspector layout and
  the inspector sizes of the DevTools controls, scoped under `.yucp-inspector`.
- **The base is the window look; inspectors are the variant.** Shared rules use one class, exactly
  as DevTools wrote them, so a window's own stylesheet still overrides them the way it always did.
  Inspector sizes come from two-class rules (`.yucp-inspector .yucp-button`) that only exist
  inside inspectors. Never add a two-class rule for a window size: it would out-rank the window's
  own rules and change its look.

Reference implementations:
- `com.yucp.components/Editor/Components/Header/FollowerDataEditor.cs` (simple inspector).
- `com.yucp.components/Editor/Header/LiveGripEditors.cs` (lists, segmented controls, Scene view editing).
- `com.yucp.devtools/Editor/PackageExporter` (window).

---

## Sources

| Short name | Source |
|---|---|
| HIG Layout | https://developer.apple.com/design/human-interface-guidelines/layout |
| HIG Boxes | https://developer.apple.com/design/human-interface-guidelines/boxes |
| HIG Typography | https://developer.apple.com/design/human-interface-guidelines/typography |
| HIG Color | https://developer.apple.com/design/human-interface-guidelines/color |
| HIG Accessibility | https://developer.apple.com/design/human-interface-guidelines/accessibility |
| HIG Writing | https://developer.apple.com/design/human-interface-guidelines/writing |
| HIG Motion | https://developer.apple.com/design/human-interface-guidelines/motion |
| HIG Buttons | https://developer.apple.com/design/human-interface-guidelines/buttons |
| HIG Toggles | https://developer.apple.com/design/human-interface-guidelines/toggles |
| HIG Segmented controls | https://developer.apple.com/design/human-interface-guidelines/segmented-controls |
| HIG Pop-up buttons | https://developer.apple.com/design/human-interface-guidelines/pop-up-buttons |
| HIG Sliders | https://developer.apple.com/design/human-interface-guidelines/sliders |
| HIG Text fields | https://developer.apple.com/design/human-interface-guidelines/text-fields |
| HIG Labels | https://developer.apple.com/design/human-interface-guidelines/labels |
| HIG Lists and tables | https://developer.apple.com/design/human-interface-guidelines/lists-and-tables |
| HIG Disclosure controls | https://developer.apple.com/design/human-interface-guidelines/disclosure-controls |
| HIG Feedback | https://developer.apple.com/design/human-interface-guidelines/feedback |
| HIG Alerts | https://developer.apple.com/design/human-interface-guidelines/alerts |
| HIG Progress indicators | https://developer.apple.com/design/human-interface-guidelines/progress-indicators |
| HIG Entering data | https://developer.apple.com/design/human-interface-guidelines/entering-data |
| HIG Settings | https://developer.apple.com/design/human-interface-guidelines/settings |
| HIG Modality | https://developer.apple.com/design/human-interface-guidelines/modality |
| HIG Panels | https://developer.apple.com/design/human-interface-guidelines/panels |
| HIG Undo and redo | https://developer.apple.com/design/human-interface-guidelines/undo-and-redo |
| HIG Icons | https://developer.apple.com/design/human-interface-guidelines/icons |
| WWDC17 802 | "Essential Design Principles", https://developer.apple.com/videos/play/wwdc2017/802/ |
| WWDC22 10037 | "Writing for interfaces", https://developer.apple.com/videos/play/wwdc2022/10037/ |
| WWDC25 404 | "Make a big impact with small writing changes", https://developer.apple.com/videos/play/wwdc2025/404/ |

---

## 1. Principles

These are the ideas every other rule in this document applies.

- **An inspector shows the selected item.** "An inspector displays the details of the currently
  selected item, automatically updating its contents when the item changes" (HIG Panels). It
  updates as the data changes. It never rebuilds itself on a timer.
- **Hierarchy.** "Order content by relative importance" and "place the most important items near
  the top" (HIG Layout). The settings people change most come first.
- **Grouping and proximity.** "Grouping helps people to understand the relationships between
  elements" and "The closer a control is to that object, the more we assume there to be a
  connection between the two" (WWDC17 802). A control sits next to what it affects, and a message
  next to what it is about.
- **Progressive disclosure.** It "gradually eases people from the simple to the more complex"
  (WWDC17 802). Show what most people need; hide the rest "until they're relevant" (HIG Disclosure
  controls).
- **Consistency.** "Representing similar design features in similar ways" (WWDC17 802). The same
  setting has the same name, control and place in every component.
- **Feedback.** "Feedback helps people know what's happening, discover what they can do next,
  understand the results of actions, and avoid mistakes" (HIG Feedback).
- **Fewer settings.** "Minimize the number of settings you offer" and "provide default settings
  that give the best experience to the largest number of people" (HIG Settings).

---

## 2. Anatomy of an inspector

Every inspector uses the same order, top to bottom:

1. **Header.** A package adds its own header to all of its inspectors through
   `YUCPInspector.Created`; com.yucp.components adds the YUCP badge and the component name, and
   puts the beta and missing-VRCFury notices in the status area. Never add them again in an editor.
2. **Status.** Component-level problems, such as being outside an avatar or a missing dependency,
   go here (`ui.Status`). This area is empty when everything is fine.
3. **Primary sections.** What the component acts on and how. Usually one to three.
4. **Secondary sections.** Optional behaviour.
5. **Avatar menu / Toggle.** Menu, toggle and parameter settings, when the component has any.
6. **Advanced Options.** A single disclosure, collapsed by default and always last. It holds
   rarely changed settings, logging, credits and the last build (`ui.Advanced`, `ui.BuildSummary`).

Rules:

- **No overview or summary card above the inputs.** It repeats them. WWDC25 404: "Resist the urge
  to fill empty space by saying the same thing in different ways."
- **No documentation button.** Put `[HelpURL]` on the component class instead; Unity's own "?"
  button in the header opens it. HIG Buttons: "Include no more than one help button per window."
- **No credit banner at the top.** Credits are a caption in Advanced Options.
- **Simple and Advanced views use tabs (`ui.Tabs`), not a segmented control.** HIG Segmented
  controls: "Use a tab view in the main window area — instead of a segmented control — for view
  switching."
- **A setting shown on both tabs keeps the same name and control on both.** WWDC22 10037: "Make
  a list of commonly used terms."

---

## 3. Layout

### Sections and groups
- **A section is a compartment: a card that holds related settings** (`ui.Section`). HIG Boxes:
  "A box creates a visually distinct group of logically related information and components."
- **In inspectors the title sits above the card,** lined up with the labels inside it. HIG Boxes:
  "By default, macOS displays a box's title above it." This is how System Settings heads each
  group. Advanced Options looks the same: its disclosure title above, its settings in a card.
- **Windows keep the DevTools sections** (title inside the card), as the Package Exporter has them.
- **Never put a box inside a box.** HIG Boxes: "adding nested boxes to define subgroups can make
  your interface feel busy and constrained" and "Consider using padding and alignment to
  communicate additional grouping within a box". Inside a section, use `ui.Group` (a title and
  spacing) or `ui.Indent`.
- **A section needs at least two related settings.** One lone setting joins the nearest related
  section or Advanced Options.
- **Use a section description only when it adds something the title doesn't.** "Configure X"
  under a title "X" is filler (WWDC25 404). Most sections have no description.

### Alignment and indentation
- **Everything shares one leading edge:** section titles, group titles, labels, checkboxes,
  captions, messages and lists all start where Unity's field labels start, 3 px in from the panel.
  The disclosure triangle of Advanced Options points in from that same edge. HIG Layout: "Align
  elements to make them easier to scan."
- **Every field label sits on the same column.** Unity's aligned field label column is used by
  `PropertyField`, `ui.Segmented` and `ui.InfoRow`. HIG Layout: "Align elements to make them easier
  to scan." Never set label widths by hand.
- **Dependent settings are indented under the control that enables them, and appear only while it
  is on** (`ui.Indent` + `ui.ShowWhen`). HIG Layout: "they perceive indented items as subordinate
  to the item they follow." HIG Toggles: "By using alignment … and indentation, you can show
  dependencies."
- **Never squeeze two labelled fields side by side.** HIG Text fields: "Stack multiple text fields
  vertically when possible, and use consistent widths."

### Spacing
- **Use the 4 px grid tokens:** `--yucp-spacing-xs/sm/md/lg/xl/xxl` = 4/8/12/16/20/24. Sections
  have 12 px padding in inspectors and 16 px in windows. HIG Accessibility: "about 12 points of
  padding around elements that include a bezel".
- **Never use `AddSpacing` or inline margins to separate things.** Grouping does that.

### Windows
- **A window root gets `yucp-window`, then `LoadDesignSystemStyles`, then the window's own
  stylesheet,** in that order. Windows keep the DevTools look (36 px buttons, 16 px section
  titles and padding) and stay dark in either editor skin; inspectors follow the skin.
- **Use the shared window layout** instead of styling panes by hand: `yucp-top-bar`,
  `yucp-content-container` with `yucp-left-pane` and `yucp-right-pane`, `yucp-panel`,
  `yucp-bottom-bar`, and `yucp-form-row` / `yucp-form-label` / `yucp-form-field` for labelled
  fields. HIG Layout: "Use consistent spacing."
- **Narrow windows adapt; they don't animate.** The window sets `yucp-window-medium` or
  `yucp-window-narrow` from its width, and layout snaps to the new arrangement. HIG Layout: "Design a
  layout that adapts gracefully and consistently."

### Progressive disclosure
- **Have one Advanced Options disclosure.** Other disclosures are allowed only when hiding a large,
  rarely used block inside a section, and never more than one level deep. HIG Disclosure controls:
  "Use no more than one disclosure button in a single view" and "Place controls that people are
  most likely to use at the top of the disclosure hierarchy … with more advanced functionality
  hidden by default."
- **Label a disclosure by what it hides** ("Advanced Options", "Smart Detection"), not "More".
  HIG Disclosure controls: "Make sure your labels indicate what is disclosed or hidden."

---

## 4. Controls: which to use

| Need | Use | Source |
|---|---|---|
| On/off setting | Checkbox: the box on the leading edge, its title trailing (`ui.Field` does this for every bool; `YUCPInspector.Checkbox` for one that isn't bound). Settings that depend on it indent under it. No switches. | HIG Toggles: "To present a single setting that can be on or off, prefer a checkbox"; "Typically, a checkbox includes a title on its trailing side"; "By using alignment — generally along the leading edge of the checkboxes — and indentation, you can show dependencies." |
| 2–5 closely related, mutually exclusive choices | `ui.Segmented` with noun labels; every segment gets a tooltip | HIG Segmented controls: "closely related choices that affect an object", "Use nouns or noun phrases for segment labels", "Aim for no more than about five to seven segments"; macOS: "If your app includes tooltips, provide one for each segment in a segmented control." |
| Longer or open-ended list of choices | Pop-up (enum `PropertyField`, `DropdownField`) with a useful default | HIG Pop-up buttons: "a flat list of mutually exclusive options", "Provide a useful default selection." |
| A value in a fixed range | Slider with its number field (`[Range]` on the field) | HIG Sliders: "people may appreciate seeing the exact slider value and having the ability to enter a specific value in a text field." |
| Free text (names, paths) | Text field, only when choices aren't possible | HIG Entering data: "When possible, offer choices instead of requiring text entry." |
| A list people add to, remove from or reorder | `ui.List`: selectable rows, + and − under it, drag to reorder; edit the selected item below the list. Unity's own array fields are restyled to match: no size field, + and − beneath the list on the leading edge. | HIG Lists and tables: selection "persistently highlights the selected row"; "People appreciate being able to reorder a list." HIG Buttons (square buttons): they "contain symbols or icons — not text" and "appear in close proximity to their associated view — usually within or beneath it." |
| A read-only value | `ui.InfoRow` (selectable text) | HIG Labels: "Make useful label text selectable." |
| An action | `ui.Button` (verb label) | HIG Buttons |
| An action as an icon | `YUCPInspector.IconButton` with a familiar Unity icon and a tooltip | HIG Icons: "familiar visual metaphors" |

### Buttons
- **Label each button with a verb in Title Case** ("Generate Preview", "Add Grip Point"). Never
  "OK", "Yes" or "Click here". HIG Buttons: "Ensure that each button clearly communicates its
  purpose"; WWDC22 10037: buttons should "name the outcome rather than say 'Yes' or 'No'".
- **At most one Primary button is visible at a time,** for the most likely action. HIG Buttons:
  "Keep the number of prominent buttons to one or two per view." YUCP uses one.
- **Destructive actions use Danger and are never Primary.** HIG Buttons: "Don't assign the primary
  role to a button that performs a destructive action."
- **The DevTools looks, decided by the variant:** neutral grey (Secondary, the default), tinted
  teal with teal text (Primary, like the exporter's Export button), and grey that turns red on hover
  (Danger). Ghost is a text-only button. HIG Buttons: "a primary button uses an app's accent color,
  whereas a destructive button uses the system red color."
- **Never override a button's color or height.** The variant decides its color; size comes from
  the stylesheet. HIG Buttons: "Use style — not size — to visually distinguish the preferred
  choice." Buttons are 24 px tall in inspectors and 36 px in windows, above the macOS 20 pt
  minimum (HIG Accessibility).
- **Add an ellipsis only when the button opens a window or dialog** ("Install…"). HIG Buttons.
- **Place a button next to what it affects** (WWDC17 802, proximity). Related buttons share a
  `ui.ButtonRow`, most important first.

### Edit modes in the Scene view
- **Spatial things are edited by direct manipulation.** An explicit Edit/Done button enters the
  edit mode, like Unity's Edit Collider (`EditCollider` icon). HIG Modality: "Always give people an
  obvious way to dismiss a modal view." WWDC17 802: "Mapping is about designing controls to resemble
  the objects that they affect."
- **While editing:**
  - `Tools.hidden = true`.
  - Points are click-to-select dots, and only the selected one gets a gizmo.
  - Handles follow Unity's Move/Rotate tools (W/E).
  - Esc or selecting something else ends the mode.
  - `YUCPSceneOverlay.Draw` shows a short instruction.
  - Every change is undoable. HIG Undo and redo: "People generally expect to undo every action."
- **Draw scene previews from `SceneView.duringSceneGui`, not `DrawGizmo`,** so they show with
  gizmos turned off.

---

## 5. Feedback and status

- **Component-level problems go in the status area (`ui.Status`)**: where the component is placed,
  a missing package, beta. **A problem with one setting goes directly under that setting**
  (`ui.Messages`): "Assign the body mesh." belongs under Body Mesh, not at the top. HIG Feedback: "When status feedback is available near
  the items it describes, people get important information without having to take action or leave
  their current context."
- **Use the severity that matches the consequence:**
  - **Error:** the build will fail or the component won't work.
  - **Warning:** it works, but probably not as intended.
  - **Info:** only when there is something the person should know or do.
  - **Success:** only to confirm an action they just took.
- **A message is the DevTools tinted box with white text** (`CreateHelpBox`, `ui.Messages`): blue
  tint for info, amber for warning, red for error, teal for success. The tint tells the severity at
  a glance and the words say it, so color is never the only cue (HIG Accessibility: "Convey
  information with more than color alone"). The text is white, not the tint's color: colored text
  on its own tint measured 2.4–3.9:1, below the 4.5:1 HIG Accessibility asks for; white is 9:1 or
  more.
- **Never show a help box after every field.** Explanations belong in the field's `[Tooltip]`.
- **Use dialogs only to confirm a destructive action that can't be undone.** Never for
  information, never "Cache Cleared"-style results. HIG Alerts: "Avoid using an alert merely to
  provide information" and "Avoid displaying alerts for common, undoable actions." Results show in
  the inspector.
- **Write error text that tells people what to do.** HIG Writing: "Show errors right next to the
  field, and instruct people how to enter the information correctly." For example: "Assign the body
  mesh." not "Error: body mesh missing!"
- **Progress.** Use a determinate bar with the real fraction and a specific description. Never a
  fixed 0.5. HIG Progress indicators: "When possible, use a determinate progress indicator" and
  "Avoid vague terms like loading."
- **The last build is passive status, shown in Advanced Options** (`ui.BuildSummary`). HIG
  Feedback: "display status information in a passive way so that people can view it when they need
  it."
- **Refresh, don't poll.** An inspector updates when its serialized object changes, plus a cheap
  500 ms tick for scene state (`YUCPInspector.Refresh`). Refreshers compare and change only what
  differs. Never clear and rebuild UI on a timer: it drops clicks and focus and kills drags.

---

## 6. Writing

### Capitalization

HIG Writing: "Choose a style for each UI element type and use it consistently."

| Element | Style | Example |
|---|---|---|
| Component name (header) | Title Case | Live Grip |
| Section and group titles | Sentence case (HIG Boxes: "Use sentence-style capitalization") | Grip points, Avatar menu |
| Field labels | Title Case, no colon. This follows Unity's inspector convention and departs from HIG Sliders' "sentence-style … end with a colon" for consistency with the neighbouring Unity components. | Menu Path |
| Buttons, segments, tabs, disclosures | Title Case | Edit Grip, Advanced Options |
| Descriptions, captions, tooltips, messages | Sentence case, full stop | The grip is on while this toggle is on. |

### Rules
- **Be brief.** HIG Writing: "Check each word to be sure it needs to be there." One sentence for
  captions and messages, two at most for tooltips.
- **Remove filler** (WWDC25 404): no "simply", "quickly", "please", "sorry", "Oops", and no
  exclamation marks.
- **Don't repeat** (WWDC25 404): no subtitle that restates its title, and no help box that restates
  its field's label or tooltip.
- **Lead with the why** (WWDC25 404): "To get X, do Y."
- **Describe what a setting does when it's on.** HIG Settings and Writing: "Describe what it does
  when turned on, and people can infer the opposite."
- **No ALL CAPS, no "OPTIONAL:" or "NOTE:" prefixes, no "Title\n\nBody" inside one message, no
  emoji, no glyph prefixes** (⬇, +, •). An optional field says so in its tooltip.
- **Use plain words, not internal jargon.** WWDC22 10037: "It's best to use simple, plain language."
  Avoid "orchestrator", "descriptor" (say avatar), "param" (say parameter).
- **Name the object, not the implementation.** Write "Assign the body mesh.", not "targetBodyMesh
  is null."
- **Write empty states that say what to do next.** HIG Writing: "Provide clear next steps on any
  blank screens."

### Word list

WWDC25 404: "Think of the words you would use in your app, and the ones you wouldn't, and write
them down."

| Use | Don't use |
|---|---|
| avatar | descriptor, VRCAvatarDescriptor (in UI text) |
| Avatar menu (section) | Menu, Options, Toggle Settings, Toggles & Menu |
| Menu Path | Menu Location, Target Menu Path |
| Toggle Name | Menu Name |
| On by Default | Default ON, Default On |
| Saved | Saved Between Worlds, Save |
| Synced | Network Synced |
| Global Parameter | Global Param, Param |
| Existing Toggle | Use Existing Toggle (as a segment) |
| Advanced Options | Advanced, Advanced & Diagnostics, Expert & Diagnostics, Diagnostics & Debug |
| Verbose Logging | Debug Logging, Debug Mode (for logging) |
| Show Credits | Include Credits Banner |
| Share Setup / Group ID | Enable Grouping, Grouping & Collaboration |
| Body Mesh | Target Body Mesh, Body Renderer |
| Generate Preview / Clear Preview | Preview In Scene, Revert Preview |
| auto-detect (verb), Automatic (option) | Auto Detect, Auto-Detect |
| PhysBone | Physbone |
| VRCFury toggle | VRCF toggle |
| Last build: … | Build Stats, Last Build Results |

---

## 7. Color, typography, motion

### Color
- **Use only the tokens in `YUCPDesignSystem.uss`.** No `new Color(...)` or `rgb()` in editor or
  window code. HIG Color: "Avoid hard-coding system color values in your app."
- **Surfaces, from DevTools:** window `#151515`, section `#1a1a1a`, raised (popovers) `#232323`,
  inputs `#282828`, neutral controls `#3a3a3a`. Sections are flat: no border, 6 px radius.
- **There is one accent (teal),** used as a tint (20% fill, 50% border, teal text) for the
  primary action, the selection and the selected segment or tab. HIG Color: "reserve it for
  elements that truly benefit from emphasis, such as status indicators or primary actions" and
  "Refrain from adding color to the background of multiple controls."
- **Status colors mean status only:** info blue, warning amber, error red, success teal. HIG Color:
  "Avoid using the same color to mean different things."
- **Color never carries meaning alone.** Badges and messages always have words. HIG Color: "Avoid
  relying solely on color"; HIG Accessibility: "Convey information with more than color alone."
- **Both skins work.** `.yucp-root--light` redefines every token for Unity's light skin (DevTools
  only had a dark one). HIG Color: "Make sure all your app's colors work well in light, dark, and
  increased contrast contexts."

### Typography
- **The DevTools scale:** window title 18 px, section title 16 px in windows and 13 px in
  inspectors, overline header 13 px bold (`yucp-section-header`), body 12 px, secondary 11 px,
  small 10 px. HIG Typography lists macOS's default as 13 pt and minimum as 10 pt; 12 px is the
  size of every field label in the Unity inspector beside ours.
- **Only Regular and Bold weights.** Hierarchy comes from size, weight and color. HIG Typography:
  "avoid light font weights" and "Minimize the number of typefaces you use."

### Motion
- **No decorative motion:** no hover or press scaling, no sliding panes. DevTools scaled buttons
  and rows on hover and animated pane widths; both are removed. HIG Motion: "Don't add motion for the sake of adding motion" and "In
  apps, generally avoid adding motion to UI interactions that occur frequently."
- **Hover and press feedback is a short color change only** (0.15 s). HIG Motion: "Aim for brevity
  and precision in feedback animations."

---

## 8. Code

### `YUCPInspector`

```csharp
[CustomEditor(typeof(FollowerData))]
public class FollowerDataEditor : UnityEditor.Editor
{
    public override VisualElement CreateInspectorGUI()
    {
        var data = (FollowerData)target;
        var ui = YUCPInspector.Create(this, "Follower");                  // header + status + refresh
        ui.Status(() => VRLabsInspector.Placement(data));                  // component-level messages

        var follow = ui.Section("Follow");                                 // sentence-case title
        ui.Field(follow, "appliedObjectOverride", "Followed Object");      // aligned, bound, tooltip from [Tooltip]
        ui.Field(follow, "followSpeed");

        VRLabsInspector.Menu(ui, "generateMenu", "menuLocation", ("globalParameterStop", "Stop Parameter"));
        VRLabsInspector.Advanced(ui, "followerGroupId", "Based on VRLabs Follower (MIT).",
            data.GetBuildSummary, () => data.GetLastBuildTimeUtc());
        return ui.Root;
    }
}
```

| Method | Use for |
|---|---|
| `YUCPInspector.Create(editor, title)` | The root. Loads styles (and the light-skin class) and the status area, then raises `YUCPInspector.Created` so the owning package adds its header and notices. Refreshes on serialized changes and every 500 ms. |
| `Status(messages)` / `Messages(parent, messages)` | Component-level / inline messages. Pass a function; null or empty texts are skipped; rebuilt only when the text changes. |
| `Section(title, description?)`, `Section(parent, …)` | A box. Pass a parent for tab pages. |
| `Group(parent, title?)`, `Indent(parent)` | Subgroups and dependent settings inside a section. |
| `Field(parent, path, label?, tooltip?)` | A bound `PropertyField` on the label column. `[Header]`/`[Space]` attributes are hidden in YUCP inspectors; sections do that job. |
| `Field(parent, otherSerializedObject, path, …)` | Fields on a second object (e.g. a profile asset). |
| `Segmented(parent, path, label, names…)` | Enum, bool or int property as a segmented control. |
| `Caption(parent, text or Func<string>)` | One sentence of secondary text. A function caption hides while empty. |
| `InfoRow(parent, label, Func<string>)` | A read-only value. |
| `Button(parent, text, onClick, variant, tooltip?, icon?)`, `ButtonRow(parent)`, `IconButton(icon, tooltip, onClick)` | Actions. |
| `List(parent, items, make, bind, emptyText, add, remove, addTooltip, removeTooltip)` | Selectable list with + / −. Call `Refresh()` after the items change and `Select(i)` to sync the selection. |
| `ShowWhen(element, cond)`, `EnableWhen(element, cond)` | Progressive disclosure and availability. Re-evaluated on refresh. |
| `OnRefresh(action)`, `Refresh()` | Custom updates. Compare first, change only what differs. |
| `Disclosure(parent, title, key, open?)` | A remembered foldout (rarely needed). |
| `Advanced()` | The one Advanced Options disclosure. It stays last even if sections are added after it. |
| `BuildSummary(Func<string>, Func<DateTime?>?)` | "Last build: …" in Advanced Options. |
| `Tabs(key, names…)` | A Simple / Advanced tab view; returns `TabView` with `Pages` and `Buttons`. |
| `YUCPSceneOverlay.Draw(title, body)` | The Scene view instruction panel during an edit mode. |
| `MaterialPicker(parent, path, label, source, …)` | Material tiles with a checkbox each (Auto Body Hider, Auto UV Discard). |

### Classes

Use these instead of styling an element by hand. Windows use them too.

| Class | What it is |
|---|---|
| `yucp-section`, `yucp-section-title`, `yucp-section-description`, `yucp-section-content` | A section (`CreateCard` / `ui.Section`). |
| `yucp-section-header` | An overline heading above a list or pane ("Export Profiles"). |
| `yucp-group`, `yucp-group-title` | A titled group inside a section. |
| `yucp-button` + `-primary` / `-danger` / `-text` / `-small` / `-large` / `-icon` | Buttons (`CreateButton`). |
| `yucp-help-box` + `-warning` / `-error` / `-success` | Messages (`CreateHelpBox`); plain `yucp-help-box` is info. |
| `yucp-badge` + `-primary` / `-warning` / `-danger` / `-info` | Pill badges. |
| `yucp-segmented`, `yucp-segment`; `yucp-tabs-header`, `yucp-tab` | The pill switcher. |
| `yucp-list`, `yucp-row`, `yucp-row--selected` | Lists and selectable rows. |
| `yucp-input`, `yucp-dropdown`, `yucp-toggle` | Window form controls. Inspectors keep Unity's own fields. |
| `yucp-label`, `yucp-label-secondary`, `yucp-label-small`, `yucp-title` | Text. |
| `yucp-empty-state`, `yucp-separator`, `yucp-spacer`, `yucp-stat-*` | Empty states, dividers, stats. |

Family helpers. Use one before writing a block that another editor already has:

| Helper | Use for |
|---|---|
| `VRLabsInspector` | Placement status, `Menu`, `Advanced` (Share Setup, credits, last build), `AvatarObject`, `Trigger` (gesture or global parameter), `Gesture`, `DampingConstraint`. |
| `ToggleSettingsInspector.Section(ui, …)` | The VRChat toggle block (Menu Path, Global Parameter, Saved, On by Default, slider, hold, exclusive tags, security, icon). Pass the property paths the component has. |
| `UVDiscardInspector` (in `AutoBodyHiderDataEditor.cs`) | Material, UV channel and tile settings for UV discard. |

### Rules for editor code
- **Build every inspector with `YUCPInspector` and every window with the shared classes.** Use no local `AddBoundField`/`AddSlider`/`Caption`
  copies, no `AddSpacing`, and no `style.*` for color, font size, margins, padding or borders. Add a
  class to the design system instead.
  - Allowed inline styles: `display` through `ShowWhen`, and genuinely dynamic geometry such as a
    list height.
- **Use no `schedule.Execute(...).Every(...)` in editors.** Use `ui.OnRefresh`.
- **Write tooltips in the component's `[Tooltip]` attributes,** following the writing rules.
- **Put `[HelpURL]` on the component for documentation.** Don't add a button.
- **Package-wide chrome goes through `YUCPInspector.Created`.** com.yucp.components uses it to add
  the component header and to put the `[BetaWarning]` text and the missing-VRCFury error in the
  status area; the IMGUI header drawers skip editors built with `YUCPInspector`, so every notice
  sits in one place with one style. HIG Feedback: "Consider integrating status feedback into your
  interface."
- **A window or package never ships its own copy of tokens or base controls.** Window stylesheets
  hold only that window's layout (its panes, its list rows); they never redefine `yucp-button`,
  `yucp-section`, `yucp-help-box` or a token.
- **Shared parts of a component family live in one helper** (`VRLabsInspector` for the
  VRLabs-based components). Never copy a block between editors.
- **Every action is undoable.** Use `SerializedProperty` writes or `Undo.RecordObject`, never a
  bare field assignment plus `SetDirty`.

---

## 9. Review checklist

Before an inspector ships:

- [ ] Order: status, primary sections, secondary, Avatar menu, Advanced Options (last, collapsed).
- [ ] No nested boxes, no single-field sections, no summary card above the inputs.
- [ ] Inspector sections are cards with the title above; one leading edge for titles, labels,
      checkboxes, captions, messages and lists.
- [ ] Every label on the same column; checkboxes on the leading edge with trailing titles;
      dependent settings indented and hidden until enabled.
- [ ] Every message sits under the setting it is about; only component-level problems use the
      status area.
- [ ] Every segment has a tooltip; lists use + and − beneath them on the leading edge.
- [ ] At most one Primary button; destructive actions use Danger; every button label is a verb.
- [ ] No help box after fields; tooltips carry explanations; messages sit next to what they are about.
- [ ] No dialogs except for irreversible destructive actions.
- [ ] Capitalization per element type; no ALL CAPS, "OPTIONAL:", "!", emoji or glyph prefixes;
      words from the word list.
- [ ] No hard-coded colors, font sizes or spacing; no redefined shared classes; works in the light
      skin.
- [ ] No polling rebuilds; dragging a slider and typing in a field are never interrupted.
- [ ] Every change is undoable.
- [ ] Checked by eye in the Inspector, top to bottom, in both skins.
