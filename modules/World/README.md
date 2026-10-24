# World

The place you fly over: a 2 km x 2 km playable square of cartoon countryside ("Porco Rosso
meets Sea of Thieves"): a mostly open, flyable patchwork of green and golden fields with
hedges, rolling hills, two small rocky mountains with snowy caps, a winding river and a lake,
clumps of low-poly trees, under a bright stylized sky. All around it a ring of massive cartoon
mountains (steep rocky faces, snowy peaks, cel-shaded and outlined) rises from just beyond the
edge to 1600 m and more, well above the 1000 m where engines lose their climbing power, so no
plane gets over or around it; more ranges stand behind, fading into haze to the horizon. Planes
can hit the ground and the walls: use `Terrain.HeightAt` for where they are.

- **Contract** (`Contract/`), no messages yet:
  - `WorldBounds`, constants: `HalfSize` (1000 m): the playable square spans
    `-HalfSize..HalfSize` on X and Z around the origin. Every module uses it for the map's
    size; none keeps its own copy. `GroundY` (0 m) is the lowest ground (the water surface);
    `MaxHillHeight` is the highest point, `Terrain.MaxHeight`.
  - `Terrain`, static, pure, deterministic and allocation-free (safe per tick):
    - `HeightAt(x, z)`: height (m) of the ground, or of the water surface on the river and
      lake, exactly as drawn (the meshes are built from it); always `WaterLevel..MaxHeight`.
      Defined everywhere: beyond the square's edge it is the wall mountains, so flying into
      them is a crash like any ground.
    - `MaxHeight` (2750 m): the top of the tallest wall peak (at (150, -1720), north of the
      map); nothing is higher. `InnerMaxHeight` (200 m): the highest point inside the
      playable square (the top of its tallest mountain).
    - `WallMinHeight` (1600 m) and `WallReach` (370 m): from `WallReach` beyond the edge (on X
      or Z) outward, the ground is at least `WallMinHeight` high, everywhere around.
      `BeyondEdge(x, z)`: distance beyond the edge, the far way (0 or less inside).
    - `WaterLevel` (0 m, = `GroundY`), `WaterDepthAt(x, z)` (0 on land), `IsWater(x, z)`.
    - `PlainHeight` (2 m), `OpenRadius` (175 m), `StartDistance` (600 m), `RiverHalfWidth`
      (16 m), `RiverX(z)` and `RiverDistance(x, z)` (the river's center line), `GridCells`
      (250) and `GridStep` (8 m): the ground mesh's grid, on which the drawn ground differs
      from `HeightAt` by under 1 m between vertices.

  The playable land (all in `Terrain.cs`; job 703's landscape at half the scale): the plain at
  2 m with 3 m meadow swells; rolling hills up to 2 + 3 + 46 m from smooth value noise, kept
  off the open areas and the valleys (none within 25 m of a shore, fading in to 120 m); the
  center and both start points (0, +-600: Allies south at +Z, Central north at -Z) are open
  plain within `OpenRadius`, hills fading in by 420 m, so both spawn halves are mostly low and
  open. Two mountains (smooth top, concave slopes roughened by soft ridged noise, outline
  warped; the top is provably the highest point of each), on the flanks:

  | top (x, z) | height | footprint radius |
  |---|---|---|
  | (-620, -120) | 200 m | about 280 m |
  | (600, 200) | 162 m | about 250 m |

  Water (flat at 0 m): the river (about 32 m wide, winding north to south past the center)
  and a lake, an ellipse with a wobbly shore centered at (-330, -470) (280 x 180 m). Their
  beds sink to -2.5 m under a 40 m wide shore, so banks are gentle. The edge itself and just
  inside it are low (under 60 m).

  The wall (beyond the edge only; inside it adds nothing): its foot starts 0..120 m beyond the
  edge (bays and spurs), and it rises steeply over 175..250 m (ribs and gullies) to a crest of
  1650 m plus up to 380 m of crags; behind the crest the ground rolls up to 500 m higher.
  Snowy wall mountains in hashed rows (along the crest, and 850, 1600, 2800 m out), each a
  broad bump on the wall, tops rounded off under 2560 m; one taller peak (2750 m) stands
  out. All of it is at least 1600 m from 370 m out to any distance.
- **Core** (`Core/`): `WorldModule`, the entry class (`IModule`). It has no rules yet: it
  only counts steps (no allocations), so the shell loads the scene. `WallMesh`: the layout of
  the wall ring mesh (shared by `WorldView` and the tests): square rings around the playable
  square, each `Segments` (4 x `GridCells`) vertices, ring 0 the ground mesh's border; rings
  every 6 m up to 510 m out (the face and the crest), then each 12 % farther apart out to
  6 km.
- **Core** (`Core/`): `WorldModule`, the entry class (`IModule`). It has no rules yet: it
  only counts steps (no allocations), so the shell loads the scene.
- **Godot** (`Godot/`): `Main.tscn` with `WorldView.cs`, which builds the ground mesh
  (one surface, `Terrain.GridCells` squared cells, vertices at `Terrain.HeightAt`, UV.x the
  height above water or minus the water depth), the wall ring mesh (one surface, laid out by
  `WallMesh`, vertices at `Terrain.HeightAt`, same UVs) and the trees (two `MultiMesh`es: round trees
  and poplars, seeded so the layout never changes; clumps on dry land away from shores,
  below 110 m and off steep slopes, so none on water, peaks or walls) in `_Ready`. No SDFGI.
  Few draw calls: ground, wall ring, round trees and poplars, each a toon pass plus an
  outline pass, and the sky; three materials (ground, wall, trees) and one outline each.
  - `Toon.gdshaderinc`: cel lighting (shadow / half-lit / lit bands), included by
    `Ground.gdshader` (fields and hedges on the low land, meadow on hills, banded rock with
    flat low-poly facets on high or steep ground, pale rock above 150 m and snow above about
    190 m; on the wall ring, through its uniforms, faint 140 m strata, pale rock above about
    1850 m and snow above about 2050 m on all but the steepest rock; sandy shores and the water where UV.x < 0: bright blue, deeper offshore, soft
    breathing foam at the shore, gently drifting animated ripples) and `Tree.gdshader`
    (vertex colors).
  - `Outline.gdshader`: ink lines by inverted hull, a fixed pixel width on screen.
  - `Sky.gdshader`: gradient sky, sun halo, two-tone cartoon clouds. The `WorldEnvironment`
    adds depth fog (700 m to 7 km, the haze the far ranges fade into, matching the horizon)
    and gentle glow; the warm `Sun` casts soft shadows.
  - `PreviewCamera` frames the scene when it runs alone (screenshots, the editor) from a
    plane's height (420 m up over the Allies' half at (150, 850), looking north across the map
    at the wall); it is
    removed when the shell loads the scene, so the player's camera is used.
  - Colors and light energies were tuned on the screenshot renderer (Compatibility/OpenGL).
  Other modules may copy the shaders' approach but must not reference these files.
- **Tests** (`Tests/`): `WorldModuleTests`: starts and ticks without allocating, is the only
  entry class, targets net10.0, no Godot in Core or Contract, `HalfSize` is 1000 (a 2 km
  square). `TerrainTests`: heights within `WaterLevel..MaxHeight` everywhere (out to 7 km), the
  tallest peak's top is `MaxHeight`; inside the square at most `InnerMaxHeight`, reached; the
  wall all around: low just inside and at the edge, at least `WallMinHeight` from `WallReach`
  out to any distance; peaks above 2100 m behind the crest along most of the edge, a jagged
  crest; the inner mountains where documented (tops highest), open rolling land elsewhere,
  each side's spawn half mostly low, flat low center and start areas, flat water on the river
  and lake; same answer every call without allocating (and a pinned value); the ground mesh
  grid matches `HeightAt` within 1 m, and the wall ring mesh joins it vertex for vertex and
  matches `HeightAt` within 5 m + 2.5 % up to 1300 m (8 % above, out of planes' reach).

Test it alone: `./test.sh World`. Screenshot (not
committed): `./screenshot.sh modules/World/Godot/Main.tscn` (`World.png`).
