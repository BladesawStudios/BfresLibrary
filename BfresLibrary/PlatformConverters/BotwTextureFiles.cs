using System.IO;

namespace BfresLibrary.PlatformConverters
{
    /// <summary>
    /// BotW Wii U keeps each texture archive in two files: X.Tex1 holds mip level 0 and X.Tex2 holds the rest of the
    /// mip chain (with its own swizzle). Switch keeps one X.Tex file.
    /// </summary>
    public static class BotwTextureFiles
    {
        /// <summary>
        /// Moves the mip chain of a Tex2 file into the textures of its Tex1 file and renames it to X.Tex.
        /// </summary>
        public static void MergeTex2(ResFile tex1, ResFile tex2)
        {
            foreach (var name in tex1.Textures.Keys)
            {
                if (!(tex1.Textures[name] is WiiU.Texture main) || tex2 == null || !tex2.Textures.ContainsKey(name) || !(tex2.Textures[name] is WiiU.Texture mips))
                    continue;
                main.MipData = mips.MipData;
                main.MipOffsets = mips.MipOffsets;
                main.MipCount = mips.MipCount;
                main.MipSwizzle = mips.Swizzle;
            }
            tex1.Name = BaseName(tex1.Name) + ".Tex";
        }

        /// <summary>
        /// Saves a Wii U texture archive as the Tex1 / Tex2 pair. Tex2 is null when no texture has mips.
        /// </summary>
        public static (byte[] Tex1, byte[] Tex2) SaveSplit(ResFile textures)
        {
            var name = BaseName(textures.Name);
            textures.Name = name + ".Tex1";
            var tex1 = Save(textures);

            byte[] tex2 = null;
            bool hasMips = false;
            foreach (var tex in textures.Textures.Values)
                if (tex is WiiU.Texture t && t.MipCount > 1)
                    hasMips = true;
            if (hasMips)
            {
                var swizzles = new System.Collections.Generic.Dictionary<WiiU.Texture, uint>();
                foreach (var tex in textures.Textures.Values)
                {
                    if (!(tex is WiiU.Texture t)) continue;
                    swizzles[t] = t.Swizzle;
                    if (t.MipSwizzle != 0) t.Swizzle = t.MipSwizzle;
                }
                textures.Name = name + ".Tex2";
                tex2 = Save(textures);
                foreach (var pair in swizzles)
                    pair.Key.Swizzle = pair.Value;
            }
            textures.Name = name + ".Tex";
            return (tex1, tex2);
        }

        /// <summary>
        /// The GX2 bank/pipe swizzle BotW's Wii U textures use, which follows the texture's format and channel
        /// selectors (it matches 98.8% of the game's textures).
        /// </summary>
        public static uint BankSwizzle(WiiU.Texture texture)
        {
            if (texture.Dim == GX2.GX2SurfaceDim.Dim2DArray)
                return 0;
            var channels = $"{Sel(texture.CompSelR)}{Sel(texture.CompSelG)}{Sel(texture.CompSelB)}{Sel(texture.CompSelA)}";
            switch (texture.Format)
            {
                case GX2.GX2SurfaceFormat.T_BC1_UNorm: return channels == "RGBA" ? 2u : 0u;
                case GX2.GX2SurfaceFormat.T_BC3_SRGB: return 1;
                case GX2.GX2SurfaceFormat.T_BC4_UNorm: return channels == "RRRR" ? 3u : 0u;
                case GX2.GX2SurfaceFormat.T_BC5_UNorm: return channels == "RGGG" ? 3u : channels == "RG11" ? 2u : 0u;
                default: return 0;
            }
        }

        private static char Sel(GX2.GX2CompSel sel)
        {
            switch (sel)
            {
                case GX2.GX2CompSel.ChannelR: return 'R';
                case GX2.GX2CompSel.ChannelG: return 'G';
                case GX2.GX2CompSel.ChannelB: return 'B';
                case GX2.GX2CompSel.ChannelA: return 'A';
                case GX2.GX2CompSel.Always0: return '0';
                default: return '1';
            }
        }

        private static byte[] Save(ResFile file)
        {
            var stream = new MemoryStream();
            file.Save(stream, true);
            return stream.ToArray();
        }

        private static string BaseName(string name)
        {
            foreach (var suffix in new[] { ".Tex1", ".Tex2", ".Tex" })
                if (name.EndsWith(suffix))
                    return name.Substring(0, name.Length - suffix.Length);
            return name;
        }
    }
}
