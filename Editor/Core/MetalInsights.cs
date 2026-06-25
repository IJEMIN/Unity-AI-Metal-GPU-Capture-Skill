using System;
using System.Collections.Generic;
using System.Globalization;

namespace JeminLee.MetalGpuCaptureSkill.Editor
{
    /// <summary>Summed GPU cost of one URP category across a frame's render encoders.</summary>
    [Serializable]
    public class CategoryCost
    {
        public string category;
        public double ms;
        public double percent;   // of frame GPU time
        public int passCount;
    }

    /// <summary>One prioritized optimization insight (measurement-driven, URP-specific).</summary>
    [Serializable]
    public class MetalInsight
    {
        public string title;     // what the issue is
        public string evidence;  // the measured numbers behind it
        public string fix;       // concrete URP action
        public double impactMs;  // addressable cost (used for ranking)
        public bool quickWin;    // a toggle/setting change vs. a refactor
    }

    /// <summary>
    /// Deterministic, measurement-driven analysis of a MetalTraceSummary: groups render encoders into
    /// URP categories and derives prioritized optimization insights from the measured per-pass GPU
    /// time. No LLM involved (the "Ask AI" button layers explanation/Q&amp;A on top). Meaningful only
    /// when gpuTimingLoaded is true. Bottleneck classification from GPU counters is a separate step.
    /// </summary>
    public static class MetalInsights
    {
        // label substring -> URP category (first match wins; order matters).
        static readonly (string needle, string category)[] Map =
        {
            ("Shadow", "Shadows"),
            ("DrawMotionVectors", "Motion vectors"),
            ("SSAO", "SSAO"),
            ("Decal", "Decals"),
            ("RG_TAA", "Post: TAA"),
            ("Bloom", "Post: Bloom"),
            ("ColorGradingLUT", "Post: Color grading"),
            ("Lens Flare", "Post: Lens flare"),
            ("Blit Final Post", "Post: Final (FXAA/grain)"),
            ("Blit Post Processing", "Post: UberPost"),
            ("UberPost", "Post: UberPost"),
            ("CopyColor", "Copy: Opaque texture"),
            ("CopyDepth", "Copy: Depth texture"),
            ("RenderLoop.DrawSRPBatcher", "Geometry (opaque/transparent)"),
            ("DrawObjects", "Geometry (opaque/transparent)"),
            ("GUITexture", "UI / overlay"),
        };

