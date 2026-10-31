# EnglishHub visual refresh / Thiết kế lại giao diện

Implemented and checked on **16–17/11/2026**. This is a visual and navigation update; lesson completion, grading, score evidence and calendar policies retain their server-side rules.

## Evidence and interpretation

- [Plass et al., 2014](https://doi.org/10.1016/j.learninstruc.2013.02.006) tested color and rounded face-like shapes in multimedia lessons. Effects differed across comprehension, emotion and transfer conditions. The studies support investigating appealing instructional visuals, rather than a universal optimal learning palette.
- [Mayer & Estrella, 2014](https://doi.org/10.1016/j.learninstruc.2014.02.004) found benefits from appealing, personified instructional graphics in short lessons about viruses. That setting does not establish the effect of this website's colors on language acquisition.
- [Heidig et al., 2015](https://doi.org/10.1016/j.chb.2014.11.009) found a more nuanced relationship involving perceived aesthetics/usability and motivation. Color and rounded corners alone are insufficient evidence of improved learning outcomes.
- [Duolingo's design article](https://blog.duolingo.com/shape-language-duolingos-art-style/) informs the use of clear silhouettes, rounded shapes, vivid accents and whitespace. [Busuu](https://www.busuu.com/) provides a reference for short, focused learning steps. EnglishHub uses its own book illustration and semantic palette.
- [WCAG text contrast](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html) and [non-text contrast](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html) provide measurable readability criteria: 4.5:1 for ordinary text, 3:1 for qualifying large text and applicable control indicators.

VI: Lựa chọn thiết kế là suy luận từ các nguồn trên và mục tiêu sử dụng của sản phẩm. Chưa chạy nghiên cứu người học, thử nghiệm ngẫu nhiên hay phép đo khả năng nhớ từ vựng trên EnglishHub; không gắn tuyên bố “kích thích não bộ tốt nhất” vào một tông màu cụ thể.

EN: The practical objectives are legibility, clear next actions, friendly presentation and consistent skill identification. These are design objectives, not measured educational gains.

## Palette and type

| Role | Colors | Calculated contrast |
|---|---|---|
| Main text | `#142342` on white | 15.57:1 |
| Supporting text | `#52617A` on `#F5F7FC` | 5.85:1 |
| Primary button | White on `#2447E6` | 6.73:1 |
| Hero heading | White on `#17245B` | 14.58:1 |
| Hero action | `#142342` on `#FFCF56` | 10.61:1 |
| Input border | `#7B8BA5` on white | 3.45:1 |
| Listening illustration | White on `#2855DB` | 6.15:1 |
| Grammar illustration | White on `#713DE0` | 6.09:1 |
| Reading illustration | White on `#08786B` | 5.37:1 |

The previous card description color `#929C91` on white calculated to 2.84:1 at a 10px font. Supporting copy is now generally 13–16px, lesson text 17–18px, with stronger headings and 16px inputs. Small uppercase category labels remain distinct from paragraph text. Vocabulary uses an amber illustration with dark ink.

The palette is defined in `web/src/theme.css`; shared and route-specific styles consume semantic variables. Success/danger labels retain text and icons. Category colors follow the skill rather than a card's position in a list.

## Interaction changes

- The hero's **Học bài hôm nay** action leads to the daily plan. Learning tasks precede optional skill analysis, which uses a native disclosure.
- Current task date/timezone comes from the server's date-only calendar snapshot; formatting does not shift it through a second timezone conversion.
- Catalog filters expose pressed state, search uses a named search field, and result counts announce loading/empty/result states.
- Mobile has a labelled disclosure menu with Settings and Logout, correct expanded state, Escape handling and focus return. Selecting a route closes it.
- A skip link reaches the main region. New route navigation starts at the top; hash links wait for asynchronously rendered anchors. Search typing does not move focus.
- Quiz choices have a stronger selected state; assessment option buttons expose pressed state. Shared checkboxes no longer depend on assessment CSS being loaded first.
- A narrow result score stays together on one line. Layouts wrap rather than relying on hidden horizontal overflow.
- State colors change immediately; motion is limited to short transforms. Reduced-motion CSS disables nonessential transitions and animation.

## Observed verification and limits

TypeScript/Vite production builds passed. Browser checks cover dashboard, task anchor, skill disclosure, category/search filtering, empty results, lesson reading and an unsubmitted radio selection, existing result explanations, saved notes, calendar deep links, Settings and account form presentation. The existing quiz/result data was used; no new attempt was submitted and no profile/calendar form was saved during the UI checks.

Responsive checks use viewport overrides at 320px, 390px, 768px and 1280px, including horizontal-width checks. After clearing overrides, the actual 603px app panel was also checked. Keyboard checks exercise mobile Escape/focus return and Tab → skip link → main focus. The weekday label was measured at 64×44px; a cold-loaded saved-list checkbox used its shared 18px control and 44px label.

Rendered-style contrast observations are recorded separately in `outputs/EnglishHub-UI-audit.json` at handover. They inspect rendered text/background pairs, excluding hidden/decorative/disabled content, and are samples of the inspected routes/states. They are not a full WCAG conformance audit. Before/after screenshots are actual captures, without image recoloring.

Backend regression suites remain part of CI. [URL-persisted catalog filters](catalog-filters.md) were added separately after the visual refresh. Broader screen-reader testing, real-device coverage, learner usability studies and educational-effect experiments remain unverified.
