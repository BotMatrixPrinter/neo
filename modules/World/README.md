# World

The place you fly over: 4 km x 4 km of cartoon countryside ("Porco Rosso meets Sea of
Thieves"): a mostly open, flyable patchwork of green and golden fields with hedges, rolling hills,
three small rocky mountains with snowy or pale rock caps, a winding river and two lakes,
clumps of low-poly trees, under a bright stylized sky. Planes can hit the ground: use
`Terrain.HeightAt` for where it is.

- **Contract** (`Contract/`), no messages yet:
  - `WorldBounds`, constants: the ground spans `-HalfSize..HalfSize` (2000 m) on X and Z
    around the origin; `GroundY` (0 m) is the lowest ground (the water surface);
    `MaxHillHeight` is the highest point, `Terrain.MaxHeight` (250 m).
  - `Terrain`, static, pure, deterministic and allocation-free (safe per tick):
    - `HeightAt(x, z)`: height (m) of the ground, or of the water surface on the river and
      lakes, exactly as drawn (the ground mesh is built from it); always
      `WaterLevel..MaxHeight`. Defined everywhere, also outside the 4 km square.
    - `MaxHeight` (250 m): the top of the tallest mountain; nothing is higher.
    - `WaterLevel` (0 m, = `GroundY`), `WaterDepthAt(x, z)` (0 on land), `IsWater(x, z)`.
    - `PlainHeight` (2 m), `OpenRadius` (350 m), `RiverHalfWidth` (22 m), `RiverX(z)` and
      `RiverDistance(x, z)` (the river's center line), `GridCells` (400) and `GridStep`
      (10 m): the ground mesh's grid, on which the drawn ground differs from `HeightAt` by
      under 1 m between vertices.

  The terrain (all in `Terrain.cs`): the plain at 2 m with 3 m meadow swells; rolling hills
  up to 2 + 3 + 46 m from smooth value noise, kept off the open areas and the valleys
  (none within 40 m of a shore, fading in to 220 m); the center and both start areas
  (0, +-1500: Allies south at +Z, Central north at -Z) are open plain within `OpenRadius`,
  hills fading in by 700 m. Three mountains (smooth top, concave slopes roughened by soft
  ridged noise, outline warped; the top is provably the highest point of each):

  | top (x, z) | height | footprint radius |
  |---|---|---|
  | (-1450, -500) | 250 m | about 430 m |
  | (1350, 150) | 207 m | about 450 m |
  | (-850, 950) | 167 m | about 380 m |

  Water (flat at 0 m): the river (about 44 m wide, the same winding curve as before) and two
  lakes, ellipses with wobbly shores centered at (-700, -900) (460 x 280 m) and (850, 1150)
  (340 x 250 m). Their beds sink to -2.5 m under a 50 m wide shore, so banks are gentle.
  The balloons (300-500 m up) keep at least 50 m clear of the highest point.
- **Core** (`Core/`): `WorldModule`, the entry class (`IModule`). It has no rules yet: it
  only counts steps (no allocations), so the shell loads the scene.
- **Godot** (`Godot/`): `Main.tscn` with `WorldView.cs`, which builds the ground mesh
  (one surface, `Terrain.GridCells` squared cells, vertices at `Terrain.HeightAt`, UV.x the
  height above water or minus the water depth) and the trees (two `MultiMesh`es: round trees
  and poplars, seeded so the layout never changes; clumps on dry land away from shores,
  below 110 m and off steep slopes, so none on water or peaks) in `_Ready`. No SDFGI. Few draw calls: ground, round trees and poplars, each a toon pass plus an outline
  pass, and the sky.
  - `Toon.gdshaderinc`: cel lighting (shadow / half-lit / lit bands), included by
    `Ground.gdshader` (fields and hedges on the low land, meadow on hills, banded rock with
    flat low-poly facets on high or steep ground, pale rock above 150 m and snow above about
    190 m; sandy shores and the water where UV.x < 0: bright blue, deeper offshore, soft
    breathing foam at the shore, gently drifting animated ripples) and `Tree.gdshader`
    (vertex colors).
  - `Outline.gdshader`: ink lines by inverted hull, a fixed pixel width on screen.
  - `Sky.gdshader`: gradient sky, sun halo, two-tone cartoon clouds. The `WorldEnvironment`
    adds depth fog toward the horizon and gentle glow; the warm `Sun` casts soft shadows.
  - `PreviewCamera` frames the scene when it runs alone (screenshots, the editor) from a
    plane's height (320 m above the Allies' start, looking north across the map); it is
    removed when the shell loads the scene, so the player's camera is used.
  - Colors and light energies were tuned on the screenshot renderer (Compatibility/OpenGL).
  Other modules may copy the shaders' approach but must not reference these files.
- **Tests** (`Tests/`): `WorldModuleTests`: starts and ticks without allocating, is the only
  entry class, targets net10.0, no Godot in Core or Contract. `TerrainTests`: heights within
  `WaterLevel..MaxHeight` everywhere, the tallest top equals `MaxHeight` with room under the
  balloons, mountains where documented (tops highest), open rolling land elsewhere, flat low
  center and start areas, flat water on the river and lakes, same answer every call without
  allocating, and the mesh grid matches `HeightAt` within 1 m.

Test it alone: `./test.sh World`. Screenshot (not
committed): `./screenshot.sh modules/World/Godot/Main.tscn` (`World.png`).
