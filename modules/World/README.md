# World

The place you fly over: a 2 km x 2 km playable square of cartoon countryside ("Porco Rosso
meets Sea of Thieves"): a mostly open, flyable patchwork of green and golden fields with
hedges, rolling hills, two small rocky mountains with snowy caps, a winding river and a lake,
clumps of low-poly trees, under a bright stylized sky. All around it a cartoon mountain range:
forested, grassy foothills start at the edge and the range climbs over about 2 km to a main ridge
(never under 1380 m, saddles included) with rows of individual peaks of about 1500-2600 m on and
behind it (pointed, ridged and broad), valleys between them that narrow and rise toward the ridge,
rock with soft strata higher up and snow caps on the peaks, cel-shaded and outlined, the far rows
fading into haze. Engines lose their climbing power at about 1000 m, so no plane gets over or
around it. Planes can hit the ground and the mountains: use `Terrain.HeightAt` for where they are.

- **Contract** (`Contract/`), no messages yet:
  - `WorldBounds`, constants: `HalfSize` (1000 m): the playable square spans
    `-HalfSize..HalfSize` on X and Z around the origin. Every module uses it for the map's
    size; none keeps its own copy. `GroundY` (0 m) is the lowest ground (the water surface);
    `MaxHillHeight` is the highest point, `Terrain.MaxHeight`.
  - `Terrain`, static, pure, deterministic and allocation-free (safe per tick):
    - `HeightAt(x, z)`: height (m) of the ground, or of the water surface on the river and
      lake, exactly as drawn (the meshes are built from it); always `WaterLevel..MaxHeight`.
      Defined everywhere: beyond the square's edge it is the mountain range, so flying into
      it is a crash like any ground.
    - `MaxHeight` (2600 m): the top of the range's tallest peak (at (150, -3700), north of the
      map); nothing is higher. `InnerMaxHeight` (200 m): the highest point inside the
      playable square (the top of its tallest mountain).
    - `WallMinHeight` (1380 m) and `WallReach` (2480 m): every way out of the square (any
      path, in any direction) crosses the range's main ridge, ground at least `WallMinHeight`
      high, within `WallReach` beyond the edge; behind it the ground stays above 1100 m.
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

  The range (beyond the edge only; inside it adds nothing; distances from the square, rounded at
  its corners): the inner land fades into it over the first 500 m. A slope rises from 0 at the
  edge, easing in, then evenly, rounding over at the main ridge's crest; the ridge line wanders
  2150..2480 m out and its crest is 1380..1540 m high (higher where it is farther out, so the
  climb is about as steep everywhere: under 45 degrees for most of the range). Two valleys run
  out from each side (the river's own valley among them on the north and south), cutting into
  the slope, narrowing and rising toward the ridge, where they end. Behind the ridge the ground
  dips by up to 220 m into high valleys. Peaks stand on the slope in hashed rows (foothills about
  380 m out, 50..140 m tall; hills 950 m out; a row 250 m behind the ridge line; and rows 3, 4.4
  and 5.8 km out), each pointed, ridged (long along the range) or broad, blended into its
  neighbors, tops under 2440 m; the tallest (2600 m) stands behind the north ridge. Slopes stay
  under 60 degrees below 1400 m (about 58 at most anywhere); steepest just under the summits.
- **Core** (`Core/`): `WorldModule`, the entry class (`IModule`). It has no rules yet: it
  only counts steps (no allocations), so the shell loads the scene. `WallMesh`: the layout of
  the range's ring mesh (shared by `WorldView` and the tests): square rings around the playable
  square, each `Segments` (4 x `GridCells`) vertices, ring 0 the ground mesh's border; rings
  every 8 m up to 400 m out (the foothills), then each 5 % farther apart out to 6 km (denser
  near the edge, coarser far away).
- **Godot** (`Godot/`): `Main.tscn` with `WorldView.cs`, which builds the ground mesh
  (one surface, `Terrain.GridCells` squared cells, vertices at `Terrain.HeightAt`, UV.x the
  height above water or minus the water depth), the range's ring mesh (one surface, laid out by
  `WallMesh`, vertices at `Terrain.HeightAt`, same UVs) and the trees (three `MultiMesh`es: round trees
  and poplars, seeded so the layout never changes; clumps on dry land away from shores,
  below 110 m and off steep slopes, so none on water or peaks; and low-poly pines (stacked
  cones) in clumps on the range's foothills, thinning out with height up to 950 m, none on water
  or slopes over about 40 degrees) in `_Ready`. No SDFGI.
  Few draw calls: ground, range ring, round trees, poplars and pines, each a toon pass plus an
  outline pass, and the sky; four materials (ground, range, trees, pines) and one outline each.
  - `Toon.gdshaderinc`: cel lighting (shadow / half-lit / lit bands), included by
    `Ground.gdshader` (fields and hedges on the low land, meadow on hills, banded rock with
    flat low-poly facets on high or steep ground, pale rock above 150 m and snow above about
    190 m; on the range's ring, through its uniforms, no fields but meadow mottled with darker
    forest green up to about 950 m, rock from about 700 m (400 m on steep ground) in soft 70 m
    strata, pale rock from about 1420 m and snow caps from about 1450-1640 m (the line wanders),
    less on steep rock; sandy shores and the water where UV.x < 0: bright blue, deeper offshore, soft
    breathing foam at the shore, gently drifting animated ripples) and `Tree.gdshader`
    (vertex colors).
  - `Outline.gdshader`: ink lines by inverted hull, a fixed pixel width on screen.
  - `Sky.gdshader`: gradient sky, sun halo, two-tone cartoon clouds. The `WorldEnvironment`
    adds depth fog (900 m to 10 km, a bluish haze the far rows fade into)
    and gentle glow; the warm `Sun` casts soft shadows.
  - `PreviewCamera` frames the scene when it runs alone (screenshots, the editor) from a
    plane's height (300 m up over the Allies' half at (150, 600), looking north across the map
    at the range); it is removed when the shell loads the scene (or another scene instances it),
    so the player's camera is used. `AlongRange.tscn` instances `Main.tscn` with its own camera
    300 m up near the east edge, looking north along the eastern range (screenshots only; the
    shell loads only `Main.tscn`).
  - Colors and light energies were tuned on the screenshot renderer (Compatibility/OpenGL).
  Other modules may copy the shaders' approach but must not reference these files.
