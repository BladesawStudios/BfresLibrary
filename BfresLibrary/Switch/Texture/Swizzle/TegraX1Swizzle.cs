using System;
using System.Collections.Generic;
using Syroot.NintenTools.NSW.Bntx.GFX;
using Syroot.NintenTools.NSW.Bntx;

namespace BfresLibrary.Swizzling
{
    public class TegraX1Swizzle
    {
        public static byte[] GetImageData(Texture texture, byte[] ImageData, int ArrayLevel, int MipLevel, int DepthLevel, uint BlockHeightLog2, int target = 1, bool LinearTileMode = false)
        {
            //ImageData holds every slice; each one takes an equal share of it.
            int slice = DepthLevel * (int)texture.ArrayLength + ArrayLevel;
            int sliceSize = (int)(ImageData.Length / texture.ArrayLength);
            return GetSliceImageData(texture, new ReadOnlySpan<byte>(ImageData, slice * sliceSize, sliceSize), MipLevel, BlockHeightLog2, target, LinearTileMode);
        }

        /// <summary>
        /// Deswizzles one mip level from the data (all mips) of a single slice.
        /// </summary>
        public static byte[] GetSliceImageData(Texture texture, byte[] sliceData, int MipLevel, uint BlockHeightLog2, int target = 1, bool LinearTileMode = false)
        {
            return GetSliceImageData(texture, new ReadOnlySpan<byte>(sliceData), MipLevel, BlockHeightLog2, target, LinearTileMode);
        }

        /// <summary>
        /// Deswizzles one mip level from the data (all mips) of a single slice, returning a new array.
        /// </summary>
        public static byte[] GetSliceImageData(Texture texture, ReadOnlySpan<byte> sliceData, int MipLevel, uint BlockHeightLog2, int target = 1, bool LinearTileMode = false)
        {
            try
            {
                byte[] result = new byte[GetMipLevelSize(texture, MipLevel)];
                DeswizzleMipLevel(texture, sliceData, MipLevel, BlockHeightLog2, result, target, LinearTileMode);
                return result;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to swizzle texture {texture.Name}!");
                Console.WriteLine(e);
                return new byte[0];
            }
        }

        /// <summary>
        /// The size in bytes of a mip level once deswizzled: what <see cref="DeswizzleMipLevel"/> writes.
        /// </summary>
        public static int GetMipLevelSize(Texture texture, int mipLevel)
        {
            GetFormatInfo(texture, out uint bpp, out uint blkWidth, out uint blkHeight);
            uint width = DIV_ROUND_UP((uint)Math.Max(1, texture.Width >> mipLevel), blkWidth);
            uint height = DIV_ROUND_UP((uint)Math.Max(1, texture.Height >> mipLevel), blkHeight);
            return checked((int)(width * height * bpp));
        }

        /// <summary>
        /// Where a mip level starts in the data of a slice (all of its mips), and the block height (log2, in GOBs) that level is tiled with.
        /// </summary>
        public static uint GetMipLevelOffset(Texture texture, int mipLevel, uint BlockHeightLog2, out int mipBlockHeightLog2)
        {
            return GetMipLevelOffset(texture, mipLevel, BlockHeightLog2, out mipBlockHeightLog2, out _);
        }

        /// <summary>
        /// As above, also giving the bytes the level takes swizzled (its padded surface, not counting the alignment before the next level).
        /// </summary>
        public static uint GetMipLevelOffset(Texture texture, int mipLevel, uint BlockHeightLog2, out int mipBlockHeightLog2, out uint swizzledSize)
        {
            GetFormatInfo(texture, out uint bpp, out uint blkWidth, out uint blkHeight);
            uint surfaceSize = 0, mipOffset = 0;
            mipBlockHeightLog2 = 0;
            swizzledSize = 0;
            for (int level = 0; level <= mipLevel; level++)
            {
                uint width__ = DIV_ROUND_UP((uint)Math.Max(1, texture.Width >> level), blkWidth);
                uint height__ = DIV_ROUND_UP((uint)Math.Max(1, texture.Height >> level), blkHeight);

                //A mip's block height (in GOBs) shrinks to the smallest power of two that covers its height.
                uint mipBlockHeight = Math.Min(1u << (int)BlockHeightLog2, pow2_round_up(DIV_ROUND_UP(height__, 8)));
                mipBlockHeightLog2 = 0;
                while ((1u << (mipBlockHeightLog2 + 1)) <= mipBlockHeight)
                    mipBlockHeightLog2++;

                surfaceSize = round_up(surfaceSize, 512);
                mipOffset = surfaceSize;
                swizzledSize = round_up(width__ * bpp, 64) * round_up(height__, mipBlockHeight * 8);
                surfaceSize += swizzledSize;
            }
            return mipOffset;
        }

