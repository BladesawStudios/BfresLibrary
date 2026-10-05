using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BfresLibrary.GX2;

namespace BfresLibrary.PlatformConverters
{
    internal class MaterialConverterBOTW : MaterialConverterBase
    {
        //Changes with Switch BOTW

        //RENDER INFO ADDED
        //gsys_render_state_mode
        //gsys_render_state_display_face
        //gsys_render_state_blend_mode
        //gsys_depth_test_enable
        //gsys_depth_test_write
        //gsys_depth_test_func
        //gsys_color_blend_rgb_src_func
        //gsys_color_blend_rgb_dst_func
        //gsys_color_blend_rgb_op
        //gsys_color_blend_alpha_src_func
        //gsys_color_blend_alpha_dst_func
        //gsys_color_blend_alpha_op
        //gsys_alpha_test_enable
        //gsys_alpha_test_func
        //gsys_color_blend_const_color
        //gsys_alpha_test_value

        //Wii U state words shared by every BotW material (stencil, polygon mode and logic op are never changed).
        private const uint DefaultPolygonControl = 0x280240;
        private const uint DefaultDepthControl = 0x49749700;

        private static readonly string[] SwitchRenderInfos =
        {
            "gsys_render_state_mode", "gsys_render_state_display_face", "gsys_render_state_blend_mode",
            "gsys_depth_test_enable", "gsys_depth_test_write", "gsys_depth_test_func",
            "gsys_color_blend_rgb_src_func", "gsys_color_blend_rgb_dst_func", "gsys_color_blend_rgb_op",
            "gsys_color_blend_alpha_src_func", "gsys_color_blend_alpha_dst_func", "gsys_color_blend_alpha_op",
            "gsys_alpha_test_enable", "gsys_alpha_test_func", "gsys_color_blend_const_color", "gsys_alpha_test_value",
        };

        internal override void ConvertToWiiUMaterial(Material material)
        {
            if (!material.RenderInfos.ContainsKey("gsys_render_state_mode"))
                return;

            var state = new RenderState();
            state.FlagsMode = GetString(material, "gsys_render_state_mode") switch
            {
                "opaque" => RenderStateFlagsMode.Opaque,
                "mask" => RenderStateFlagsMode.AlphaMask,
                "translucent" => RenderStateFlagsMode.Translucent,
                _ => RenderStateFlagsMode.Custom,
            };
            var blendMode = GetString(material, "gsys_render_state_blend_mode");
            state.FlagsBlendMode = blendMode == "color" ? RenderStateFlagsBlendMode.Color
                : blendMode == "logic" ? RenderStateFlagsBlendMode.Logical : RenderStateFlagsBlendMode.None;

            var face = GetString(material, "gsys_render_state_display_face");
            state.PolygonControl = new PolygonControl { Value = DefaultPolygonControl };
            state.PolygonControl.CullBack = face == "front" || face == "none";
            state.PolygonControl.CullFront = face == "back" || face == "none";

            state.DepthControl = new DepthControl { Value = DefaultDepthControl };
            state.DepthControl.DepthTestEnabled = GetString(material, "gsys_depth_test_enable") == "true";
            state.DepthControl.DepthWriteEnabled = GetString(material, "gsys_depth_test_write") == "true";
            state.DepthControl.DepthFunc = Lookup(CompareFunction, GetString(material, "gsys_depth_test_func"), GX2.GX2CompareFunction.LessOrEqual);

            state.AlphaControl = new AlphaControl();
            state.AlphaControl.AlphaTestEnabled = GetString(material, "gsys_alpha_test_enable") == "true";
            state.AlphaControl.AlphaFunc = Lookup(CompareFunction, GetString(material, "gsys_alpha_test_func"), GX2.GX2CompareFunction.GreaterOrEqual);
            state.AlphaRefValue = GetSingles(material, "gsys_alpha_test_value")?.FirstOrDefault() ?? 0.5f;

            state.ColorControl = new ColorControl { LogicOp = GX2.GX2LogicOp.Copy };
            state.ColorControl.BlendEnableMask = (byte)(state.FlagsBlendMode == RenderStateFlagsBlendMode.Color ? 1 : 0);

            state.BlendTarget = 0;
            state.BlendControl = new BlendControl();
            state.BlendControl.ColorSourceBlend = Lookup(BlendFunction, GetString(material, "gsys_color_blend_rgb_src_func"), GX2.GX2BlendFunction.SourceAlpha);
            state.BlendControl.ColorDestinationBlend = Lookup(BlendFunction, GetString(material, "gsys_color_blend_rgb_dst_func"), GX2.GX2BlendFunction.OneMinusSourceAlpha);
            state.BlendControl.ColorCombine = Lookup(BlendCombine, GetString(material, "gsys_color_blend_rgb_op"), GX2.GX2BlendCombine.Add);
            state.BlendControl.AlphaSourceBlend = Lookup(BlendFunction, GetString(material, "gsys_color_blend_alpha_src_func"), GX2.GX2BlendFunction.One);
            state.BlendControl.AlphaDestinationBlend = Lookup(BlendFunction, GetString(material, "gsys_color_blend_alpha_dst_func"), GX2.GX2BlendFunction.Zero);
            state.BlendControl.AlphaCombine = Lookup(BlendCombine, GetString(material, "gsys_color_blend_alpha_op"), GX2.GX2BlendCombine.Add);
            state.BlendControl.SeparateAlphaBlend = true;

            var constColor = GetSingles(material, "gsys_color_blend_const_color");
            state.BlendColor = constColor != null && constColor.Length >= 4
                ? new Syroot.Maths.Vector4F(constColor[0], constColor[1], constColor[2], constColor[3])
                : new Syroot.Maths.Vector4F(0, 0, 0, 0);

            material.RenderState = state;

            //Wii U keeps the alpha reference in the render state; the shader parameter stays zero.
            if (material.ShaderParams.TryGetValue("gsys_alpha_test_ref_value", out var param))
                param.DataValue = 0f;

            foreach (var key in SwitchRenderInfos)
                if (material.RenderInfos.ContainsKey(key))
                    material.RenderInfos.RemoveKey(key);
        }