        public static string Categorize(string label)
        {
            if (string.IsNullOrEmpty(label)) return "Other";
            foreach (var m in Map)
                if (label.IndexOf(m.needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return m.category;
            return "Other";
        }

        /// <summary>Groups gpuPasses by URP category, sorted by GPU ms descending.</summary>
        public static List<CategoryCost> Breakdown(MetalTraceSummary s)
        {
            var byCat = new Dictionary<string, CategoryCost>();
            if (s != null && s.gpuPasses != null)
            {
                foreach (var p in s.gpuPasses)
                {
                    string cat = Categorize(p.label);
                    if (!byCat.TryGetValue(cat, out CategoryCost cc))
                    {
                        cc = new CategoryCost { category = cat };
                        byCat[cat] = cc;
                    }
                    cc.ms += p.gpuMs;
                    cc.passCount++;
                }
            }
            double frame = (s != null && s.gpuFrameMs > 0) ? s.gpuFrameMs : 0;
            var list = new List<CategoryCost>(byCat.Values);
            foreach (CategoryCost cc in list)
                cc.percent = frame > 0 ? cc.ms / frame * 100.0 : 0;
            list.Sort((a, b) => b.ms.CompareTo(a.ms));
            return list;
        }

        /// <summary>Target GPU frame time (ms) for a frame rate, e.g. 60 fps -> 16.67 ms.</summary>
        public static double TargetMs(int fps) => fps > 0 ? 1000.0 / fps : 0;

        /// <summary>
        /// Builds the prioritized insight list. Ranked by addressable GPU ms; quick wins
        /// (toggles/settings) win near-ties. Returns at most <paramref name="max"/>.
        /// </summary>
        public static List<MetalInsight> TopInsights(MetalTraceSummary s, int max = 3)
        {
            var outp = new List<MetalInsight>();
            if (s == null || !s.gpuTimingLoaded || s.gpuFrameMs <= 0) return outp;

            double frame = s.gpuFrameMs;
            List<CategoryCost> bd = Breakdown(s);

            double Cat(string name)
            {
                CategoryCost c = bd.Find(x => x.category == name);
                return c != null ? c.ms : 0;
            }
            double CatPrefix(string prefix)
            {
                double t = 0;
                foreach (CategoryCost c in bd)
                    if (c.category.StartsWith(prefix, StringComparison.Ordinal)) t += c.ms;
                return t;
            }
            string Ms(double ms) => ms.ToString("F2", CultureInfo.InvariantCulture) + " ms";
            string Pct(double ms) => frame > 0 ? (ms / frame * 100.0).ToString("F0", CultureInfo.InvariantCulture) + "%" : "?";

            void Add(double impact, bool quick, string title, string evidence, string fix)
            {
                // Skip trivial contributors (< 0.25 ms and < 3% of frame).
                if (impact < 0.25 && impact / Math.Max(frame, 0.001) < 0.03) return;
                outp.Add(new MetalInsight { impactMs = impact, quickWin = quick, title = title, evidence = evidence, fix = fix });
            }

            double opaqueCopy = Cat("Copy: Opaque texture");
            Add(opaqueCopy, true,
                "Opaque Texture copy is enabled",
                "CopyColor (_CameraOpaqueTexture) = " + Ms(opaqueCopy) + " (" + Pct(opaqueCopy) + ")",
                "If no shader samples _CameraOpaqueTexture (refraction/distortion), turn OFF 'Opaque Texture' in the URP Asset.");

            double depthCopy = Cat("Copy: Depth texture");
            Add(depthCopy, true,
                "Depth Texture copy is enabled",
                "CopyDepth (_CameraDepthTexture) = " + Ms(depthCopy) + " (" + Pct(depthCopy) + ")",
                "If nothing samples _CameraDepthTexture, turn OFF 'Depth Texture' in the URP Asset.");

            double bloom = CatPrefix("Post: Bloom");
            Add(bloom, true,
                "Bloom chain is expensive",
                "Bloom = " + Ms(bloom) + " (" + Pct(bloom) + ") across the down/upsample mips",
                "Lower Bloom 'Max Iterations', enable downscale, or disable Bloom on low-end tiers; cost scales with resolution.");

            double taa = CatPrefix("Post: TAA");
            double motion = Cat("Motion vectors");
            Add(taa + motion, true,
                "TAA + motion vectors",
                "TAA = " + Ms(taa) + ", motion vectors = " + Ms(motion) + " (" + Pct(taa + motion) + " together)",
                "On low-end tiers switch anti-aliasing to FXAA — that also removes the motion-vector pass cost.");

            double ssao = Cat("SSAO");
            Add(ssao, true,
                "SSAO is costly",
                "SSAO = " + Ms(ssao) + " (" + Pct(ssao) + ")",
                "Reduce SSAO sample count, increase downsample, lower blur quality, or disable on low tiers.");

            double shadows = Cat("Shadows");
            Add(shadows, false,
                "Shadow rendering",
                "Shadows = " + Ms(shadows) + " (" + Pct(shadows) + ")",
                "Lower shadow cascade count and shadow distance; disable 'Cast Shadows' on small/distant meshes.");

            double decals = Cat("Decals");
            Add(decals, false,
                "Decals (DBuffer)",
                "Decals = " + Ms(decals) + " (" + Pct(decals) + ")",
                "Reduce decal projector count, or switch the Decal technique (Screen Space vs DBuffer).");

            double geo = Cat("Geometry (opaque/transparent)");
            Add(geo, false,
                "Opaque/transparent geometry",
                "Geometry = " + Ms(geo) + " (" + Pct(geo) + ") over " + s.drawCallCount + " draws this frame",
                "Improve batching (keep SRP Batcher compatibility, GPU instancing, atlas materials), cull aggressively, use LODs.");

            outp.Sort((a, b) =>
            {
                if (Math.Abs(a.impactMs - b.impactMs) >= 0.2) return b.impactMs.CompareTo(a.impactMs);
                return b.quickWin.CompareTo(a.quickWin);
            });
            if (outp.Count > max) outp = outp.GetRange(0, max);
            return outp;
        }
    }
}