        /// <summary>
        /// Deswizzles one mip level from the data (all mips) of a single slice into the destination, with no allocation or intermediate copy.
        /// Returns the bytes written, <see cref="GetMipLevelSize"/>. Texels the source is too short to hold are zero.
        /// </summary>
        public static int DeswizzleMipLevel(Texture texture, ReadOnlySpan<byte> sliceData, int mipLevel, uint BlockHeightLog2, Span<byte> destination, int target = 1, bool LinearTileMode = false)
        {
            uint offset = GetMipLevelOffset(texture, mipLevel, BlockHeightLog2, out int mipBlockHeightLog2);
            ReadOnlySpan<byte> mipData = offset < sliceData.Length ? sliceData.Slice((int)offset) : ReadOnlySpan<byte>.Empty;
            return DeswizzleMipData(texture, mipData, mipLevel, mipBlockHeightLog2, destination, target, LinearTileMode);
        }

        /// <summary>
        /// Deswizzles one mip level from data that starts at that mip (not at the start of the slice), with the block height
        /// (log2, in GOBs) <see cref="GetMipLevelOffset"/> gives for it.
        /// </summary>
        public static int DeswizzleMipData(Texture texture, ReadOnlySpan<byte> mipData, int mipLevel, int mipBlockHeightLog2, Span<byte> destination, int target = 1, bool LinearTileMode = false)
        {
            GetFormatInfo(texture, out uint bpp, out uint blkWidth, out uint blkHeight);
            uint width = (uint)Math.Max(1, texture.Width >> mipLevel);
            uint height = (uint)Math.Max(1, texture.Height >> mipLevel);
            return DeswizzleSurface(width, height, blkWidth, blkHeight, bpp, target, LinearTileMode ? 1u : 0u, mipBlockHeightLog2, mipData, destination);
        }

        static void GetFormatInfo(Texture texture, out uint bpp, out uint blkWidth, out uint blkHeight)
        {
            if (!FormatList.TryGetValue(texture.Format, out FormatInfo info))
                throw new ArgumentException($"Unsupported texture format {texture.Format}.");
            bpp = info.BytesPerPixel;
            blkWidth = info.BlockWidth;
            blkHeight = info.BlockHeight;
        }

        static int DeswizzleSurface(uint width, uint height, uint blkWidth, uint blkHeight, uint bpp, int roundPitch, uint tileMode, int blockHeightLog2, ReadOnlySpan<byte> data, Span<byte> destination)
        {
            uint w = DIV_ROUND_UP(width, blkWidth);
            uint h = DIV_ROUND_UP(height, blkHeight);
            int rowBytes = checked((int)(w * bpp));
            int size = checked(rowBytes * (int)h);
            if (destination.Length < size)
                throw new ArgumentException($"Destination holds {destination.Length} bytes; {size} are needed.", nameof(destination));
            destination = destination.Slice(0, size);

            //Texels that aren't a whole share of a 16 byte run (3 bytes a texel) go one at a time.
            if (bpp == 0 || 16 % bpp != 0)
            {
                byte[] slow = _swizzle(width, height, 1, blkWidth, blkHeight, 1, roundPitch, bpp, tileMode, blockHeightLog2, data.ToArray(), 0);
                new ReadOnlySpan<byte>(slow, 0, size).CopyTo(destination);
                return size;
            }

            uint blockHeight = 1u << blockHeightLog2;
            uint pitch, surfSize;
            if (tileMode == 1)
            {
                pitch = w * bpp;
                if (roundPitch == 1)
                    pitch = round_up(pitch, 32);
                surfSize = pitch * h;
            }
            else
            {
                pitch = round_up(w * bpp, 64);
                surfSize = pitch * round_up(h, blockHeight * 8);
            }

            //Whatever the source can't hold stays zero.
            if (data.Length < surfSize)
                destination.Clear();

            CopyRuns(data, destination, Math.Min(surfSize, data.Length), h, rowBytes, bpp, pitch, tileMode == 1, blockHeight);
            return size;
        }

