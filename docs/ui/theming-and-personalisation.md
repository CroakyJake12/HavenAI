# Theming and Personalisation

How Haven's five themes, four appearances, accent override, global font and
profile pictures work — and how apps consume them. Implementation lives in
`src/Haven.Desktop/HavenUI/Tokens/`, `Controls/SurfacePaletteCatalog.cs`,
`HavenUI/Tokens` applier, and `Services/UserPreferencesService.cs`.

## The model

```text
Theme (Glow | Bubble | Retro | Playful | Cinematic)
   ↓  expression: radius/motion/shadow scales + interaction treatment
Appearance (SuperBright | Bright | Dark | SuperDark)   ← the shared light/dark control
   ↓  colour branch
Accent source: override ON → selected palette (13 semantic families)
               override OFF → per-surface hue anchors
   ↓
SurfacePaletteCatalog.For(surface, appearance) → Palette (semantic colours)
   ↓ HavenUiResourceApplier.Apply()
application resources (brushes, radii, motion scale) → Avalonia styles + HUI scenes
```

- **Themes decide how things look and react; never where they go.** Layout,
  navigation, spacing rhythm and information architecture are theme-invariant.
- **Appearances are the Light/Dark dimension** Haven already had: Bright and
  SuperBright are the light expressions, Dark and SuperDark the dark ones.
  Every theme ships all four.
- **Glow is the baseline**: with default personalisation the pipeline is an
  identity transform, byte-identical to the pre-theme appearance (guarded by
  `PersonalisationTests.Glow_palette_matches_the_pre_theme_baseline`).

## Theme identities

| Theme | Personality | Signature |
|---|---|---|
| Glow (default/fallback) | tidal gradients, soft glow | unchanged baseline |
| Bubble | soft, glassy, atmospheric | translucent panels, bloom hover, larger radii |
| Retro | engineered, technical | hairline illuminated borders, veil hover, sharp corners, fast motion |
| Playful | tactile, tonal, friendly | opaque tonal fills, bold hover fills, pill radii |
| Cinematic | immersive, depth-driven | tinted translucent layers, heavier shadows, slow fades |

Each non-Glow theme changes hover/press/focus derivation, border treatment,
panel translucency and geometry/motion scales (`HavenThemeCatalog.All`) — not
just hues.

## Accent precedence

Override **off**: surface accent → theme interpretation → semantic brushes.
Override **on**: selected palette anchors → same interpretation. Apps only
ever consume semantic resources (`HavenAccentPrimaryBrush`, …). Never read the
override flag in app code. GenUI `AccentKey` scoping continues to win inside
its `HavenAccentScope`.

All 13 palettes (Red…Monotone) resolve per appearance family; Yellow/Lime use
deepened strong anchors for contrast; Monotone is true grayscale with strong
contrast while status colours keep their meaning.

## Fonts

`HavenUiFont` resolves every "Montserrat" request through the user's selection
with the bundled Montserrat face as guaranteed fallback
(`"User Font, bundled#Montserrat"`). AXAML consumes `HavenFontFamily` as a
dynamic resource; HUI text goes through `HavenSceneControl`'s resolver.
Selection uses OS-installed families via `FontManager.SystemFonts`; missing
fonts degrade down the fallback chain.

## Avatars

Independent opt-ins (`UserAvatarEnabled`, `HavenAvatarEnabled`) rendered in
chat via the optional `Avatar` element in `ChatUserMessage.hui` /
`ChatAssistantMessage.hui`, fed `avatar://user` / `avatar://haven` sources by
the scene. `HavenDesktopImageResolver` resolves that scheme from
`AvatarStore`'s processed local files. Disabled by default; enabling requires
a stored asset; removing the asset disables rendering. Images never leave the
device.

## Live updates

Changing theme/appearance/accent/font/avatar applies immediately:
preferences publish into shared state (`HavenPersonalisation`) and mutate
existing resources in place — no shell rebuild, no restart, active tabs/text/
tasks survive. `AppearanceChanged` fires after any personalisation change;
background consumers re-render from it.

## What app developers must NOT do

- Do not branch on `if Theme == Bubble` in product code; express differences
  through tokens or extend the catalog/applier.
- Do not hard-code hex colours in UI (exception: canonical palette definitions,
  e.g. the settings swatches).
- Do not create app-local theme/accent/font/avatar stores — consume the
  shared one.
- Do not change layout to express a theme.