        private static string GetString(Material material, string key) =>
            material.RenderInfos.TryGetValue(key, out var info) && info.Type == RenderInfoType.String ? info.GetValueStrings().FirstOrDefault() : null;

        private static float[] GetSingles(Material material, string key) =>
            material.RenderInfos.TryGetValue(key, out var info) && info.Type == RenderInfoType.Single ? info.GetValueSingles() : null;

        private static T Lookup<T>(Dictionary<T, string> map, string value, T fallback)
        {
            foreach (var pair in map)
                if (pair.Value == value)
                    return pair.Key;
            return fallback;
        }

        internal override void ConvertToSwitchMaterial(Material material)
        {
            material.SetRenderInfo("gsys_render_state_mode", GetRenderState(material.RenderState));
            material.SetRenderInfo("gsys_render_state_display_face", GetCullState(material.RenderState));
            material.SetRenderInfo("gsys_render_state_blend_mode", GetBlendMode(material.RenderState));
            material.SetRenderInfo("gsys_depth_test_enable", RenderInfoBoolString(
                material.RenderState.DepthControl.DepthTestEnabled));
            material.SetRenderInfo("gsys_depth_test_write", RenderInfoBoolString(
                material.RenderState.DepthControl.DepthWriteEnabled));
            material.SetRenderInfo("gsys_depth_test_func", CompareFunction[
                  material.RenderState.DepthControl.DepthFunc]);

            material.SetRenderInfo("gsys_color_blend_rgb_src_func", BlendFunction[
                  material.RenderState.BlendControl.ColorSourceBlend]);
            material.SetRenderInfo("gsys_color_blend_rgb_dst_func", BlendFunction[
                  material.RenderState.BlendControl.ColorDestinationBlend]);
            material.SetRenderInfo("gsys_color_blend_rgb_op", BlendCombine[
                  material.RenderState.BlendControl.ColorCombine]);

            material.SetRenderInfo("gsys_color_blend_alpha_src_func", BlendFunction[
                  material.RenderState.BlendControl.AlphaSourceBlend]);
            material.SetRenderInfo("gsys_color_blend_alpha_dst_func", BlendFunction[
                  material.RenderState.BlendControl.AlphaDestinationBlend]);
            material.SetRenderInfo("gsys_color_blend_alpha_op", BlendCombine[
                  material.RenderState.BlendControl.AlphaCombine]);

            material.SetRenderInfo("gsys_alpha_test_enable", RenderInfoBoolString(
                material.RenderState.AlphaControl.AlphaTestEnabled));
            material.SetRenderInfo("gsys_alpha_test_func", CompareFunction[
                  material.RenderState.AlphaControl.AlphaFunc]);
            material.SetRenderInfo("gsys_color_blend_const_color", new float[4] { 0, 0, 0, 0 });
            material.SetRenderInfo("gsys_alpha_test_value",
                  material.RenderState.AlphaRefValue);

            SetAlphaRefParam(material, material.RenderState.AlphaRefValue);
            material.RenderState = null;
        }

