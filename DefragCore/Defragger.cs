namespace DefragCore
{
    public class Defragger (VirtualFileSystem fileSystem, VirtualHardDisk disk)
    {
        const uint SectorsPerCluster = VirtualFileSystem.ClusterSize / VirtualHardDisk.SectorLength;

        public void Defrag()
        {
            //1. Decide on first movable sector  - is it a file or a free space.
            //if a file - we'll start with it.

            var defragSectorPtr = disk.FindNextFreeSectorSpace(new(0, 1))!; //1.1 first localte lowest free space block

            //1.2 now search for the lowest address file
            ulong lowestFilePtr = ulong.MaxValue;
            int lowestFileIndex = -1;
            for (int i = 0; i < fileSystem.Files.Count; i++)
            {
                var f = fileSystem.Files[i];
                for (int j = 0; j < f.ClusterSegmentsLocation.Count; j++) 
                { 
                    var d = f.ClusterSegmentsLocation[j];
                    if((i==0  && j==0 ) || (d.Address < lowestFilePtr))
                    {
                        lowestFilePtr = d.Address;
                        lowestFileIndex = i;
                    }
                }
            }

            //1.3 if the first thing after the locked zone is a file and not free space, we start with that file at that position
            if(lowestFilePtr < defragSectorPtr.Address)
            {
                defragSectorPtr.Address = lowestFilePtr;
                defragSectorPtr.NumUnits = 0;
                fileSystem.PioritizeFileAtIdx(lowestFileIndex); //just move it to the top of the list to start with it
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
            for (int i = 0; i < fileSystem.Files.Count; i++)
            {
                var file = fileSystem.Files[i];
                //2.1
                int hostSegmentIdx = 0; //The index of the segment we are accumulating the rest into. It grows if the file needs to be broken in 2+ parts.
                for (int j = 0; j < file.ClusterSegmentsLocation.Count; /*manual update*/)
                {
                    OnProgress(totalProcessed, file.Name);
                    bool canMove = true; //flag to see if this iteration moved data successfully or newed to jump away
                    bool hostSegmentIncreased = false; //flag to track the growth in the minimum count of the file segments
                    var segmentOfClusters = file.ClusterSegmentsLocation[j];
                    if(segmentOfClusters.Address != defragSectorPtr.Address)
                    {
                        //2.2
                        OnMessage("Setting up free space...");
                        var segmentInSectors = segmentOfClusters.NumUnits * SectorsPerCluster;
                        while (defragSectorPtr.NumUnits < segmentInSectors)
                        {
                            var nextSectorAddress = defragSectorPtr.Address + defragSectorPtr.NumUnits * VirtualHardDisk.SectorLength;
                            var nextSectorState = disk.PeekAlignSector(ref nextSectorAddress);
                            if(nextSectorState == VirtualHardDisk.SectorState.Locked || nextSectorState == VirtualHardDisk.SectorState.Bad)
                            {
                                break; //this free space region cannot be expanded further
                            }
                            else if(nextSectorState == VirtualHardDisk.SectorState.Empty)
                            {
                                defragSectorPtr.NumUnits += disk.FindNextFreeSectorSpace(new(nextSectorAddress, 1))!.NumUnits;
                            }
                            else
                            {
                                (var targetFile, var targetSegment) = fileSystem.GetFileAndClusterSegmentAt(nextSectorAddress);
                                var targetSegmentInSectors = targetSegment.NumUnits * SectorsPerCluster;
                                var nextFreeSectorSpace = disk.FindNextFreeSectorSpace(new(nextSectorAddress, targetSegmentInSectors)) ?? disk.FindNextFreeSectorSpace(new(nextSectorAddress, SectorsPerCluster)); //at least 1 cluster space should be free for the move
                                if (nextFreeSectorSpace == null)
                                {
                                    break; //no more free space to move, proceed to break the current segment (the one we are trying to move into defragPtr, not the one we are moving away to free space)
                                }
                                if(nextFreeSectorSpace.NumUnits < targetSegmentInSectors)//next free block does not fit the entire sacrificial segment --> break the segment
                                {
                                    if (!targetFile.TryBreakSegment(targetSegment, nextFreeSectorSpace.NumUnits))
                                    { //free block is waaaay too small. We need to move the primary defragPtr out of it. Let's just fallback onto the next iteration.
                                        break;
                                    }
                                }
                                MoveData(targetSegment, nextFreeSectorSpace); //moving data out to free space
                                defragSectorPtr.NumUnits += targetSegment.NumUnits * SectorsPerCluster;
                            }
                        }
                        //check if we were able to reach the required length
                        if (defragSectorPtr.NumUnits < segmentInSectors) //still less than needed - have to break the current segment
                        {
                            canMove = file.TryBreakSegment(segmentOfClusters, defragSectorPtr.NumUnits);
                            if(!canMove && defragSectorPtr.Address + defragSectorPtr.NumUnits* VirtualHardDisk.SectorLength >= disk.SizeBytes - 1)
                            {
                                throw new InvalidDataException("Fragmentation too rough, unable to move a single solid cluster. Please delete some files.");
                            }
                        }

                        //2.3
                        if (canMove)
                        {
                            OnMessage("Moving data...");
                            MoveData(segmentOfClusters, defragSectorPtr);
                        }

                        //2.4
                        if (!canMove)
                        {
                            hostSegmentIdx++; //a blocked piece of physical space in front of us, this file will have >1 segment.
                            hostSegmentIncreased = true;
                        }
                        else if (j == hostSegmentIdx)
                        {
                            j++;
                        }
                        else if (j == hostSegmentIdx + 1)
                        {
                            file.ClusterSegmentsLocation[j - 1].NumUnits += segmentOfClusters.NumUnits;
                            file.ClusterSegmentsLocation.RemoveAt(j);
                        }
                        else
                        {
                            throw new Exception("How did we go to segment > hostSegmentIdx + 1?");
                        }
                    }
                    //2.5
                    if (canMove)
                    {
                        var amountMoved = segmentOfClusters.NumUnits * VirtualFileSystem.ClusterSize;
                        totalProcessed += amountMoved;
                        defragSectorPtr.Address += amountMoved;
                    }
                    else //we did not move data. The current ptr points to an unusable area. Need to jump ahead without skipping segments.
                    {
                        defragSectorPtr.Address += defragSectorPtr.NumUnits * VirtualHardDisk.SectorLength;
                    }
                    VirtualHardDisk.SectorState newSectorState; 
                    while((newSectorState = disk.PeekAlignSector(ref defragSectorPtr.Address)) > VirtualHardDisk.SectorState.Data /*while pointing at Locked or Bad sector*/)
                    {   /*This may theoretically throw if we're at the end of the disk and trying the sequence of locked and bad sectors*/
                        defragSectorPtr.Address += VirtualHardDisk.SectorLength; //try next sector
                        if (!hostSegmentIncreased) //we need to do it only once per segment, ignore if this is nth attempt or if !canMove
                        {
                            hostSegmentIdx++; //we have successfully written full _n_ clusters of data (canMove:true), but the very next sector is locked, so the file must still be split
                            hostSegmentIncreased = true; 
                        }
                    }
                    defragSectorPtr.NumUnits = newSectorState == VirtualHardDisk.SectorState.Data ? 0 : disk.FindNextFreeSectorSpace(new(defragSectorPtr.Address, 1))!.NumUnits;
                    //^^this is going to be our next sector to write the data. Size 0 means it's occupied and we have to move the data; othervise n free sectors
                }
            }

            //3
            OnProgress(totalProcessed, "<finished>");
            OnMessage("Defrag complete! Press ENTER to exit.");
        }

        private void MoveData(VirtualUnitSequence sourceClusterSegment, VirtualUnitSequence targetSectorLocation)
        {
            var dataSize = sourceClusterSegment.NumUnits * VirtualFileSystem.ClusterSize;
            disk.Read(sourceClusterSegment.Address, dataSize);
            disk.Write(targetSectorLocation.Address, dataSize);
            var oldLocation = sourceClusterSegment.Address;
            sourceClusterSegment.Address = targetSectorLocation.Address;
            disk.Clear(oldLocation, dataSize);
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
