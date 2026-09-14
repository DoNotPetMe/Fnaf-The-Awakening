# Setup

## Requirements

- **Unity 6** — 6000.0 LTS or newer. `ProjectSettings/ProjectVersion.txt` names
  6000.0.32f1; if you have a different 6000.x, open it anyway and let Unity upgrade.
- Git. Git LFS is configured in `.gitattributes` but not currently needed — nothing
  binary is committed.

## First open

1. Clone and open the folder in Unity Hub. First import takes a few minutes while URP
   compiles its shader variants.

2. **Tools → Grotto → Build Facility Scene.** This does everything, in order: builds
   the render pipeline, regenerates the settings assets, then constructs the scene. It
   takes a few seconds.

3. Press **Play**. You land on the title screen, over a live view of the site.

### The render pipeline

Step 2 creates it for you, but it is worth knowing what it did, because it is the one
thing that makes the difference between the game and a magenta cave.

*Tools → Grotto → Rebuild Render Pipeline* writes three URP assets — `URP_Low`,
`URP_Medium`, `URP_High` — into `Assets/Game/Settings/Rendering`, assigns them across
the project's quality levels, and sets the Medium one as the default. Each is
configured for one specific kind of scene: a dark interior lit by a handful of small,
moving, shadow-casting lamps, viewed from a fixed seat.

That means HDR on, depth and opaque textures on (the water shader and the fog both read
them), one shadow cascade at short distance rather than four spread over ground nothing
occupies, eight per-object lights because the station has six, Forward+, and SSAO.

If you would rather configure it by hand, the shaders only require:

- **HDR** on — a cap lamp at intensity 3.2 in a 0.05-ambient room is a 60:1 range
- **Depth texture** and **opaque texture** on
- **Additional lights: per pixel**, limit 8

4. **Tools → Grotto → Validate Project** reports anything still missing — an absent
   layer, a shader that did not compile, a settings asset that was not written.

## If a package fails to resolve

The versions in `Packages/manifest.json` are the best known for Unity 6.0 but were not
verified against a live registry. If Package Manager reports one cannot be resolved,
open *Window → Package Manager*, find the package and install the version it offers.
The project needs:

| Package | Used for |
|---|---|
| `com.unity.render-pipelines.universal` | Everything visual |
| `com.unity.inputsystem` | All input; the old Input Manager is not used |
| `com.unity.ugui` | UI and TextMeshPro assemblies |
| `com.unity.test-framework` | The edit-mode tests |
| `com.unity.ai.navigation` | Not used at runtime; kept for future work |
| `com.unity.mathematics`, `com.unity.burst`, `com.unity.collections` | Referenced by the asmdefs |

## Input system

The project uses the new Input System exclusively, with actions built in code
(`GrottoInput`). If Unity asks whether to enable the new backend, say yes and let it
restart. If input does nothing, check *Project Settings → Player → Active Input
Handling* is **Input System Package (New)** or **Both**.

## Merging Unity YAML

`.gitattributes` marks scenes and assets for `unityyamlmerge`. To enable it:

```
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver "'<Unity>/Editor/Data/Tools/UnityYAMLMerge' merge -p %O %B %A %A"
```

In practice it matters less here than in most Unity projects: the scene contains no
meshes, materials or prefabs, so conflicts are rare and readable.

## Running the tests

*Window → General → Test Runner → EditMode → Run All.* They need no scene and no Play
Mode.

Outside Unity, and in CI:

```bash
python3 Tools/validate_project.py
```

This checks delimiter balance in every C# file with a scanner that understands
comments, char literals, verbatim strings, interpolated strings and nested literals
inside interpolation holes; validates every JSON and asmdef; resolves assembly
references and detects cycles; checks shader structure; and confirms the 32-entry layer
table. It is not a compiler, but it catches the class of mistake that otherwise costs a
round trip through the editor.

## Building

*Tools → Grotto → Build → Windows (development)* and friends. Builds run
`ProjectValidator` first and **refuse to build on a blocking problem** — a build that
succeeds and then renders magenta because the pipeline was unassigned costs far more
than one that fails in ten seconds with the reason.

For CI:

```
Unity -quit -batchmode -projectPath . \
      -executeMethod Grotto.Editor.BuildScript.BuildFromCommandLine \
      -grottoTarget StandaloneLinux64 -grottoDevelopment
```

## Troubleshooting

| Symptom | Cause |
|---|---|
| Everything is magenta | No URP asset assigned. See step 2. |
| The cave is empty on Play | `FacilityGeometrySpawner` has no layout. Run *Rebuild Settings Assets*. |
| Objects are all on layer Default | `ProjectSettings/TagManager.asset` did not import. Run *Validate Project*. |
| The monitor shows a flat picture | `Grotto/MonitorFeed` did not compile. Check the console; the feed still works untreated. |
| No input at all | Active Input Handling is set to the old backend. |
| Console will not open | Not a development build. Add `-grotto-devtools`. |
| Text is missing | `UIFactory.DefaultFont` found no built-in font. Rare; it falls back to an OS font. |