        // Within a row the swizzled bytes come in runs of 16 that stay together, so a row is copied a run at a time.
        static void CopyRuns(ReadOnlySpan<byte> data, Span<byte> destination, long limit, uint rows, int rowBytes, uint bpp, uint pitch, bool linear, uint blockHeight)
        {
            uint gobSize = 512 * blockHeight;
            uint gobsPerRow = DIV_ROUND_UP((uint)rowBytes, 64);
            for (uint y = 0; y < rows; y++)
            {
                int destRow = (int)y * rowBytes;
                uint rowBase = linear ? 0
                    : (y / (8 * blockHeight)) * gobSize * gobsPerRow
                    + (y % (8 * blockHeight) / 8) * 512
                    + ((y % 8) / 2) * 64 + (y % 2) * 16;

                for (int b = 0; b < rowBytes; b += 16)
                {
                    int len = Math.Min(16, rowBytes - b);
                    long pos = linear
                        ? (long)y * pitch + b
                        : rowBase + (uint)(b >> 6) * gobSize + (uint)((b & 63) >> 5) * 256 + (uint)((b & 31) >> 4) * 32;

                    long avail = limit - pos;
                    if (avail < len)
                    {
                        avail -= avail % bpp; // only texels that fit whole are taken
                        if (avail <= 0)
                            continue;
                        len = (int)avail;
                    }
                    data.Slice((int)pos, len).CopyTo(destination.Slice(destRow + b));
                }
            }
        }

        internal static uint GetBytesPerPixel(Texture texture)
        {
            if (FormatList.ContainsKey(texture.Format))
                return FormatList[texture.Format].BytesPerPixel;
            return 0;
        }

        internal static uint GetBlockWidth(Texture texture)
        {
            if (FormatList.ContainsKey(texture.Format))
                return FormatList[texture.Format].BlockWidth;
            return 0;
        }

        internal static uint GetBlockHeight(Texture texture)
        {
            if (FormatList.ContainsKey(texture.Format))
                return FormatList[texture.Format].BlockHeight;
            return 0;
        }

