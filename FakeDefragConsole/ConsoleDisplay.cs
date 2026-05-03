using DefragCore;
using System.Text;

namespace FakeDefragConsole
{
    internal class ConsoleDisplay
    {
        private int lastWidth, lastHeight, mapHeight, mapWidth;
        private ulong sectorsPerBlock;
        private int SplitterX => lastWidth - 40;
        private int MessageMaxLength => SplitterX - 13;

        const string Title = "Fake Defragmentator 2026 Pro Max";
        const char DataPresent = '\u2588',
                   Locked = '\u2592',
                   Bad = '\u25A8',
                   Empty = '\u2B1E';
        private readonly VirtualHardDisk _disk;

        public ConsoleDisplay(VirtualHardDisk disk, Defragger defragger)
        {
            _disk = disk;
            disk.ReadBegin += Disk_ReadBegin;
            disk.ReadEnd += Disk_OpEnd;
            disk.WriteBegin += Disk_WriteBegin;
            disk.WriteEnd += Disk_OpEnd;

            defragger.ProgressChanged += Defragger_ProgressChanged;
            defragger.MessageReceived += Defragger_MessageReceived;

            Console.OutputEncoding = Encoding.Unicode;

            BeginScreen();
        }

        private void CheckForResize()
        {
            if (Console.BufferHeight != lastHeight || Console.BufferWidth != lastWidth)
            {
                BeginScreen();
            }
        }


        private void BeginScreen()
        {
            lastWidth = Console.BufferWidth; 
            lastHeight = Console.BufferHeight;
            Console.Clear();
            Console.Write($"\u2554{new string('\u2550', (lastWidth - (4+Title.Length))/2)} {Title} {new string('\u2550', (int)Math.Ceiling((lastWidth - (4 + Title.Length)) / 2.0))}\u2557\u2551");
            for (int i = 1; i < lastHeight - 5; i++)
            {
                Console.CursorLeft = lastWidth - 1;
                Console.Write("\u2551\u2551");
            }
            Console.CursorLeft = 0;
            Console.Write($"\u2560{new string('\u2550', SplitterX - 2)}\u2566{new string('\u2550', lastWidth - SplitterX - 1)}\u2563");
            for (int i = 0; i < 3; i++)
            {
                Console.Write($"\u2551{new string(' ', SplitterX - 2)}\u2551{new string(' ', lastWidth - SplitterX - 1)}\u2551");
            }
            Console.Write($"\u255A{new string('\u2550', SplitterX - 2)}\u2569{new string('\u2550', lastWidth - SplitterX - 1)}\u255D");

            Console.SetCursorPosition(2, lastHeight - 4);
            Console.Write("Status  : loading...");
            Console.CursorLeft = SplitterX + 2;
            Console.Write($"{DataPresent} Allocated     {Bad} Bad");

            Console.SetCursorPosition(2, lastHeight - 3);
            Console.Write("Progress:");
            Console.CursorLeft = SplitterX + 2;
            Console.Write($"{Locked} Locked        R Reading");

            Console.SetCursorPosition(2, lastHeight - 2);
            Console.Write("File    :");
            Console.CursorLeft = SplitterX + 2;
            Console.Write($"{Empty} Empty         W Writing");

            mapHeight = lastHeight - 8;
            mapWidth = lastWidth - 4;
            sectorsPerBlock = (ulong)Math.Ceiling((double)(_disk.SizeBytes / VirtualHardDisk.SectorLength) / (mapWidth * mapHeight));
            var addressIncrement = sectorsPerBlock * VirtualHardDisk.SectorLength;
            ulong address = 0;
            for (int i = 0; i < mapHeight; i++)
            {
                Console.SetCursorPosition(2, 2 + i);
                for (int j = 0; j < mapWidth; j++)
                {
                    Console.Write(MapAddressToSymbol(ref address));
                    address += addressIncrement;
                }
            }
            SetStatus($"Defragmenting. {sectorsPerBlock} sectors per block. {_disk.SpaceAvailable} free bytes ({(double)_disk.SpaceAvailable / _disk.SizeBytes * 100:F2}%)");
        }

