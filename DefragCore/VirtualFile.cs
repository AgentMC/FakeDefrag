namespace DefragCore
{
    /// <summary>
    /// File Descriptor
    /// </summary>
    /// <param name="Name">Full path, name and extension</param>
    /// <param name="Size">Size in bytes</param>
    /// <param name="DataLocation">Physical memory pointers. Units are FS clusters.</param>
    public record VirtualFile(string Name, ulong Size, List<VirtualClusterSequence> DataLocation);
}