        static Dictionary<SurfaceFormat, FormatInfo> FormatList = new Dictionary<SurfaceFormat, FormatInfo>()
        {
            { SurfaceFormat.R32_G32_B32_A32_UNORM,    new FormatInfo(16, 1,  1) },
            { SurfaceFormat.R16_G16_B16_A16_UNORM,    new FormatInfo(8, 1, 1) },
            { SurfaceFormat.R8_G8_B8_A8_UNORM,        new FormatInfo(4, 1, 1) },
            { SurfaceFormat.R8_G8_B8_A8_SRGB,         new FormatInfo(4, 1, 1) },
            { SurfaceFormat.R8_G8_B8_A8_SNORM,        new FormatInfo(4, 1, 1) },
            { SurfaceFormat.R4_G4_B4_A4_UNORM,        new FormatInfo(3, 1, 1) },
            { SurfaceFormat.R32_G32_B32_UNORM,        new FormatInfo(8, 1, 1) },
            { SurfaceFormat.R4_G4_UNORM,              new FormatInfo(1, 1, 1) },
            { SurfaceFormat.R32_UNORM,                new FormatInfo(4, 1, 1) },
            { SurfaceFormat.R16_UNORM,                new FormatInfo(2, 1, 1) },
            { SurfaceFormat.R16_UINT,                 new FormatInfo(2, 1, 1) },
            { SurfaceFormat.R8_UNORM,                 new FormatInfo(1, 1, 1) },
            { SurfaceFormat.R8_G8_UNORM,              new FormatInfo(2, 1, 1) },
            { SurfaceFormat.D32_FLOAT_S8X24_UINT,     new FormatInfo(8, 1, 1) },
            { SurfaceFormat.R32_G8_X24_UNORM,         new FormatInfo(8, 1, 1) },
            { SurfaceFormat.B8_G8_R8_A8_UNORM,        new FormatInfo(4, 1, 1) },
            { SurfaceFormat.B8_G8_R8_A8_SRGB,         new FormatInfo(4, 1, 1) },
            { SurfaceFormat.R5_G5_B5_A1_UNORM,        new FormatInfo(2, 1, 1) },
            { SurfaceFormat.B5_G5_R5_A1_UNORM,        new FormatInfo(2, 1, 1) },
            { SurfaceFormat.A1_B5_G5_R5_UNORM,        new FormatInfo(2, 1, 1) },
            { SurfaceFormat.R5_G6_B5_UNORM,           new FormatInfo(2, 1, 1) },
            { SurfaceFormat.R10_G10_B10_A2_UNORM,     new FormatInfo(4, 1, 1) },
            { SurfaceFormat.R11_G11_B10_UNORM,        new FormatInfo(4, 1, 1) },
            { SurfaceFormat.A4_B4_G4_R4_UNORM,        new FormatInfo(2, 1, 1) },
            { SurfaceFormat.B5_G6_R5_UNORM,           new FormatInfo(2, 1, 1) },

            { SurfaceFormat.BC1_UNORM,           new FormatInfo(8, 4, 4) },
            { SurfaceFormat.BC1_SRGB,            new FormatInfo(8, 4, 4) },
            { SurfaceFormat.BC2_UNORM,           new FormatInfo(16, 4, 4) },
            { SurfaceFormat.BC2_SRGB,            new FormatInfo(16, 4, 4) },
            { SurfaceFormat.BC3_UNORM,           new FormatInfo(16, 4, 4) },
            { SurfaceFormat.BC3_SRGB,            new FormatInfo(16, 4, 4) },
            { SurfaceFormat.BC4_UNORM,           new FormatInfo(8, 4, 4) },
            { SurfaceFormat.BC4_SNORM,           new FormatInfo(8, 4, 4) },
            { SurfaceFormat.BC5_UNORM,           new FormatInfo(16, 4, 4) },
            { SurfaceFormat.BC5_SNORM,           new FormatInfo(16, 4, 4) },
            { SurfaceFormat.BC6_UFLOAT,          new FormatInfo(16, 4, 4) },
            { SurfaceFormat.BC6_FLOAT,           new FormatInfo(16, 4, 4) },
            { SurfaceFormat.BC7_UNORM,           new FormatInfo(16, 4, 4) },
            { SurfaceFormat.BC7_SRGB,            new FormatInfo(16, 4, 4) },

            { SurfaceFormat.ASTC_4x4_UNORM,      new FormatInfo(16, 4, 4) },
            { SurfaceFormat.ASTC_4x4_SRGB,       new FormatInfo(16, 4, 4) },
            { SurfaceFormat.ASTC_5x5_UNORM,       new FormatInfo(16, 5, 5) },
            { SurfaceFormat.ASTC_5x5_SRGB,       new FormatInfo(16, 5, 5) },
            { SurfaceFormat.ASTC_6x5_UNORM,       new FormatInfo(16, 6, 5) },
            { SurfaceFormat.ASTC_6x5_SRGB,       new FormatInfo(16, 6, 5) },
            { SurfaceFormat.ASTC_8x5_UNORM,       new FormatInfo(16, 8, 5) },
            { SurfaceFormat.ASTC_8x5_SRGB,       new FormatInfo(16, 8, 5) },
            { SurfaceFormat.ASTC_8x6_UNORM,       new FormatInfo(16, 8, 6) },
            { SurfaceFormat.ASTC_8x6_SRGB,       new FormatInfo(16, 8, 6) },
            { SurfaceFormat.ASTC_10x5_UNORM,       new FormatInfo(16, 10, 5) },
            { SurfaceFormat.ASTC_10x5_SRGB,       new FormatInfo(16, 10, 5) },
            { SurfaceFormat.ASTC_10x6_UNORM,       new FormatInfo(16, 10, 6) },
            { SurfaceFormat.ASTC_10x6_SRGB,       new FormatInfo(16, 10, 6) },
            { SurfaceFormat.ASTC_10x10_SRGB,       new FormatInfo(16, 10, 10) },
            { SurfaceFormat.ASTC_10x10_UNORM,       new FormatInfo(16, 10, 10) },
            { SurfaceFormat.ASTC_12x10_SRGB,       new FormatInfo(16, 12, 10) },
            { SurfaceFormat.ASTC_12x10_UNORM,       new FormatInfo(16, 12, 10) },
            { SurfaceFormat.ASTC_12x12_SRGB,       new FormatInfo(16, 12, 12) },
            { SurfaceFormat.ASTC_12x12_UNORM,       new FormatInfo(16, 12, 12) },
        };

