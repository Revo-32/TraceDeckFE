# TraceDeck FE — Forza Horizon 6 Manual Validation Checklist

Use this checklist for a physical-input regression pass against a loaded Forza Horizon 6 session. Use a synthetic, non-sensitive reference image and avoid changing game progress, vehicles, credits or unrelated account data.

Recommended setup:

- Forza Horizon 6 in Windowed or Borderless mode
- TraceDeck FE v1.0.0
- A synthetic PNG or SVG with clear geometry and known colors
- Reference Visible ON; begin with Lock OFF

## 1. Target selection and connection

- Select the Forza window from the target list and connect.
- Confirm the controller reports a connected target and matching client size.
- Confirm the reference appears directly above the target when Visible is ON.

## 2. Resize and repeated resize

- Place the reference near the center and note its approximate percentage of target width.
- Resize Large → Small → Medium → Large.
- Confirm relative center, relative size and aspect ratio remain stable.
- Return to the original client size and confirm there is no visible drift.
- Confirm automatic resize compensation does not dirty the project or consume an Undo action.

## 3. Window movement

- Move the target without changing its size.
- Confirm the overlay follows while the reference transform and project dirty state remain unchanged.

## 4. Z-order and visibility

- With Visible ON, confirm the reference appears above Forza when Forza is foreground.
- Bring an unrelated normal application forward and confirm it naturally covers the overlay.
- Return to Forza and confirm the reference returns without toggling Visible.
- Turn Visible OFF, switch applications and return; confirm it remains off.

## 5. Minimize and restore

- Minimize Forza and confirm the overlay disappears while the controller remains available.
- Restore Forza and confirm the overlay returns with the prior relative placement.

## 6. Lock and cursor-centered zoom

- With Lock ON, click through the overlay and confirm Forza receives input.
- With Lock OFF, drag the reference and confirm it moves.
- Zoom with the pointer away from the center and confirm the source point under the cursor remains approximately fixed.

## 7. Opacity and guides

- Test low, medium and full reference opacity; confirm controller opacity is unaffected.
- Enable grid and center guides and confirm they align to the target client area.
- Confirm guides do not force the reference Visible state on.

## 8. Original-pixel color picker

- Pick a known pixel from the synthetic reference.
- Confirm HEX, RGB and Forza HSB match the original source, even when opacity or display effects are active.
- Begin another pick and cancel with Esc.

## 9. Target lifecycle

- Close Forza and confirm TraceDeck FE remains running and disconnects cleanly.
- Relaunch Forza if convenient and confirm automatic detection or manual reconnection still works.

## 10. Idle behavior

- Leave a connected reference visible for several minutes without input.
- Confirm there is no visible continuous redraw/flicker, sustained high CPU or game video inside the controller.

## Result template

```text
Target selection: PASS / FAIL
Resize and repeated resize: PASS / FAIL
Window movement: PASS / FAIL
Z-order: PASS / FAIL
Visible OFF persistence: PASS / FAIL
Minimize and restore: PASS / FAIL
Lock and zoom: PASS / FAIL
Opacity and guides: PASS / FAIL
Original-pixel color picker: PASS / FAIL
Target close/reconnect: PASS / FAIL / SKIPPED
Idle behavior: PASS / FAIL / SKIPPED
Notes:
```

The v1.0.0 release completed all 11 checklist groups successfully on 2026-08-29. This record contains no personal account, machine or private-project details.

