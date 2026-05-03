namespace DefragCore
{
    public class Defragger (VirtualFileSystem fileSystem, VirtualHardDisk disk)
    {
        const uint SectorsPerCluster = VirtualFileSystem.ClusterSize / VirtualHardDisk.SectorLength;

        public void Defrag()
        {
            //1. Decide on first movable sector  - is it a file or a free space.
            //if a file - we'll start with it.

            var defragPtr = disk.FindNextFreeSpace(new(0, 1))!; //1.1 first localte lowest free space block

            //1.2 now search for the lowest address file
            ulong lowestFilePtr = ulong.MaxValue;
            int lowestFileIndex = -1;
            for (int i = 0; i < fileSystem.files.Count; i++)
            {
                var f = fileSystem.files[i];
                for (int j = 0; j < f.DataLocation.Count; j++) 
                { 
                    var d = f.DataLocation[j];
                    if((i==0  && j==0 ) || (d.address < lowestFilePtr))
                    {
                        lowestFilePtr = d.address;
                        lowestFileIndex = i;
                    }
                }
            }

            //1.3 if the first thing after the locked zone is a file and not free space, we start with that file at that position
            if(lowestFilePtr < defragPtr.address)
            {
                defragPtr.address = lowestFilePtr;
                defragPtr.numUnits = 0;
                var vf = fileSystem.files[lowestFileIndex];
                fileSystem.files.RemoveAt(lowestFileIndex); //just move it to the top of the list to start with it
                fileSystem.files.Insert(0, vf);
            }

            //2. for each file i
            //2.1   for each segment j (actually j only is 0 or 1, after 1 the list gets reduced instead of j growing)
            //          raise event for file + Total Processed (moved lower)
            //2.2       ensure free space at defragPtr for the segment
            //              event Looking for Free Space
            //              if available free segments is not enough, then
            //                  move data away until enough or until no more free space
            //                      find the file occupying the sector after defragPtr
            //                      get next free space from the sector after defragPtr
            //                          break the occupying segment if needed
            //                      move data (the occupying segment)
            //                  if still not enough then break the current file segment to move what we can
            //2.3       move the current segment
            //              event moving data
            //              read source
            //              write destination
            //              clear source
            //2.4       update segment
            //              if idx != 0, merge Size with previous and remove at idx
            //              j--
            //2.5       stats
            //              update TotalProcessed
            //              update defragPtr
            //3. event Progress+Completed

            //2
            ulong totalProcessed = 0;
            for (int i = 0; i < fileSystem.files.Count; i++)
            {
                var file = fileSystem.files[i];
                //2.1
                int hostSegmentIdx = 0;
                for (int j = 0; j < file.DataLocation.Count; /*manual update*/)
                {
                    OnProgress(totalProcessed, file.Name);
                    bool canMove = true; //flag to see if this iteration moved data successfully or newed to jump away
                    var segment = file.DataLocation[j];
                    if(segment.address != defragPtr.address)
                    {
                        //2.2
                        OnMessage("Setting up free space...");
                        var segmentInSectors = segment.numUnits * SectorsPerCluster;
                        while (defragPtr.numUnits < segmentInSectors)
                        {
                            var nextSectorAddress = defragPtr.address + defragPtr.numUnits * VirtualHardDisk.SectorLength;
                            var nextSectorState = disk.PeekAlignSector(ref nextSectorAddress);
                            if(nextSectorState == VirtualHardDisk.SectorState.Locked || nextSectorState == VirtualHardDisk.SectorState.Bad)
                            {
                                break; //this free space region cannot be expanded further
                            }
                            else if(nextSectorState == VirtualHardDisk.SectorState.Empty)
                            {
                                defragPtr.numUnits += disk.FindNextFreeSpace(new(nextSectorAddress, 1))!.numUnits;
                            }
                            else
                            {
                                (var targetFile, var targetSegment) = GetFileAndSegmentAt(nextSectorAddress);
                                var targetSegmentInSectors = targetSegment.numUnits * SectorsPerCluster;
                                var nextFreeSpace = disk.FindNextFreeSpace(new(nextSectorAddress, targetSegmentInSectors)) ?? disk.FindNextFreeSpace(new(nextSectorAddress, SectorsPerCluster)); //at least 1 cluster space should be free for the move
                                if (nextFreeSpace == null)
                                {
                                    break; //no more free space to move, proceed to break the current segment (the one we are trying to move into defragPtr, not the one we are moving away to free space)
                                }
                                if(nextFreeSpace.numUnits < targetSegmentInSectors)//next free block does not fit the entire sacrificial segment --> break the segment
                                {
                                    if (!BreakSegment(targetSegment, targetFile, nextFreeSpace.numUnits))
                                    { //free block is waaaay too small. We need to move the primary defragPtr out of it. Let's just fallback onto the next iteration.
                                        break;
                                    }
                                }
                                MoveData(targetSegment, nextFreeSpace); //moving data out to free space
                                defragPtr.numUnits += targetSegment.numUnits * SectorsPerCluster;
                            }
                        }
                        //check if we were able to reach the required length
                        if (defragPtr.numUnits < segmentInSectors) //still less than needed - have to break the current segment
                        {
                            canMove = BreakSegment(segment, file, defragPtr.numUnits);
                            if(!canMove && defragPtr.address + defragPtr.numUnits* VirtualHardDisk.SectorLength >= disk.SizeBytes - 1)
                            {
                                throw new InvalidDataException("Fragmentation too rough, unable to move a single solid cluster. Please delete some files.");
                            }
                        }

                        //2.3
                        if (canMove)
                        {
                            OnMessage("Moving data...");
                            MoveData(segment, defragPtr);
                        }

                        //2.4
                        if (!canMove)
                        {
                            hostSegmentIdx++; //a blocked piece of physical space in front of us, this file will have >1 segment.
                        }
                        else if (j == hostSegmentIdx)
                        {
                            j++;
                        }
                        else if (j == hostSegmentIdx + 1)
                        {
                            file.DataLocation[j - 1].numUnits += segment.numUnits;
                            file.DataLocation.RemoveAt(j);
                        }
                        else
                        {
                            throw new Exception("How did we go to segment > hostSegmentIdx + 1?");
                        }
                    }
                    //2.5
                    if (canMove)
                    {
                        var amountMoved = segment.numUnits * VirtualFileSystem.ClusterSize;
                        totalProcessed += amountMoved;
                        defragPtr.address += amountMoved;
                    }
                    else //we did not move data. The current ptr points to an unusable area. Need to jump ahead without skipping segments.
                    {
                        defragPtr.address += defragPtr.numUnits * VirtualHardDisk.SectorLength;
                    }
                    VirtualHardDisk.SectorState newSectorState; 
                    while((newSectorState = disk.PeekAlignSector(ref defragPtr.address)) > VirtualHardDisk.SectorState.Data)
                    {
                        defragPtr.address += VirtualHardDisk.SectorLength;
                    }
                    defragPtr.numUnits = newSectorState == VirtualHardDisk.SectorState.Data ? 0 : disk.FindNextFreeSpace(new(defragPtr.address, 1))!.numUnits;
                }
            }

            //3
            OnProgress(totalProcessed, "<finished>");
            OnMessage("Defrag complete!");
        }

        private void MoveData(VirtualClusterSequence sourceClusterSegment, VirtualClusterSequence targetSectorLocation)
        {
            var dataSize = sourceClusterSegment.numUnits * VirtualFileSystem.ClusterSize;
            disk.Read(sourceClusterSegment.address, dataSize);
            disk.Write(targetSectorLocation.address, dataSize);
            var oldLocation = sourceClusterSegment.address;
            sourceClusterSegment.address = targetSectorLocation.address;
            disk.Clear(oldLocation, dataSize);
        }

        private bool BreakSegment(VirtualClusterSequence segmentOfClusters, VirtualFile file, uint availableSectors)
        {
            var movableClusters = availableSectors / SectorsPerCluster;//we can only move this many clusters
            if (movableClusters == 0) return false;  //Unable to move a single solid cluster.
            var differenceInClusters = segmentOfClusters.numUnits - movableClusters;//actual unmovable difference to break
            segmentOfClusters.numUnits = movableClusters;              //actually break the segment
            var segmentIndex = file.DataLocation.IndexOf(segmentOfClusters);
            file.DataLocation.Insert(segmentIndex + 1, new(segmentOfClusters.address + segmentOfClusters.numUnits * VirtualFileSystem.ClusterSize, differenceInClusters));
            return true;
        }

        private (VirtualFile, VirtualClusterSequence) GetFileAndSegmentAt(ulong address)
        {
            for (int i = 0; i < fileSystem.files.Count; i++)
            {
                var f = fileSystem.files[i];
                for (int j = 0; j < f.DataLocation.Count; j++)
                {
                    var d = f.DataLocation[j];
                    if (d.address == address)
                    {
                        return (f, d);
                    }
                }
            }
            throw new Exception($"Unable to locate the file segment at address {address}");
        }

        public class ProgressEventArgs(ulong bytesCopied, string fileName) : EventArgs
        {
            public ulong BytesCopied { get; } = bytesCopied;
            public string FileName { get; } = fileName;
        }

        public class MessageEventArgs(string message):EventArgs 
        {
            public string Message { get; } = message; 
        }

        public event EventHandler<ProgressEventArgs>? ProgressChanged;
        public event EventHandler<MessageEventArgs>? MessageReceived;

        private void OnMessage(string msg) => MessageReceived?.Invoke(this, new(msg));
        private void OnProgress(ulong bytesCopied, string fileName) => ProgressChanged?.Invoke(this, new ProgressEventArgs(bytesCopied, fileName));
    }
}
