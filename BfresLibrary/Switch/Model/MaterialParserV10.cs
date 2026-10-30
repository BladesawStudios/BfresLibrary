using BfresLibrary.Core;
using BfresLibrary.Switch.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BfresLibrary.Switch
{
    public class MaterialParserV10
    {
        public static void PrepareSave(Material mat)
        {
            var info = new ShaderInfo();
            ShaderAssignV10 previous = mat.ShaderInfoV10?.ShaderAssign;

            info.ShaderAssign = new ShaderAssignV10();
            info.ShaderAssign.ParentMaterial = mat;
            // The assign as the file had it, which still holds the dictionaries the game keyed by hash.
            info.ShaderAssign.Original = previous?.Original ?? previous;
            info.ShaderAssign.ShaderArchiveName = mat.ShaderAssign.ShaderArchiveName;
            info.ShaderAssign.ShadingModelName = mat.ShaderAssign.ShadingModelName;
            info.ShaderAssign.ParamCount = (ushort)mat.ShaderParams.Count;
            info.ShaderAssign.RenderInfoCount = (ushort)mat.RenderInfos.Count;
            info.SamplerAssigns = new List<string>();
            info.AttribAssigns = new List<string>();
            info.OptionValues = new List<string>();

            if (mat.ShaderAssign != null)
            {
                List<sbyte> samplerIndices = new List<sbyte>();
                List<sbyte> attributeIndices = new List<sbyte>();
                List<short> optionChoiceIndices = new List<short>();

                //Values
                foreach (var sampler in mat.ShaderAssign.SamplerAssigns.Values)
                {
                    if (sampler == "<Default Value>")
                    {
                        samplerIndices.Add(-1);
                        continue;
                    }

                    info.SamplerAssigns.Add(sampler);
                    samplerIndices.Add((sbyte)info.SamplerAssigns.IndexOf(sampler));
                }

                foreach (var sampler in mat.ShaderAssign.AttribAssigns.Values)
                {
                    if (sampler == "<Default Value>")
                    {
                        attributeIndices.Add(-1);
                        continue;
                    }
                    info.AttribAssigns.Add(sampler);
                    attributeIndices.Add((sbyte)info.AttribAssigns.IndexOf(sampler));
                }

                int choiceIdx = 0;

                List<bool> toggles = new List<bool>();
                foreach (var op in mat.ShaderAssign.ShaderOptions.Values)
                {
                    if (op == "<Default Value>")
                    {
                        optionChoiceIndices.Add(-1);
                        continue;
                    }

                    if (op == "True") toggles.Add(true);
                    else if (op == "False") toggles.Add(false);
                    else info.OptionValues.Add(op);

                    optionChoiceIndices.Add((short)choiceIdx);
                    choiceIdx++;
                }

                info.OptionToggles = toggles.ToArray();

                if (samplerIndices.Any(x => x == -1))
                    info.SamplerAssignIndices = samplerIndices.ToArray();
                if (attributeIndices.Any(x => x == -1))
                    info.AttributeAssignIndices = attributeIndices.ToArray();

                info.OptionIndices = optionChoiceIndices.ToArray();

                //Dicts
                foreach (var sampler in mat.ShaderAssign.SamplerAssigns)
                    info.ShaderAssign.SamplerAssign.Add(sampler.Key, sampler.Value);
                foreach (var att in mat.ShaderAssign.AttribAssigns)
                    info.ShaderAssign.AttributeAssign.Add(att.Key, att.Value);
                foreach (var op in mat.ShaderAssign.ShaderOptions)
                    info.ShaderAssign.Options.Add(op.Key, op.Value);
            }

            // Render infos the game names by hash stay in the order the file had them: its dictionary of them was built from the names, which are not in the file, and its entries are found by
            // the position they have in the list, so a different order would make every lookup find another render info. Others are grouped by type, as the older files have them.
            bool namedByHash = mat.RenderInfos.Count > 0 && mat.RenderInfos.Values.All(x => x.NameHash != 0);
            if (!namedByHash)
            {
                List<RenderInfo> renderInfoOrdered = new List<RenderInfo>();
                renderInfoOrdered.AddRange(mat.RenderInfos.Values.Where(x => x.Type == RenderInfoType.String));
                renderInfoOrdered.AddRange(mat.RenderInfos.Values.Where(x => x.Type == RenderInfoType.Single));
                renderInfoOrdered.AddRange(mat.RenderInfos.Values.Where(x => x.Type == RenderInfoType.Int32));

                mat.RenderInfos.Clear();
                foreach (var renderInfo in renderInfoOrdered)
                    mat.RenderInfos.Add(renderInfo.Name, renderInfo);
            }

            mat.ShaderInfoV10 = info;
        }

        public static void Load(ResFileSwitchLoader loader, Material mat)
        {
            //V10 changes quite alot....

            //First change is a new struct with shader assign + tables for shader assign data
            var info = loader.Load<ShaderInfo>();
            long TextureArrayOffset = loader.ReadInt64();
            long TextureNameArray = loader.ReadInt64();
            long SamplerArrayOffset = loader.ReadInt64();
            mat.Samplers = loader.LoadDictValues<Sampler>();
            //Next is table data
            long renderInfoDataTable = loader.ReadInt64();
            long renderInfoCounterTable = loader.ReadInt64();
            long renderInfoDataOffsets = loader.ReadInt64(); //offsets as shorts
            long SourceParamOffset = loader.ReadInt64();
            long SourceParamIndices = loader.ReadInt64(); //0xFFFF a bunch per param. Set at runtime??
            loader.ReadUInt64(); //reserved
            mat.UserData = loader.LoadDictValues<UserData>();
            long VolatileFlagsOffset = loader.ReadInt64();
            long userPointer = loader.ReadInt64();
            long SamplerSlotArrayOffset = loader.ReadInt64();
            long TexSlotArrayOffset = loader.ReadInt64();
            ushort idx = loader.ReadUInt16();
            byte numSampler = loader.ReadByte();
            byte numTextureRef = loader.ReadByte();
            loader.ReadUInt16(); //reserved
            ushort numUserData = loader.ReadUInt16();
            ushort renderInfoDataSize = loader.ReadUInt16();
            ushort user_shading_model_option_ubo_size = loader.ReadUInt16(); //Set at runtime?
            loader.ReadUInt32(); //padding

            mat.RenderInfoSize = renderInfoDataSize;

            long pos = loader.Position;

            var textures = loader.LoadCustom(() => loader.LoadStrings(numTextureRef), (uint)TextureNameArray);

            mat.TextureRefs = new List<TextureRef>();
            if (textures != null)
            {
                foreach (var tex in textures)
                    mat.TextureRefs.Add(new TextureRef() { Name = tex });
            }

            //Add names to the value as switch does not store any
            foreach (var sampler in mat.Samplers)
                sampler.Value.Name = sampler.Key;

            mat.TextureSlotArray = loader.LoadCustom(() => loader.ReadInt64s(numTextureRef), (uint)SamplerSlotArrayOffset);
            mat.SamplerSlotArray = loader.LoadCustom(() => loader.ReadInt64s(numSampler), (uint)TexSlotArrayOffset);

            if (info != null && info.ShaderAssign != null)
            {
                mat.ShaderAssign = new ShaderAssign()
                {
                    ShaderArchiveName = info.ShaderAssign.ShaderArchiveName,
                    ShadingModelName = info.ShaderAssign.ShadingModelName,
                };
                mat.ShaderParamData = loader.LoadCustom(() => loader.ReadBytes(info.ShaderAssign.ShaderParamSize), (uint)SourceParamOffset);
                mat.ParamIndices = loader.LoadCustom(() => loader.ReadInt32s(info.ShaderAssign.ShaderParameters.Count), (uint)SourceParamIndices);

                ReadRenderInfo(loader, info, mat, renderInfoCounterTable, renderInfoDataOffsets, renderInfoDataTable);
                ReadShaderParams(loader, info, mat);

                LoadAttributeAssign(info, mat);
                LoadSamplerAssign(info, mat);
                LoadShaderOptions(info, mat);
            }

            //Loading never kept this, so everything the shader assign carries - the archive
            //and shading model names, the parameter and sampler tables - was unreachable
            //from a material that had just been read.
            mat.ShaderInfoV10 = info;

            loader.Seek(pos, SeekOrigin.Begin);
        }

        static void ReadRenderInfo(ResFileSwitchLoader loader, ShaderInfo info, Material mat,
            long renderInfoCounterTable, long renderInfoDataOffsets, long renderInfoDataTable)
        {
            for (int i = 0; i < info.ShaderAssign.RenderInfos.Count; i++)
            {
                RenderInfo renderInfo = new RenderInfo();

                //Info table. Sixteen bytes an entry: the name, then the type. The game's own
                //files put a 64-bit hash of the name there; files other tools write put a
                //pointer to the name in the file, and the game reads both.
                loader.Seek((int)info.ShaderAssign.renderInfoListOffset + i * 16, SeekOrigin.Begin);
                ulong renderInfoKey = loader.ReadUInt64();
                renderInfo.Type = (RenderInfoType)loader.ReadByte();
                renderInfo.Name = NameOf(loader, renderInfoKey, out ulong renderInfoHash);
                renderInfo.NameHash = renderInfoHash;



                //Count table
                loader.Seek((int)renderInfoCounterTable + i * 2, SeekOrigin.Begin);
                ushort count = loader.ReadUInt16();

                //Offset table
                loader.Seek((int)renderInfoDataOffsets + i * 2, SeekOrigin.Begin);
                ushort dataOffset = loader.ReadUInt16();

                //Raw data table
                loader.Seek((int)renderInfoDataTable + dataOffset, SeekOrigin.Begin);
                renderInfo.ReadData(loader, renderInfo.Type, count);

                renderInfo.Name = UniqueName(mat.RenderInfos.Keys, renderInfo.Name, i);
                mat.RenderInfos.Add(renderInfo.Name, renderInfo);
            }
        }


        /// <summary>
        /// A name to file an entry under when the one read back is unusable.
        /// </summary>
        /// <remarks>
        /// Tears of the Kingdom's materials read back empty names for render infos, shader
        /// params and the assign tables, which throws the moment a material has more than one
        /// of anything - and most have several. These names are metadata that the geometry
        /// does not depend on, so an entry that cannot be named is filed under its index
        /// rather than costing the whole model.
        /// </remarks>
        static string UniqueName(IEnumerable<string> taken, string name, int index)
        {
            if (!string.IsNullOrEmpty(name) && !taken.Contains(name))
                return name;

            return $"{name}#{index}";
        }

        /// <summary>
        /// The name a render info or shader param hash stands for.
        /// </summary>
        /// <remarks>
        /// The hashes are keys into the same string table outside the file that
        /// <see cref="ResFile.ExternalStringResolver"/> reads, so it names them when it is set. Without it,
        /// or for a key it does not know, the hash itself is the name.
        /// </remarks>
        static string HashedName(ulong hash) =>
            ResFile.ExternalStringResolver?.Invoke(hash) ?? $"0x{hash:X16}";

        /// <summary>
        /// The name an eight-byte render info or shader param name field stands for.
        /// </summary>
        /// <remarks>
        /// A field that points inside the file is a string there, and its hash is zero so that saving writes the
        /// name back as a string. Anything else is the game's hash of the name.
        /// </remarks>
        static string NameOf(ResFileSwitchLoader loader, ulong key, out ulong hash)
        {
            if (loader.HoldsString(key))
            {
                hash = 0;
                return loader.StringAt((long)key);
            }
            hash = key;
            return HashedName(key);
        }

        static void ReadShaderParams(ResFileSwitchLoader loader, ShaderInfo info, Material mat)
        {
            for (int i = 0; i < info.ShaderAssign.ShaderParameters.Count; i++)
            {
                ShaderParam param = new ShaderParam();

                //Twenty-four bytes an entry: padding, the name (a hash or a pointer, as for
                //render infos), then the offset into the parameter data and the type.
                loader.Seek((int)info.ShaderAssign.shaderParamOffset + i * 24, SeekOrigin.Begin);
                var pad0 = loader.ReadUInt64(); //padding
                ulong paramKey = loader.ReadUInt64();
                param.DataOffset = loader.ReadUInt16();
                param.Type = (ShaderParamType)loader.ReadUInt16();
                var pad2 = loader.ReadUInt32(); //padding

                param.Name = NameOf(loader, paramKey, out ulong paramHash);
                param.NameHash = paramHash;

                param.Name = UniqueName(mat.ShaderParams.Keys, param.Name, i);
                mat.ShaderParams.Add(param.Name, param);
            }
        }

        static void LoadAttributeAssign(ShaderInfo info, Material mat)
        {
            for (int i = 0; i < info.ShaderAssign.AttributeAssign.Count; i++)
            {
                int idx = info.AttributeAssignIndices?.Length > 0 ? info.AttributeAssignIndices[i] : i;
                var value = idx == -1 ? "<Default Value>" : info.AttribAssigns[idx];
                var key = info.ShaderAssign.AttributeAssign.GetKey(i);

                mat.ShaderAssign.AttribAssigns.Add(UniqueName(mat.ShaderAssign.AttribAssigns.Keys, key, i), value);
            }
        }

        static void LoadSamplerAssign(ShaderInfo info, Material mat)
        {
            for (int i = 0; i < info.ShaderAssign.SamplerAssign.Count; i++)
            {
                int idx = info.SamplerAssignIndices?.Length > 0 ? info.SamplerAssignIndices[i] : i;
                var value = idx == -1 ? "<Default Value>" : info.SamplerAssigns[idx];
                var key = info.ShaderAssign.SamplerAssign.GetKey(i);

                mat.ShaderAssign.SamplerAssigns.Add(UniqueName(mat.ShaderAssign.SamplerAssigns.Keys, key, i), value);
            }
        }

        static void LoadShaderOptions(ShaderInfo info, Material mat)
        {
            //Find target option
            List<string> choices = new List<string>();
            for (int i = 0; i < info.OptionToggles.Length; i++)
                choices.Add(info.OptionToggles[i] ? "True" : "False");
            if (info.OptionValues != null)
                choices.AddRange(info.OptionValues);

            for (int i = 0; i < info.ShaderAssign.Options.Count; i++)
            {
                int idx = info.OptionIndices?.Length > 0 ? info.OptionIndices[i] : i;
                var value = idx == -1 ? "<Default Value>" : choices[idx];
                var key = info.ShaderAssign.Options.GetKey(i);

                mat.ShaderAssign.ShaderOptions.Add(UniqueName(mat.ShaderAssign.ShaderOptions.Keys, key, i), value);
            }
        }

        public static void Save(ResFileSwitchSaver saver, Material mat)
        {
            ShaderInfo info = mat.ShaderInfoV10;

            //Calculate total buffer sizes and offsets
            int renderInfoDataSize = 0;
            foreach (var renderInfo in mat.RenderInfos.Values)
            {
                renderInfo.DataOffset = renderInfoDataSize;
                switch (renderInfo.Type)
                {
                    case RenderInfoType.String:
                        renderInfoDataSize += 8 * renderInfo.GetValueStrings().Length;
                        break;
                    case RenderInfoType.Single:
                        renderInfoDataSize += 4 * renderInfo.GetValueSingles().Length;
                        break;
                    default:
                        renderInfoDataSize += 4 * renderInfo.GetValueInt32s().Length;
                        break;
                }
            }
            //Adds alignment
            var alignment = 128;
            renderInfoDataSize += (-renderInfoDataSize % alignment + alignment) % alignment;
            renderInfoDataSize = mat.RenderInfoSize;

            saver.SaveRelocateEntryToSection(saver.Position, 12, 1, 0, ResFileSwitchSaver.Section1, "FMAT");

            var textureList = mat.TextureRefs.Select(x => x.Name).ToList();

            saver.SaveString(mat.Name);
            saver.Save(info);
            saver.SaveCustom(new long[mat.TextureRefs.Count], () => saver.Write(new long[mat.TextureRefs.Count]));
            saver.SaveCustom(textureList, () => saver.SaveStringsRelocated(textureList));
            saver.SaveCustom(new long[mat.Samplers.Count], () => saver.Write(new long[mat.Samplers.Count * 15
                ]));
            saver.SaveList(mat.Samplers.Values);
            saver.SaveDict(mat.Samplers);
            //Render info data
            saver.SaveCustom(mat.RenderInfos, () =>
            {
                long pos = saver.Position;

                saver.Write(new byte[renderInfoDataSize]);
                saver.Seek(pos, SeekOrigin.Begin);

                long startpos = saver.Position;
                foreach (var renderInfo in mat.RenderInfos.Values)
                {
                    renderInfo.DataOffset = saver.Position - startpos;

                    switch (renderInfo.Type)
                    {
                        // The strings are pointers and the rest of the data is not, and the types can be in any order, so each run of strings is relocated where it stands.
                        case RenderInfoType.String:
                            if (renderInfo.GetValueStrings().Length > 0)
                                saver.SaveRelocateEntryToSection(saver.Position, (uint)renderInfo.GetValueStrings().Length, 1, 0, ResFileSwitchSaver.Section1, "Render Info Strings V10");
                            renderInfo.SaveStrings(saver);
                            break;
                        case RenderInfoType.Single: renderInfo.SaveFloats(saver); break;
                        default: renderInfo.SaveInts(saver); break;
                    }
                }
                saver.Seek(pos + renderInfoDataSize, SeekOrigin.Begin);
            });
            //Render info count
            saver.SaveCustom(new byte[renderInfoDataSize], () =>
            {
                foreach (var renderInfo in mat.RenderInfos.Values)
                {
                    switch (renderInfo.Type)
                    {
                        case RenderInfoType.String: saver.Write((ushort)renderInfo.GetValueStrings()?.Length); break;
                        case RenderInfoType.Single: saver.Write((ushort)renderInfo.GetValueSingles()?.Length); break;
                        default: saver.Write((ushort)renderInfo.GetValueInt32s()?.Length); break;
                    }
                }
            });
            //Render info offsets
            saver.SaveCustom(new uint[mat.RenderInfos.Count], () =>
            {
                foreach (var renderInfo in mat.RenderInfos.Values)
                    saver.Write((ushort)renderInfo.DataOffset);
            });
            //Shader params
            saver.SaveCustom(mat.ShaderParamData, () =>
            {
                saver.Write(mat.ShaderParamData);
                saver.Align(128);
            });
            saver.SaveCustom(mat.ParamIndices, () => saver.Write(mat.ParamIndices));
            saver.Write(0UL); //0

            saver.SaveRelocateEntryToSection(saver.Position, 3, 1, 0, ResFileSwitchSaver.Section1, "FMAT User Data");

            mat.PosUserDataMaterialOffset = saver.SaveOffset();
            mat.PosUserDataDictMaterialOffset = saver.SaveOffset();

            saver.SaveCustom(new byte[32], () => saver.Write(new byte[32])); //Volatile Flags?
            saver.Write(0UL); //userPointer?

            saver.SaveRelocateEntryToSection(saver.Position, 2, 1, 0, ResFileSwitchSaver.Section1, "Material texture slots");

            saver.SaveCustom(mat.SamplerSlotArray, () => saver.Write(mat.SamplerSlotArray));
            saver.SaveCustom(mat.TextureSlotArray, () => saver.Write(mat.TextureSlotArray));
            saver.Write((ushort)saver.CurrentIndex);
            saver.Write((byte)mat.TextureRefs.Count);
            saver.Write((byte)mat.Samplers.Count);
            saver.Write((ushort)0); //numShaderParamVolatile?
            saver.Write((ushort)mat.UserData.Count);
            saver.Write((ushort)renderInfoDataSize);

            saver.Write((ushort)0);
            saver.Write((ushort)0);
            saver.Write((ushort)0);
        }

        public class ShaderInfo : IResData
        {
            public ShaderAssignV10 ShaderAssign;

            public IList<string> AttribAssigns;
            public IList<string> SamplerAssigns;

            public bool[] OptionToggles;
            public IList<string> OptionValues;

            public short[] OptionIndices;
            public sbyte[] AttributeAssignIndices;
            public sbyte[] SamplerAssignIndices;

            private long[] _optionBitFlags;

            void IResData.Load(ResFileLoader loader)
            {
                ShaderAssign = loader.Load<ShaderAssignV10>();
                long attribAssignOffset = loader.ReadInt64();
                long attribAssignIndicesOffset = loader.ReadInt64();
                long samplerAssignOffset = loader.ReadInt64();
                long samplerAssignIndicesOffset = loader.ReadInt64();
                ulong optionChoiceToggleOffset = loader.ReadUInt64();
                ulong optionChoiceStringsOffset = loader.ReadUInt64();
                long optionChoiceIndicesOffset = loader.ReadInt64();
                loader.ReadUInt32(); //padding
                byte numAttributeAssign = loader.ReadByte();
                byte numSamplerAssign = loader.ReadByte();
                ushort shaderOptionBooleanCount = loader.ReadUInt16();
                ushort shaderOptionChoiceCount = loader.ReadUInt16();
                loader.ReadUInt16(); //padding
                loader.ReadUInt32(); //padding

                var numBitflags = 1 + shaderOptionBooleanCount / 64;

                AttribAssigns = loader.LoadCustom(() => loader.LoadStrings(numAttributeAssign), (uint)attribAssignOffset);
                SamplerAssigns = loader.LoadCustom(() => loader.LoadStrings(numSamplerAssign), (uint)samplerAssignOffset);
                _optionBitFlags = loader.LoadCustom(() => loader.ReadInt64s(numBitflags), (uint)optionChoiceToggleOffset);

                if (ShaderAssign  != null)
                {
                    OptionIndices = ReadShortIndices(loader, optionChoiceIndicesOffset, shaderOptionChoiceCount, ShaderAssign.Options.Count);
                    AttributeAssignIndices = ReadByteIndices(loader, attribAssignIndicesOffset, numAttributeAssign, ShaderAssign.AttributeAssign.Count);
                    SamplerAssignIndices = ReadByteIndices(loader, samplerAssignIndicesOffset, numSamplerAssign, ShaderAssign.SamplerAssign.Count);

                    var numChoiceValues = shaderOptionChoiceCount - shaderOptionBooleanCount;
                    OptionValues = loader.LoadCustom(() => loader.LoadStrings((int)numChoiceValues), (uint)optionChoiceStringsOffset);

                    SetupOptionBooleans(shaderOptionBooleanCount);
                }
            }

            void IResData.Save(ResFileSaver saver)
            {
                CreateOptionFlag();

                ((ResFileSwitchSaver)saver).SaveRelocateEntryToSection(saver.Position, 8, 1, 0, ResFileSwitchSaver.Section1, "ShaderInfo");

                saver.Save(ShaderAssign);
                saver.SaveCustom(AttribAssigns, () => ((ResFileSwitchSaver)saver).SaveStringsRelocated(AttribAssigns));
                saver.SaveCustom(AttributeAssignIndices, () => WriteIndices(saver, AttributeAssignIndices));
                saver.SaveCustom(SamplerAssigns, () => ((ResFileSwitchSaver)saver).SaveStringsRelocated(SamplerAssigns));
                saver.SaveCustom(SamplerAssignIndices, () => WriteIndices(saver, SamplerAssignIndices));
                saver.SaveCustom(OptionToggles, () => saver.Write(_optionBitFlags));
                saver.SaveCustom(OptionValues, () => ((ResFileSwitchSaver)saver).SaveStringsRelocated(OptionValues));
                saver.SaveCustom(OptionIndices, () => WriteIndices(saver, OptionIndices));
                saver.Write(0); //padding
                saver.Write((byte)AttribAssigns?.Count);
                saver.Write((byte)SamplerAssigns?.Count);
                saver.Write((ushort)OptionToggles?.Length);
                saver.Write((ushort)(OptionToggles?.Length + OptionValues?.Count));
                saver.Write(new byte[6]); //padding
            }

            private void CreateOptionFlag()
            {
                var numBitflags = 1 + OptionToggles.Length / 64;
                _optionBitFlags = new long[numBitflags];

                int idx = 0;
                for (int i = 0; i < OptionToggles.Length; i++)
                {
                    if (i != 0 && i % 64 == 0)
                        idx++;

                    if (OptionToggles[i])
                        _optionBitFlags[idx] |= ((long)1 << i);
                }
            }

            private sbyte[] ReadByteIndices(ResFileLoader loader, long offset, int usedCount, int totalCount)
            {
                if (offset == 0)
                    return null;

                using (loader.TemporarySeek((int)offset, SeekOrigin.Begin))
                {
                    var usedIndices = loader.ReadSBytes(usedCount);
                    return loader.ReadSBytes(totalCount);
                }
            }

            private short[] ReadShortIndices(ResFileLoader loader, long offset, int usedCount, int totalCount)
            {
                if (offset == 0)
                    return null;

                using (loader.TemporarySeek((int)offset, SeekOrigin.Begin))
                {
                    var usedIndices = loader.ReadInt16s(usedCount);
                    return loader.ReadInt16s(totalCount);
                }
            }

            private void SetupOptionBooleans(int count)
            {
                OptionToggles = new bool[count];

                if (count == 0) return;

                var flags = _optionBitFlags.ToArray();
                int idx = 0;
                for (int i = 0; i < count; i++)
                {
                    if (i != 0 && i % 64 == 0)
                        idx++;

                    OptionToggles[i] = (_optionBitFlags[idx] & ((long)1 << i)) != 0;
                }
                for (int i = 0; i < _optionBitFlags.Length; i++)
                    if (_optionBitFlags[i] != flags[i])
                        throw new Exception();
            }

            private void WriteIndices(ResFileSaver saver, short[] indices)
            {
                for (short i = 0; i < indices.Length; i++)
                {
                    if (indices[i] != -1)
                        saver.Write(i);
                }
                saver.Write(indices);
                saver.Align(8);
            }

            private void WriteIndices(ResFileSaver saver, sbyte[] indices)
            {
                for (sbyte i = 0; i < indices.Length; i++)
                {
                    if (indices[i] != -1)
                        saver.Write(i);
                }
                saver.Write(indices);
                saver.Align(8);
            }
        }

        public class ShaderAssignV10 : IResData
        {
            public ResDict<ResString> RenderInfos = new ResDict<ResString>();
            public ResDict<ResString> ShaderParameters = new ResDict<ResString>();
            public ResDict<ResString> AttributeAssign = new ResDict<ResString>();
            public ResDict<ResString> SamplerAssign = new ResDict<ResString>();
            public ResDict<ResString> Options = new ResDict<ResString>();

            public string ShaderArchiveName;
            public string ShadingModelName;

            public ulong shaderParamOffset;
            public ulong renderInfoListOffset;

            public ushort ShaderParamSize;

            public ushort RenderInfoCount;
            public ushort ParamCount;

            public Material ParentMaterial;

            // The assign this one was made from when the material was read from a file, whose hashed dictionaries are written back as they were while the names they stand for are unchanged.
            public ShaderAssignV10 Original;

            void IResData.Load(ResFileLoader loader)
            {
                ShaderArchiveName = loader.LoadString();
                ShadingModelName = loader.LoadString();

                //List of names + type. Data in material section
                renderInfoListOffset = loader.ReadUInt64();
                RenderInfos = loader.LoadDict<ResString>();
                //List of names + type. Data in material section
                shaderParamOffset = loader.ReadUInt64();
                ShaderParameters = loader.LoadDict<ResString>();
                AttributeAssign = loader.LoadDict<ResString>();
                SamplerAssign = loader.LoadDict<ResString>();
                Options = loader.LoadDict<ResString>();
                RenderInfoCount = loader.ReadUInt16(); //render info count
                ParamCount = loader.ReadUInt16(); //param count
                ShaderParamSize = loader.ReadUInt16();
                loader.ReadUInt16(); //padding
                loader.ReadUInt64(); //padding
            }

            void IResData.Save(ResFileSaver saver)
            {
                ((ResFileSwitchSaver)saver).SaveRelocateEntryToSection(saver.Position, 9, 1, 0, ResFileSwitchSaver.Section1, "ShaderAssignV10");

                saver.SaveString(ShaderArchiveName);
                saver.SaveString(ShadingModelName);
                saver.SaveCustom(new long[ParentMaterial.RenderInfos.Count], () =>
                {
                    foreach (var renderInfo in ParentMaterial.RenderInfos.Values)
                    {
                        //A hash where the file names it by hash, a string where it does not -
                        //the eight bytes are the same either way, and writing a string into a
                        //file the game reads as a hash would be writing a pointer it would
                        //follow somewhere meaningless. Only a string is a pointer the loader
                        //relocates: a hash in the relocation table has the load address added
                        //to it, and the game then cannot find the name it is looking up.
                        if (renderInfo.NameHash != 0) saver.Write(renderInfo.NameHash);
                        else
                        {
                            ((ResFileSwitchSaver)saver).SaveRelocateEntryToSection(saver.Position, 1, 1, 1, ResFileSwitchSaver.Section1, "Render Param Info V10");
                            saver.SaveString(renderInfo.Name);
                        }

                        saver.Write((byte)renderInfo.Type);
                        saver.Write(new byte[7]);
                    }
                });
                if (SameHashes(Original?.RenderInfos, ParentMaterial.RenderInfos.Values.Select(r => r.NameHash)))
                    saver.SaveDict(Original.RenderInfos);
                else
                    saver.SaveDict(ParentMaterial.RenderInfos);
                saver.SaveCustom(new long[ParentMaterial.ShaderParams.Count], () =>
                {
                    foreach (var param in ParentMaterial.ShaderParams.Values)
                    {
                        // As above: a name that is a hash is not a pointer and is not relocated.
                        if (param.NameHash == 0)
                            ((ResFileSwitchSaver)saver).SaveRelocateEntryToSection(saver.Position, 2, 1, 1, ResFileSwitchSaver.Section1, "Shader Param Info V10");
                        saver.Write(new byte[8]);

                        if (param.NameHash != 0) saver.Write(param.NameHash);
                        else saver.SaveString(param.Name);
                        saver.Write((ushort)param.DataOffset);
                        saver.Write((ushort)param.Type);
                        saver.Write(new byte[4]);
                    }
                });
                if (SameHashes(Original?.ShaderParameters, ParentMaterial.ShaderParams.Values.Select(p => p.NameHash)))
                    saver.SaveDict(Original.ShaderParameters);
                else
                    saver.SaveDict(ParentMaterial.ShaderParams);
                saver.SaveDict(AttributeAssign);
                saver.SaveDict(SamplerAssign);
                if (Original?.Options != null && Original.Options.HasRawKeys && SameOptionKeys(Original.Options, Options))
                    saver.SaveDict(Original.Options);
                else
                    saver.SaveDict(Options);
                saver.Write((ushort)ParentMaterial.RenderInfos.Count);
                saver.Write((ushort)ParentMaterial.ShaderParams.Count);
                saver.Write((ushort)ParentMaterial.ShaderParamData.Length);
                saver.Write((ushort)0);//padding
                saver.Write(0UL);//padding
            }

            // Whether a dictionary read from the file was keyed by the hashes the material still has, in the same order.
            static bool SameHashes(ResDict<ResString> original, IEnumerable<ulong> current) =>
                original != null && original.HasRawKeys && original.RawKeys.SequenceEqual(current);

            // The options are named as they were read (an unreadable name gets its number added, which is all that may differ).
            static bool SameOptionKeys(ResDict<ResString> original, ResDict<ResString> current)
            {
                var was = original.Keys.ToList();
                var now = current.Keys.ToList();
                if (was.Count != now.Count) return false;
                for (int i = 0; i < was.Count; i++)
                    if (now[i] != was[i] && now[i] != $"{was[i]}#{i}") return false;
                return true;
            }

            public override int GetHashCode()
            {
                int hash = 0;
                hash += ShaderArchiveName.GetHashCode();
                hash += ShadingModelName.GetHashCode();

                foreach (var renderInfo in ParentMaterial.RenderInfos.Values)
                {
                    hash += renderInfo.Name.GetHashCode();
                    hash += renderInfo.Type.GetHashCode();
                }
                foreach (var p in ParentMaterial.ShaderParams.Values)
                {
                    hash += p.Name.GetHashCode();
                    hash += p.DataOffset.GetHashCode();
                    hash += p.Type.GetHashCode();
                }
                foreach (var op in Options)
                    hash += op.GetHashCode();
                foreach (var att in AttributeAssign)
                    hash += att.GetHashCode();
                foreach (var samp in SamplerAssign)
                    hash += samp.GetHashCode();

                return hash;
            }
        }
    }
}