        private char MapAddressToSymbol(ref ulong address)
        {
            char symbol = ' ';
            if (address < _disk.SizeBytes)
            {
                var state = _disk.PeekAlignSector(ref address);
                switch (state)
                {
                    case VirtualHardDisk.SectorState.Data:
                        symbol = DataPresent;
                        break;
                    case VirtualHardDisk.SectorState.Locked:
                        symbol = Locked;
                        break;
                    case VirtualHardDisk.SectorState.Bad:
                        symbol = Bad;
                        break;
                    case VirtualHardDisk.SectorState.Empty:
                        symbol = Empty;
                        break;
                    default:
                        break;
                }
            }
            return symbol;
        }

        private string ProcessMessage(string msg)
        {
            if(msg.Length > MessageMaxLength)
            {
                return msg[MessageMaxLength..];
            }
            else if(msg.Length < MessageMaxLength)
            {
                return msg + new string(' ', MessageMaxLength - msg.Length);
            }
            return msg;
        }

        private void SetStatus(string msg)
        {
            CheckForResize();
            Console.SetCursorPosition(12, lastHeight - 4);
            Console.Write(ProcessMessage(msg));
        }
        
        private void Disk_WriteBegin(object? sender, VirtualHardDisk.DiskOperationEventArgs e)
        {
            ProcessDiskOperation(e, 'W');
        }

        private void Disk_ReadBegin(object? sender, VirtualHardDisk.DiskOperationEventArgs e)
        {
            ProcessDiskOperation(e, 'R');
        }

        private void Disk_OpEnd(object? sender, VirtualHardDisk.DiskOperationEventArgs e)
        {
            ProcessDiskOperation(e, null);
        }

        private void ProcessDiskOperation(VirtualHardDisk.DiskOperationEventArgs e, char? symbol)
        {
            char symbolToWrite;
            var sectorEventStart = e.Address / VirtualHardDisk.SectorLength;
            var sectorEventEnd = (e.Address + e.Length) / VirtualHardDisk.SectorLength;
            var blockStart = sectorEventStart / sectorsPerBlock;
            var blockEnd = sectorEventEnd / sectorsPerBlock;
            for (var block = blockStart; block <= blockEnd; block++)
            {
                CheckForResize();
                (var row, var col) = Math.DivRem((int)block, mapWidth);
                Console.SetCursorPosition(2 + col, 2 + row);
                if(symbol == null)
                {
                    ulong address = block * sectorsPerBlock * VirtualHardDisk.SectorLength;
                    symbolToWrite = MapAddressToSymbol(ref address);
                }
                else
                {
                    symbolToWrite = symbol.Value;
                }
                Console.Write(symbolToWrite);
            }
        }

        private void Defragger_MessageReceived(object? sender, Defragger.MessageEventArgs e) => SetStatus(e.Message);

        private void Defragger_ProgressChanged(object? sender, Defragger.ProgressEventArgs e)
        {
            CheckForResize();
            var progress = (double)e.BytesCopied / (_disk.SpaceAllocated - 3 * VirtualHardDisk.BootLoaderSize);
            var pbLength = MessageMaxLength - 9;//[100.00% ... ]
            var pbFull = (int)Math.Round(pbLength * progress, MidpointRounding.AwayFromZero);
            var pbLight = pbLength - pbFull;
            Console.SetCursorPosition(12, lastHeight - 3);
            Console.Write(string.Format("{0,6:F2}% {1}{2}", progress*100, new string('\u2593', pbFull), new string('\u2591', pbLight)));
            Console.SetCursorPosition(12, lastHeight - 2);
            Console.Write(ProcessMessage(e.FileName));
        }
    }
}
