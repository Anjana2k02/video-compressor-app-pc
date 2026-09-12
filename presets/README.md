# Presets

Versioned social destination presets. Presets are **data**, not code: the app loads and validates every
`*.json` file in this folder at startup ([PresetCatalog](../src/VideoOptimizer.Core/PresetCatalog.cs)). An
invalid file is reported to the user and skipped; the remaining valid presets still load.

Current presets:

| File | Id | Target | Audio | Delivery caps |
|------|----|--------|-------|---------------|
| `instagram-story.json` | `instagram-story` | 1080×1920 (9:16) | AAC 160 kbps | — |
| `tiktok.json` | `tiktok` | 1080×1920 (9:16) | AAC 160 kbps | — |
| `whatsapp-status.json` | `whatsapp-status` | 720×1280 (9:16) | AAC 128 kbps | 30 fps, 2 Mbps VBV |
| `whatsapp-status-hd.json` | `whatsapp-status-hd` | 1080×1920 (9:16) | AAC 128 kbps | 30 fps, 4 Mbps VBV |

Optional `video.maxFps` and `video.maxBitrate` are **delivery caps**: platforms like WhatsApp re-compress
uploads that exceed their limits, so delivering inside the envelope means the platform's own transcoder does
little or no further damage. An FPS cap is applied only when the source exceeds it, and the strategy
explanation always names the reduction — never silent.

Schema (`schemaVersion: 1`):

```json
{
  "schemaVersion": 1,
  "id": "instagram-story",
  "name": "Instagram Story / Reel",
  "lastReviewed": "2026-09-12",
  "video": { "preferredWidth": 1080, "preferredHeight": 1920, "aspectRatio": "9:16", "codec": "h264", "pixelFormat": "yuv420p" },
  "audio": { "codec": "aac", "sampleRate": 48000, "bitrate": 160000 },
  "quality": { "smallCrf": 23, "recommendedCrf": 20, "maximumCrf": 17 },
  "rules": { "preventUpscaling": true, "preserveSourceFps": true }
}
```

Platform requirements change over time. Each preset carries `lastReviewed`; treat these as reasonable
defaults, not a permanent compliance guarantee. The build copies this folder next to the app executable.
