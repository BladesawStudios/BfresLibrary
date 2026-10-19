namespace BfresLibrary
{
    /// <summary>
    /// A model saved the way the game's mesh codec packages hold it: the BFRES without its buffers, and the buffers as two streams.
    /// </summary>
    /// <remarks>
    /// Body is the file as far as its size field says, with every offset to a buffer relative to where the buffers will be placed, which is
    /// after the file and the 0x120 bytes the mesh codec leaves for the memory pool. IndexStream holds every mesh's indices, each aligned to
    /// eight bytes; VertexStream every vertex buffer after it. The two alignments are the ones the mesh section is to declare.
    /// </remarks>
    public sealed class MeshCodecParts
    {
        public byte[] Body { get; internal set; }

        public byte[] IndexStream { get; internal set; }

        public byte[] VertexStream { get; internal set; }

        public uint IndexAlign { get; internal set; } = 8;

        public uint VertexAlign { get; internal set; } = 8;
    }
}
