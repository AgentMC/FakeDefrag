using DefragCore;
using FakeDefragConsole;

var disk = new VirtualHardDisk(20 * 1024 * 1024);
var fs = new VirtualFileSystem(disk);
var defragger = new Defragger(fs, disk);
var display = new ConsoleDisplay(disk, defragger);
defragger.Defrag();
Console.ReadLine();
