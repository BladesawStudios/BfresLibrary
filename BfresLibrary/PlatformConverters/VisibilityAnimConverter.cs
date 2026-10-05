using System.Collections.Generic;
using System.Linq;

namespace BfresLibrary.PlatformConverters
{
    /// <summary>
    /// Converts material visibility animations between the Wii U FVIS form and the Switch FMAA form ("_fvm").
    /// </summary>
    public static class VisibilityAnimConverter
    {
        public const string SwitchSuffix = "_fvm";

        private const ushort MaterialTypeFlag = 0x100;
        private const int None = 0xFFFF;

        /// <summary>
        /// Builds a Switch material animation from a Wii U material visibility animation. Each material gets either one
        /// curve (with no base value) or one constant holding its base value. <see cref="MaterialAnimData.VisualConstantIndex"/>
        /// counts the curves of the preceding materials.
        /// </summary>
        public static MaterialAnim ToSwitch(VisibilityAnim anim)
        {
            var result = new MaterialAnim
            {
                Name = anim.Name + SwitchSuffix,
                Path = anim.Path,
                FrameCount = anim.FrameCount,
                Flags = (MaterialAnim.MaterialAnimFlags)(anim._flags & 0x7),
                BindModel = anim.BindModel,
                BindIndices = anim.BindIndices?.ToArray() ?? new ushort[anim.Names.Count],
                UserData = anim.UserData ?? new ResDict<UserData>(),
                TextureNames = new ResDict<TextureRef>(),
                MaterialAnimDataList = new List<MaterialAnimData>(),
            };

            int curveCount = 0;
            for (int i = 0; i < anim.Names.Count; i++)
            {
                var data = new MaterialAnimData
                {
                    Name = anim.Names[i],
                    ParamAnimInfos = new List<ParamAnimInfo>(),
                    PatternAnimInfos = new List<PatternAnimInfo>(),
                    Constants = new List<AnimConstant>(),
                    Curves = new List<AnimCurve>(),
                    ShaderParamCurveIndex = None,
                    TexturePatternCurveIndex = None,
                    VisualConstantIndex = curveCount,
                    VisalCurveIndex = None,
                    BeginVisalConstantIndex = None,
                };

                var curve = anim.Curves.FirstOrDefault(x => x.AnimDataOffset == i);
                if (curve != null)
                {
                    var copy = curve.Copy();
                    copy.AnimDataOffset = 0;
                    data.Curves.Add(copy);
                    data.VisalCurveIndex = 0;
                    curveCount++;
                }
                else
                {
                    bool visible = anim.BaseDataList != null && i < anim.BaseDataList.Length && anim.BaseDataList[i];
                    data.Constants.Add(new AnimConstant { AnimDataOffset = 0, Value = visible ? 1 : 0 });
                    data.BeginVisalConstantIndex = 0;
                }
                result.MaterialAnimDataList.Add(data);
            }
            return result;
        }

        /// <summary>
        /// True if the Switch material animation carries material visibility data.
        /// </summary>
        public static bool IsVisibilityAnim(MaterialAnim anim) =>
            anim.Name.EndsWith(SwitchSuffix) || anim.MaterialAnimDataList.Any(d =>
                IsIndex(d.VisalCurveIndex, d.Curves?.Count ?? 0) || IsIndex(d.BeginVisalConstantIndex, d.Constants?.Count ?? 0));

        /// <summary>
        /// Builds a Wii U material visibility animation from a Switch one. Curved materials get the curve's first key as
        /// their base value, as the Wii U files do.
        /// </summary>
        public static VisibilityAnim ToWiiU(MaterialAnim anim)
        {
            var name = anim.Name.EndsWith(SwitchSuffix) ? anim.Name.Substring(0, anim.Name.Length - SwitchSuffix.Length) : anim.Name;
            var result = new VisibilityAnim
            {
                Name = name,
                Path = anim.Path,
                FrameCount = anim.FrameCount,
                BindModel = anim.BindModel,
                BindIndices = anim.BindIndices?.ToArray() ?? new ushort[anim.MaterialAnimDataList.Count],
                UserData = anim.UserData ?? new ResDict<UserData>(),
                Names = new List<string>(),
                Curves = new List<AnimCurve>(),
            };
            result._flags = (ushort)(MaterialTypeFlag | ((ushort)anim.Flags & 0x7));

            var baseData = new bool[anim.MaterialAnimDataList.Count];
            for (int i = 0; i < anim.MaterialAnimDataList.Count; i++)
            {
                var data = anim.MaterialAnimDataList[i];
                result.Names.Add(data.Name);
                if (IsIndex(data.VisalCurveIndex, data.Curves?.Count ?? 0))
                {
                    var copy = data.Curves[data.VisalCurveIndex].Copy();
                    copy.AnimDataOffset = (uint)i;
                    result.Curves.Add(copy);
                    baseData[i] = copy.KeyStepBoolData != null && copy.KeyStepBoolData.Length > 0 && copy.KeyStepBoolData[0];
                }
                else if (IsIndex(data.BeginVisalConstantIndex, data.Constants?.Count ?? 0))
                    baseData[i] = data.Constants[data.BeginVisalConstantIndex].Value.Int32 != 0;
            }
            result.BaseDataList = baseData;
            return result;
        }

        private static bool IsIndex(int index, int count) => index >= 0 && index != None && index < count;
    }
}
