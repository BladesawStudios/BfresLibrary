using System.Collections.Generic;
using System.Linq;

namespace BfresLibrary.PlatformConverters
{
    /// <summary>
    /// Converts shader parameter, texture SRT, color and texture pattern animations between the Wii U (FSHU/FTXP) and
    /// Switch (FMAA) layouts. Shader parameter style animations share one layout; texture patterns differ in where the
    /// base texture of a pattern without a curve is stored.
    /// </summary>
    public static class MaterialAnimConverter
    {
        public const string ShaderParamSuffix = "_fsp";
        public const string TexSrtSuffix = "_fts";
        public const string ColorSuffix = "_fcl";
        public const string TexPatternSuffix = "_ftp";

        private const int None = 0xFFFF;

        /// <summary>
        /// Wii U texture patterns keep a base texture index per pattern in <see cref="MaterialAnimData.BaseDataList"/>
        /// (the first key plus the curve offset for animated patterns). Switch stores a constant for every pattern
        /// without a curve and points <see cref="PatternAnimInfo.BeginConstant"/> at it.
        /// </summary>
        public static void TexPatternToSwitch(MaterialAnim anim)
        {
            foreach (var data in anim.MaterialAnimDataList)
            {
                var constants = new List<AnimConstant>();
                var patterns = data.PatternAnimInfos ?? new List<PatternAnimInfo>();
                for (int i = 0; i < patterns.Count; i++)
                {
                    if (patterns[i].CurveIndex >= 0)
                    {
                        patterns[i].BeginConstant = None;
                        continue;
                    }
                    int texture = data.BaseDataList != null && i < data.BaseDataList.Length ? data.BaseDataList[i] : 0;
                    patterns[i].BeginConstant = (ushort)constants.Count;
                    constants.Add(new AnimConstant { AnimDataOffset = 0, Value = texture });
                }
                data.Constants = constants;
            }
        }

        public static void TexPatternToWiiU(MaterialAnim anim)
        {
            foreach (var data in anim.MaterialAnimDataList)
            {
                var patterns = data.PatternAnimInfos ?? new List<PatternAnimInfo>();
                var baseData = new ushort[patterns.Count];
                for (int i = 0; i < patterns.Count; i++)
                {
                    var pattern = patterns[i];
                    if (pattern.CurveIndex >= 0 && pattern.CurveIndex < data.Curves.Count)
                    {
                        var curve = data.Curves[pattern.CurveIndex];
                        baseData[i] = (ushort)((int)curve.Keys[0, 0] + curve.Offset.Int32);
                    }
                    else if (pattern.BeginConstant != None && data.Constants != null && pattern.BeginConstant < data.Constants.Count)
                        baseData[i] = (ushort)data.Constants[pattern.BeginConstant].Value.Int32;
                    pattern.BeginConstant = None;
                }
                data.BaseDataList = baseData;
                data.Constants = new List<AnimConstant>();
            }
        }

        /// <summary>
        /// The kind suffix of a Switch material animation name, or null if it has none.
        /// </summary>
        public static string KindOf(string name)
        {
            foreach (var suffix in new[] { ShaderParamSuffix, TexSrtSuffix, ColorSuffix, TexPatternSuffix, VisibilityAnimConverter.SwitchSuffix })
                if (name.EndsWith(suffix))
                    return suffix;
            return null;
        }

        public static string StripKind(string name)
        {
            var kind = KindOf(name);
            return kind == null ? name : name.Substring(0, name.Length - kind.Length);
        }
    }
}