        /// <summary>
        /// Switch keeps the alpha reference in the gsys_alpha_test_ref_value parameter. Materials that lack it get it
        /// right after uking_edit_proc_discard_scale (or at the end), depending only on itself.
        /// </summary>
        private static void SetAlphaRefParam(Material material, float value)
        {
            const string name = "gsys_alpha_test_ref_value";
            if (material.ShaderParams.TryGetValue(name, out var existing))
            {
                existing.DataValue = value;
                return;
            }

            var ordered = material.ShaderParams.Values.ToList();
            var dependNames = ordered.Select(x => (Depend: ordered[x.DependIndex].Name, Depended: ordered[x.DependedIndex].Name)).ToList();
            int insertAt = ordered.FindIndex(x => x.Name == "uking_edit_proc_discard_scale") + 1;
            if (insertAt <= 0) insertAt = ordered.Count;

            var param = new ShaderParam { Name = name, Type = ShaderParamType.Float, DataValue = value };
            ordered.Insert(insertAt, param);
            dependNames.Insert(insertAt, (name, name));

            var dict = new ResDict<ShaderParam>();
            foreach (var p in ordered)
                dict.Add(p.Name, p);
            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].DependIndex = (ushort)ordered.FindIndex(x => x.Name == dependNames[i].Depend);
                ordered[i].DependedIndex = (ushort)ordered.FindIndex(x => x.Name == dependNames[i].Depended);
            }
            material.ShaderParams = dict;
        }

        private string GetCullState(RenderState state)
        {
            if (state.PolygonControl.CullBack && state.PolygonControl.CullFront)
                return "none";
            else if (state.PolygonControl.CullBack)
                return "front";
            else if (state.PolygonControl.CullFront)
                return "back";
            return "both";
        }

        private string GetRenderState(RenderState state)
        {
            if (state.FlagsMode == RenderStateFlagsMode.Opaque)
                return "opaque";
            else if (state.FlagsMode == RenderStateFlagsMode.AlphaMask)
                return "mask";
            else if (state.FlagsMode == RenderStateFlagsMode.Translucent)
                return "translucent";
            else
                return "custom";
        }

        private string GetBlendMode(RenderState state)
        {
            if (state.FlagsBlendMode == RenderStateFlagsBlendMode.Color)
                return "color";
            else if (state.FlagsBlendMode == RenderStateFlagsBlendMode.Logical)
                return "logic";
            else
                return "none";
        }

        Dictionary<GX2.GX2BlendCombine, string> BlendCombine = new Dictionary<GX2.GX2BlendCombine, string>()
        {
            { GX2.GX2BlendCombine.Add, "add" },
            { GX2.GX2BlendCombine.Maximum, "max" },
            { GX2.GX2BlendCombine.Minimum, "min" },
        };


        Dictionary<GX2.GX2CompareFunction, string> CompareFunction = new Dictionary<GX2.GX2CompareFunction, string>()
        {
            { GX2.GX2CompareFunction.Always, "always" },
            { GX2.GX2CompareFunction.Never, "never" },
            { GX2.GX2CompareFunction.GreaterOrEqual, "gequal" },
            { GX2.GX2CompareFunction.LessOrEqual, "lequal" },
            { GX2.GX2CompareFunction.Equal, "equal" },
            { GX2.GX2CompareFunction.Less, "less" },
            { GX2.GX2CompareFunction.Greater, "greater" },
            { GX2.GX2CompareFunction.NotEqual, "noequal" },
        };

        Dictionary<GX2.GX2BlendFunction, string> BlendFunction = new Dictionary<GX2.GX2BlendFunction, string>()
        {
            { GX2.GX2BlendFunction.OneMinusSourceAlpha, "one_minus_src_alpha" },
            { GX2.GX2BlendFunction.SourceAlpha, "src_alpha" },
            { GX2.GX2BlendFunction.SourceColor, "src_color" },
            //Todo confirm these 2
            { GX2.GX2BlendFunction.ConstantAlpha, "const_alpha" },
            { GX2.GX2BlendFunction.ConstantColor, "const_color" },

            { GX2.GX2BlendFunction.DestinationAlpha, "dst_alpha" },
            { GX2.GX2BlendFunction.One, "one" },
            { GX2.GX2BlendFunction.Zero, "zero" },
        };
    }
}
