# Texture Array Pipeline

[![Unity 2022.3+](https://img.shields.io/badge/Unity-2022.3%2B-black?logo=unity)](https://unity.com/releases/editor/whats-new/2022.3.0)
[![URP 14](https://img.shields.io/badge/URP-14.0-blue)](https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/index.html)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Version](https://img.shields.io/badge/version-1.0.0-orange)]()

An editor pipeline for authoring and managing `Texture2DArray` assets in URP projects. Combine dozens of individual textures into a single GPU array, reduce draw calls through batching, and keep your build clean — source textures and the definition asset never enter the final player build.

⚠️ **This is not a production-ready solution.** See [Limitations](#limitations) for the full list of constraints.

---

## Why

When rendering many objects that share the same texture set but differ in which texture they display — think building facades, tile variants, character skins — Unity issues a separate draw call per unique material. The standard fix (texture atlases) requires UV remapping and complicates authoring. `Texture2DArray` keeps UVs intact and lets the GPU pick the right slice on the fly, enabling GPU instancing across the entire set.

**Without Texture Array Pipeline**, each variant needs its own material → its own texture → its own draw call:

### Stats comparison

| Before | After |
|--------|-------|
| <img width="300" height="226" alt="image" src="https://github.com/user-attachments/assets/419217a2-d7b4-43b1-9bd2-2d2fbe266ce0" /> | <img width="299" height="227" alt="image" src="https://github.com/user-attachments/assets/9a6005f0-af23-4044-93ed-b85408c5b475" /> |

### RenderDoc comparison

| Before | After |
|--------|-------|
| <img width="413" height="305" alt="image" src="https://github.com/user-attachments/assets/7ff1d236-cc6a-473e-950d-8fa21f64edf5" /> | <img width="414" height="304" alt="image" src="https://github.com/user-attachments/assets/b4647ef1-8048-4760-8659-abc42fef4dfb" /> |

---

## Features

- **One-click `Texture2DArray` builder** — drag textures into the definition asset, press *Generate*, done.
- **Automatic format normalization** — Quick Mode auto-detects the best `GraphicsFormat`; Advanced Mode lets you pin an exact format.
- **Automatic resolution normalization** — mismatched source textures are reimported to the target resolution before the array is assembled.
- **GPU-accelerated scaling** — when sizes differ, a `RenderTexture` blit path scales textures on the GPU and optionally re-compresses them.
- **Editor-only definition asset** — `TextureArrayDefinition` carries `HideFlags.DontSaveInBuild`. The ScriptableObject and the original source textures are **never included in player builds**; only the embedded `Texture2DArray` sub-asset is referenced by materials and therefore ends up in the build.
- **Mesh Baker** — an editor window (`Tools → Texture Array → Mesh Baker`) that encodes the array slice index into mesh vertex color (channel R, 0–255), re-exports the FBX via Unity's FBX Exporter, and remaps materials on the prefab automatically.
- **Custom URP shaders** — `LitArray` (full PBR) and `SimpleLitArray` (Blinn-Phong) read the slice index from `vertexColor.r` in the fragment shader, enabling GPU instancing with zero per-instance material overhead.
- **Pre-build validation** — format mismatches and missing mip chains are reported as errors before the array is written.

---

## How It Works

### 1. The definition asset

`TextureArrayDefinition` is a `ScriptableObject` that stores:
- A list of source texture **GUIDs** (not direct `Texture2D` references, so moves/renames don't break the list).
- Build settings: target resolution, `GraphicsFormat`, mip/filter/wrap options, normal-map flags.
- The timestamp of the last successful build (used for stale detection).

Because it is flagged `DontSaveInBuild`, Unity's build pipeline strips it entirely. Only the embedded `Texture2DArray` sub-asset reaches the player.

### 2. The build pipeline (`TextureArrayBuilder`)

```
Source GUIDs
    │
    ▼
ResolveTextures          — GUID → Texture2D, report missing
    │
    ▼
NormalizeImporters       — set maxTextureSize, fix NormalMap type, reimport
    │
    ▼
NormalizeCompression     — Quick: pick highest-quality format present
                           Advanced: enforce a specific GraphicsFormat
    │
    ▼
Validate                 — formats must match; mip chains must exist
    │
    ▼
BuildArray
    ├─ Fast path          — Graphics.CopyTexture (GPU, zero CPU memory)
    └─ Scaling path       — Graphics.Blit → ReadPixels → CompressTexture
    │
    ▼
EmbedSubAsset            — AddObjectToAsset, replace existing sub-asset
    │
    ▼
RecordBuildTime          — persist ticks for stale check
```

**Why this is better than a texture atlas:**

| | Atlas | Texture2DArray |
|---|---|---|
| UV remapping | ❌ Required | ✅ Not needed |
| GPU instancing | ❌ Limited (per material) | ✅ One material, many slices |
| Mip bleeding | ❌ Needs padding/fixes | ✅ No issues |
| Source textures in build | ❌ Always included | ✅ Only array included |
| Authoring complexity | ❌ High | ✅ Low |

### 3. Per-vertex slice indexing

Each mesh submesh gets a **vertex color** written into channel R (integer 0–255, stored as `R / 255.0` in the color attribute). The vertex shader passes it to the fragment shader via `Varyings.color`. The fragment shader decodes it:

```hlsl
// LitInput.hlsl
int DecodeArrayIndexFromColor(float4 color)
{
    return (int)(color.r * 255.0 + 0.5);
}

// Usage in fragment
int arrayIndex = DecodeArrayIndexFromColor(input.color);
half4 albedo = SAMPLE_TEXTURE2D_ARRAY(_BaseMapArray, sampler_BaseMapArray, uv, arrayIndex);
```

This means a **single material** covers the entire set. GPU instancing is unrestricted because there is no per-instance texture difference — the slice is baked into the geometry.

### 4. Mesh Baker

The Mesh Baker window automates the per-vertex encoding:

1. You assign a `Renderer` from a prefab asset or prefab instance.
2. It scans all submeshes for materials using `LitArray` / `SimpleLitArray`.
3. You pick a slice index (0–N) per slot via an int slider — the result is previewed live in the Scene View against a hidden working clone.
4. **Refresh Slots** re-scans the Renderer; **Reset All Slots** zeros all indices.
5. **Export to FBX** writes the indices into the vertex color channel R, overwrites the source FBX via Unity's **FBX Exporter**, and remaps materials on the prefab asset.

---

## Usage

### Step 1 — Create a definition asset

*Right-click in the Project window → Create → TextureArrayDefinition*

### Step 2 — Populate and build

1. Open the asset inspector.
2. Drag your source textures into the list (or use the **+** button).
3. Choose **Quick Mode** (pick a resolution preset) or **Advanced Mode** (full control over resolution, `GraphicsFormat`, mipmaps, filter, wrap, normal-map settings).
4. Click **Generate Texture Array**.

The inspector shows a green status when the array is up to date and an orange *Stale* warning when any source texture has been modified since the last build.

### Step 3 — Assign to a material

1. Create a material using **Universal Render Pipeline/LitArray** or **Universal Render Pipeline/SimpleLitArray**.
2. Drag the definition asset's embedded `Texture2DArray` sub-asset into the **Albedo Array** slot.

### Step 4 — Prepare the prefab

Before opening the Mesh Baker, configure your prefab so that:

- Each submesh's material slot already uses **LitArray** or **SimpleLitArray**.
- Materials are assigned **in the order that matches the desired slice mapping** — the Baker reads the material list as-is and only surfaces slots whose material uses one of the supported shaders.

### Step 5 — Bake slice indices into meshes

1. Open **Tools → Texture Array → Mesh Baker**.
2. Drag the prefab (or a prefab instance from the scene) into the **Renderer** field — the Baker will resolve the source FBX and the prefab asset path automatically.
3. The window lists every submesh slot whose material uses `LitArray` / `SimpleLitArray`. Use the **int slider** next to each slot to set the desired slice index (0–N). The result is previewed live in the Scene View immediately — no button needed.
4. Click **Save Changes** — the Baker writes the slice indices into the vertex color channel R of the working mesh, overwrites the source FBX via Unity's FBX Exporter, and remaps materials on the prefab asset.

### Step 5 — Stress test (optional)

The `_Project/Scenes/PrefabStressTest` sample scene contains a `PrefabStressTest` component. Right-click it in the Inspector and choose **Spawn Default** (individual materials) or **Spawn Array** (single shared material) to compare GPU stats directly in the Editor.

---

## Installation

### Via Unity Package Manager (UPM) — Git URL

1. Open **Window → Package Manager**.
2. Click the **+** button → *Add package from git URL…*
3. Enter:

```
https://github.com/dexsper/TextureArrayPipeline.git?path=src/Assets/Plugins/TextureArrayPipeline
```

4. Click **Add**. UPM will download the package and resolve its dependencies automatically.

### Via `manifest.json`

Add the following entry to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.dexsper.texarraypipeline": "https://github.com/dexsper/TextureArrayPipeline.git?path=src/Assets/Plugins/TextureArrayPipeline"
  }
}
```

---

## Requirements

| Requirement | Minimum version |
|-------------|-----------------|
| **Unity Editor** | 2022.3 LTS |
| **Universal Render Pipeline** | 14.0.12 (`com.unity.render-pipelines.universal`) |
| **FBX Exporter** | 4.2.1 (`com.unity.formats.fbx`) |

Both URP and FBX Exporter are declared as hard dependencies in `package.json` and will be installed automatically by UPM.

---

## Limitations

- **Shaders support `Texture2DArray` on Base Map only.** Metallic, Normal, Occlusion, Emission, and Detail maps are standard `Texture2D` properties shared across all slices. This is sufficient for use cases where only the albedo varies per object (building facades, color variants, etc.), but not for full per-slice PBR.
- **Maximum 256 slices per array** — the slice index is encoded as a single `uint8` (vertex color R channel, 0–255).
- **Desktop / console only for the fast build path** — `Graphics.CopyTexture` requires `CopyTextureSupport` on the platform. The scaling path is used as a fallback but requires `RenderTexture` support.
- **Meta pass and Universal2D pass sample slice 0** — lightmap baking and the 2D renderer will always use the first texture in the array; multi-slice baking is not supported.
- **FBX Exporter rewrites the source FBX** — the Mesh Baker overwrites the original `.fbx` file on disk. Keep source files in version control before baking.

---

## License

MIT © [Dexsper](https://github.com/dexsper)