        class FormatInfo
        {
            public uint BytesPerPixel;
            public uint BlockWidth;
            public uint BlockHeight;

            public FormatInfo(uint bpp, uint blockW, uint blockH) {
                BytesPerPixel = bpp;
                BlockWidth = blockW;
                BlockHeight = blockH;
            }
        }

        /*---------------------------------------
         * 
         * Code ported from AboodXD's BNTX Extractor https://github.com/aboood40091/BNTX-Extractor/blob/master/swizzle.py
         * 
         *---------------------------------------*/

        public static uint GetBlockHeight(uint height)
        {
            uint blockHeight = pow2_round_up(height / 8);
            if (blockHeight > 16)
                blockHeight = 16;

            return blockHeight;
        }

        public static uint DIV_ROUND_UP(uint n, uint d)
        {
            return (n + d - 1) / d;
        }
        public static uint round_up(uint x, uint y)
        {
            return ((x - 1) | (y - 1)) + 1;
        }
        public static uint pow2_round_up(uint x)
        {
            x -= 1;
            x |= x >> 1;
            x |= x >> 2;
            x |= x >> 4;
            x |= x >> 8;
            x |= x >> 16;
            return x + 1;
        }

        private static byte[] _swizzle(uint width, uint height, uint depth, uint blkWidth, uint blkHeight, uint blkDepth, int roundPitch, uint bpp, uint tileMode, int blockHeightLog2, byte[] data, int toSwizzle)
        {
            uint block_height = (uint)(1 << blockHeightLog2);

            width = DIV_ROUND_UP(width, blkWidth);
            height = DIV_ROUND_UP(height, blkHeight);
            depth = DIV_ROUND_UP(depth, blkDepth);

            uint pitch;
            uint surfSize;
            if (tileMode == 1)
            {
                pitch = width * bpp;

                if (roundPitch == 1)
                    pitch = round_up(pitch, 32);

                surfSize = pitch * height;
            }
            else
            {
                pitch = round_up(width * bpp, 64);
                surfSize = pitch * round_up(height, block_height * 8);
            }

            byte[] result = new byte[surfSize];

            for (uint y = 0; y < height; y++)
            {
                for (uint x = 0; x < width; x++)
                {
                    uint pos;
                    uint pos_;

                    if (tileMode == 1)
                        pos = y * pitch + x * bpp;
                    else
                        pos = getAddrBlockLinear(x, y, width, bpp, 0, block_height);

                    pos_ = (y * width + x) * bpp;

                    if (pos + bpp <= surfSize && (toSwizzle == 0 ? pos + bpp <= data.Length : pos_ + bpp <= data.Length))
                    {
                        if (toSwizzle == 0)
                            Array.Copy(data, pos, result, pos_, bpp);
                        else
                            Array.Copy(data, pos_, result, pos, bpp);
                    }
                }
            }
            return result;
        }

        public static byte[] deswizzle(uint width, uint height, uint depth, uint blkWidth, uint blkHeight, uint blkDepth, int roundPitch, uint bpp, uint tileMode, int size_range, byte[] data)
        {
            return _swizzle(width, height, depth, blkWidth, blkHeight, blkDepth, roundPitch, bpp, tileMode, size_range, data, 0);
        }

        public static byte[] swizzle(uint width, uint height, uint depth, uint blkWidth, uint blkHeight, uint blkDepth, int roundPitch, uint bpp, uint tileMode, int size_range, byte[] data)
        {
            return _swizzle(width, height, depth, blkWidth, blkHeight, blkDepth, roundPitch, bpp, tileMode, size_range, data, 1);
        }

        static uint getAddrBlockLinear(uint x, uint y, uint width, uint bytes_per_pixel, uint base_address, uint block_height)
        {
            /*
              From Tega X1 TRM 
                               */
            uint image_width_in_gobs = DIV_ROUND_UP(width * bytes_per_pixel, 64);


            uint GOB_address = (base_address
                                + (y / (8 * block_height)) * 512 * block_height * image_width_in_gobs
                                + (x * bytes_per_pixel / 64) * 512 * block_height
                                + (y % (8 * block_height) / 8) * 512);

            x *= bytes_per_pixel;

            uint Address = (GOB_address + ((x % 64) / 32) * 256 + ((y % 8) / 2) * 64
                            + ((x % 32) / 16) * 32 + (y % 2) * 16 + (x % 16));
            return Address;
        }
    }
}