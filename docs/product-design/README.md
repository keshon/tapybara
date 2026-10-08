# Tapybara product design

Saved locally on D: on 8 October 2026. Fix plan and mockup direction approved; implementation started.

- [Plan and revised interaction specification](plan.md)
- [Implementation status and reusable component boundaries](implementation.md)
- [Detailed 21-finding audit](audit.html)
- [Current interactive mockups, standalone/offline](mockups.html)
- [Current editable inline source](tapybara-ui-mockups.html)
- [First mockups, standalone/offline](mockups-v1.html)
- [First editable source](mockups-v1.fragment.html)

Open `mockups.html` in a browser. Both Transcript first and Review workbench are available through the variant picker. Click between words and use Ctrl+Enter or Split here; Undo reverses it. Filters expand from the button beside Search. Expanded timeline has Sources and Speakers modes.

This is a product interaction prototype using sample data. Audio, recording, filesystem operations and export delivery are simulated. Envelopes and candidate speakers are illustrative. Native WPF implementation and real audio validation remain separate work.

These files are now inside the repository and can be versioned with the project. Saving to D: is local persistence, not an off-device backup or a Git commit.

Prototype verification: JavaScript syntax checked; caret split and Undo, inline typing and Undo, filter expansion/removal, selection reassignment and updated speaker lanes, timing-review indicators and export preview exercised in the browser. Tested root widths at desktop, compact and 320px viewport without horizontal overflow. That prototype verification was separate from implementation. Current application changes and test results are recorded in implementation.md.

Current native UI layout probes: [validation notes](validation/README.md). These supplement the preserved browser prototypes; see the latest UI revision in the plan and implementation status.
