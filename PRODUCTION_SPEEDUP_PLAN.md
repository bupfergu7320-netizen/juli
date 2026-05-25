# Production Speedup Plan

Last updated: 2026-05-23

Trigger words: 提速, 速度, 太慢, 耗时, 快一点, 加快, 拍照慢, 检测慢.

When the user mentions similar speedup words later, show this plan first and use the latest field logs to decide which step to execute.

## Current Field Baseline

Latest checked field test: 2026-05-23 17:16-17:18.

Observed timings:

- OK total time: about 1112-1226 ms.
- NG total time: about 1991-2088 ms.
- Capture time: about 365-475 ms, including the configured 0.300 s capture delay.
- Template similarity: about 490-533 ms per inspection.
- Front/back contour mirror decision: about 1 ms.
- Display time: about 24-61 ms.
- PLC write/handshake time: about 53-97 ms.

Important conclusion:

- The new front/back rule is not the slow part. It costs about 1 ms.
- OK speed is mainly limited by capture delay and template similarity.
- NG is slow because the current production flow re-runs full vision inspection to build a diagnostic image after an NG result.

## Recommended Execution Order

### Step 1: Stop Full Second Inspection For Production NG

Goal:

- Reduce NG total time from about 2.0 s to about 1.2-1.4 s.

Current issue:

- Production PLC inspection first runs the fast path without a diagnostic image.
- If the result is NG, it runs `Inspect()` again with full diagnostic image enabled.
- This repeats the expensive template similarity and other vision work.

Recommended change:

- Keep the first inspection result as the final judgment.
- For NG, do not run full `Inspect()` again.
- Generate a simplified NG preview from the already available first-pass result:
  - large red NG in the upper-left preview,
  - NG reason,
  - main contour,
  - center marker,
  - XYR,
  - template/front-back scores when available.

Expected impact:

- Does not affect OK/NG judgment.
- Reduces NG latency significantly.
- Keeps enough evidence for field debugging, but the NG image will be less detailed than the current full diagnostic image.

Risk level: low.

### Step 2: Write PLC Result Before UI/Reports/Images

Goal:

- Let PLC receive `D1010=1/2` earlier by about 30-100 ms.

Recommended production order:

1. Capture.
2. Inspect.
3. Write PLC result immediately.
4. Update UI.
5. Save report/image/database records.

Current production order is closer to:

1. Capture.
2. Inspect.
3. Save records/reports/images.
4. Update UI.
5. Write PLC result.

Expected impact:

- PLC gets the production answer earlier.
- UI and evidence recording can finish slightly later.
- Need to ensure report/image save failures never change a result already handed to PLC.

Risk level: low to medium.

### Step 3: Optimize Template Similarity

Goal:

- Reduce OK total time from about 1.1-1.2 s toward about 0.8-1.0 s.

Recommended direction:

- Do not affine-transform the whole current workpiece mask every time.
- Transform sampled current contour points into the template patch and compare there.
- Keep the existing formula and threshold behavior as close as possible.

Validation required:

- Replay the 2026-05-23 17:16-17:18 reports/images.
- Confirm all known OK/NG decisions stay the same.
- Compare `MatchScore` distribution before publishing.

Expected impact:

- Faster algorithm path.
- Some score drift is possible, so this must not be mixed with Step 1 and Step 2 in the same field release.

Risk level: medium.

### Step 4: Add Production ROI

Goal:

- Reduce full-frame processing work on 5472x3648 images.

Recommended direction:

- Add a production detection area around the fixture.
- Exclude side lights, rails, screws, and unrelated reflections from contour detection.

Expected impact:

- Less contour noise.
- Faster contour detection and possibly faster matching.

Risk:

- If the workpiece can move outside ROI, detection may fail.
- Requires field confirmation of the maximum part offset range.

Risk level: medium.

## Do Not Prioritize First

PLC communication:

- Current PLC time is about 53-97 ms.
- Benefit is smaller than vision/image changes.
- Keep PLC safety/handshake behavior stable unless there is a separate PLC issue.

Capture fixed delay:

- The configured delay is 0.300 s.
- The user wants to control this in camera settings, not by code.
- Lowering it from 0.300 s to 0.100 s could save about 200 ms if the image remains stable.

## Next Recommendation

If the user asks to proceed with speedup, implement only:

1. Stop full second inspection for production NG.
2. Write PLC result before UI/report/image work.

Then publish and field-test before touching template similarity.
