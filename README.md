# ComPortCapture

[![Build Status][BS img]][Build Status]  [![Release Link][Release img]][Release Link]

Capture output from a COM port, with each line timestamped.
It also supports output as hex

## Usage

```txt
ComPortCapture, version: 1.0.30

   Usage: ComPortCapture [com=]<comPort> [baud=<baudRate>] [config=<db,sb,pa,fl>] [noempty]
                         [logpath=<logFilePath>] [logsize=<maxLogSize>] [binfile=<binLogPath>]
                         [hex[=<hexCols>]]

      where:
         comPort:     The COM port to connect to, eg. COM1.
         baudRate:    The baud rate. Default is 115200.
         config:      Comma-seperated port configuration. Default is 8,1,n,n.
                        db = data bits (5-8)
                        sb = stop bits (0, 1, 1.5, 2)
                        pa = parity (n=none, o=odd, e=even, m=mark, s=space)
                        fl = flow control (n=none, r=rts/cts, x=xon/xoff, b=rts/cts and xon/xoff)
         noempty:     Ignore empty lines.
         logFilePath: The directory to log to. Logging disabled if omitted.
         maxLogSize:  The size at which the log file rolls over, eg. 10KB, 1MB etc. Default is 10 MB.
         binLogPath:  The path to a file to log data in a binary format.
         hex:         Display data as hex. Optionally specify the number of columns. Default is 16.
```

For example,

```ComPortCapture com3 baud=11520 noempty logpath=Logs```

[Build Status]: https://ci.appveyor.com/project/KeithFletcher/comportcapture
[BS img]: https://ci.appveyor.com/api/projects/status/vvtdknw55lih8l8w?svg=true

[Release Link]: https://github.com/HisRoyalRedness/ComPortCapture/releases/latest
[Release img]: https://img.shields.io/github/v/release/HisRoyalRedness/ComPortCapture

## Git

When cloning the repo, make sure you fetch submodules as well

```git
git clone --recurse-submodules https://github.com/HisRoyalRedness/ComPortCapture
```
