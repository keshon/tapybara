# Native layout probes

Generated from actual Calls/confirmation XAML and reusable controls using native WPF theme templates at wide and compact sizes, in light and dark themes. Export images exercise the shared opaque surface and export options.

These are controlled offscreen layout fixtures: event handlers/services are omitted, sample rows are inserted, and accent resources are seeded without application bootstrap. Primary-action appearance may differ from the running application. They do not verify actual menu animation, keyboard focus, monitor DPI, audio, real recording data or export delivery.

Run with `TAPYBARA_RENDER_LAYOUTS=1` when executing `tests/Tapybara.App.Tests` to refresh these images. The ordinary test run leaves artifacts unchanged.

Second-pass fixtures include time-boundary forms, paired date fields, checked icon contrast and compact Find/Replace. Menu checks inspect the actual themed shell for movement storyboards. Fixture accent colors still differ from full application bootstrap; live pointer, menu-edge and keyboard validation remains necessary.

Regression fixtures include compact fields rendered with the actual application font, restored session-menu chrome in both themes, and a long menu before/after scrolling. Separate hidden native popup tests check screen coordinates shortly after opening and after layout settles, both below and above the anchor. These cover the positioning rule in controlled conditions, not every multi-monitor arrangement or the running installed build.
