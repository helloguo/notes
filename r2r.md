1. `./crossgen -JITPath ./libclrjit.so -Platform_Assemblies_Paths "/home/perftest/Mark/benchmarks-r2r/src/Benchmarks/bin/Release/netcoreapp2.0/ubuntu.16.04-x64/publish" -ReadyToRun /home/perftest/Mark/benchmarks-r2r/src/Benchmarks/bin/Release/netcoreapp2.0/ubuntu.16.04-x64/publish/Microsoft.AspNetCore.Server.Kestrel.Core.dll`

Then we got
```
Microsoft (R) CoreCLR Native Image Generator - Version 4.5.22220.0
Copyright (c) Microsoft Corporation.  All rights reserved.

Native image /home/perftest/Mark/benchmarks-r2r/src/Benchmarks/bin/Release/netcoreapp2.0/ubuntu.16.04-x64/publish/Microsoft.AspNetCore.Server.Kestrel.Core.ni.dll generated successfully.

```

2. `./crossgen -JITPath ./libclrjit.so -Platform_Assemblies_Paths "/home/perftest/Mark/benchmarks-r2r/src/Benchmarks/bin/Release/netcoreapp2.0/ubuntu.16.04-x64/publish" -ReadyToRun /home/perftest/Mark/benchmarks-r2r/src/Benchmarks/bin/Release/netcoreapp2.0/ubuntu.16.04-x64/publish/Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.dll`

Then we got
```
Microsoft (R) CoreCLR Native Image Generator - Version 4.5.22220.0
Copyright (c) Microsoft Corporation.  All rights reserved.

Native image /home/perftest/Mark/benchmarks-r2r/src/Benchmarks/bin/Release/netcoreapp2.0/ubuntu.16.04-x64/publish/Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.ni.dll generated successfully.

```