- **Tests** (`Tests/`): `WorldModuleTests`: starts and ticks without allocating, is the only
  entry class, targets net10.0, no Godot in Core or Contract, `HalfSize` is 1000 (a 2 km
  square). `TerrainTests`: heights within `WaterLevel..MaxHeight` everywhere (out to 7 km beyond the edge), the
  tallest peak's top is `MaxHeight`; inside the square at most `InnerMaxHeight`, reached, and
  the inner land unchanged (a pinned sum over a 4 m grid); the range: low just inside and at the
  edge, foothills (averaging 50..400 m, none over 400 m within 500 m out), nothing over 1100 m
  within 1 km out; a continuous ridge (3600 rays from the center and rays straight out from
  every 8 m of the edge each meet ground at least `WallMinHeight` within `WallReach`), over
  1100 m behind it; no slope over 60 degrees below 1400 m (none over 65 anywhere, at least 75 %
  at 45 or gentler, out to 3.5 km); many tops (1500..2600 m) in rows near, middle and far, a
  ridge with saddles hundreds of meters under its peaks; on each side a valley that rises and
  narrows toward the ridge; the inner mountains where documented (tops highest), open rolling land elsewhere,
  each side's spawn half mostly low, flat low center and start areas, flat water on the river
  and lake; same answer every call without allocating (and a pinned value); the ground mesh
  grid matches `HeightAt` within 1 m, and the range's ring mesh joins it vertex for vertex and,
  out past the ridge, matches `HeightAt` within 5 m + 2.5 % up to 1300 m (8 % above, out of
  planes' reach).

Test it alone: `./test.sh World`. Screenshots (not
committed): `./screenshot.sh modules/World/Godot/Main.tscn` (`World.png`) and
`./screenshot.sh modules/World/Godot/AlongRange.tscn WorldAlongRange`.
