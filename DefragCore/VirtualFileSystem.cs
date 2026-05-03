namespace DefragCore
{
    public class VirtualFileSystem
    {
        public readonly List<VirtualFile> files = [];


        const uint MaxGenerateFiles = 10;
        public const uint ClusterSize = 4096;

        public VirtualFileSystem(VirtualHardDisk disk)
        {
            uint SectorsPerCluster = ClusterSize / VirtualHardDisk.SectorLength;
            for (int i = 0; i < MaxGenerateFiles; i++)
            {
                var file = new VirtualFile(GetRealPath(), (ulong)Random.Shared.Next(1024, 10 * 1024 * 1024), []);
                files.Add(file);
                ulong sizeRemaining = file.Size;
                while(sizeRemaining > 0)
                {
                    if (sizeRemaining > disk.SpaceAvailable || disk.SpaceAvailable < disk.SizeBytes * 0.015) break;
                    //find a new random spot to write
                    ulong location = 0;
                    bool success = false;
                    while (!success)
                    {
                        location = (ulong)(Random.Shared.NextDouble() * disk.SizeBytes);
                        success = disk.PeekAlignSector(ref location) == VirtualHardDisk.SectorState.Empty;
                    }

                    //how much do we still HAVE to write?
                    var clustersRemaining = (int)Math.Ceiling((double)sizeRemaining / ClusterSize);
                    //of that, how much do we WANT to write in the current chunk?
                    var maxClusterWrite = Math.Min(Math.Max(Random.Shared.Next(1, clustersRemaining + 1), 3), clustersRemaining);
                    var maxSectorsWrite = maxClusterWrite * SectorsPerCluster;
                    //of that, how much CAN we write sequentially?
                    uint realClustersToWrite = 0;
                    for (uint j = 0; j < maxSectorsWrite; j++)
                    {
                        var targetAddress = location + j * VirtualHardDisk.SectorLength;
                        if (targetAddress >= disk.SizeBytes || disk.PeekAlignSector(ref targetAddress) != VirtualHardDisk.SectorState.Empty) break;
                        if ((j + 1) % SectorsPerCluster == 0) realClustersToWrite++;
                    }
                    //let's write!
                    var dataSizeToWrite = realClustersToWrite * ClusterSize;
                    disk.Write(location, dataSizeToWrite); //allocate data on disk
                    file.DataLocation.Add(new(location, realClustersToWrite)); //add chunk to the reference table
                    sizeRemaining -= Math.Min(dataSizeToWrite, sizeRemaining);
                }
            }
            disk.InitComplete = true;
        }

        private static string GetRealPath()
        {
            var depth = Random.Shared.Next(2, 5);
            var dirInfos = Directory.GetDirectories("C:\\");
            for (int i = 0; i < depth; i++)
            {
                var d = dirInfos;
                try
                {
                    dirInfos = Directory.GetDirectories(dirInfos[Random.Shared.Next(dirInfos.Length)]);
                }
                catch (Exception)
                {
                    dirInfos = [];
                }
                if (dirInfos.Length == 0) dirInfos = d;
            }
            var path = dirInfos[Random.Shared.Next(dirInfos.Length)];
            try
            {
                dirInfos = Directory.GetFiles(path);
            }
            catch (Exception)
            {
                dirInfos = [];
            }
            return dirInfos.Length > 0 ? dirInfos[Random.Shared.Next(dirInfos.Length)] : Path.Combine(path, DateTime.Now.Ticks.ToString("X")[^8..] + ".tmp");
        }
    }
}
