## Linux

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
## Windows

1. `C:\Users\Administrator\Mark\r2r\win10-x64-r2r\publish>crossgen.exe /Platform_Assemblies_Paths "C:\Users\Administrator\Mark\r2r\win10-x64-r2r\publish" /ReadyToRun Microsoft.AspNetCore.Server.Kestrel.Core.dll`

Then we got
```
Microsoft (R) CoreCLR Native Image Generator - Version 4.5.22220.0
Copyright (c) Microsoft Corporation.  All rights reserved.

Native image C:\Users\Administrator\Mark\r2r\win10-x64-r2r\publish\Microsoft.AspNetCore.Server.Kestrel.Core.ni.dll generated successfully.
```

2. `C:\Users\Administrator\Mark\r2r\win10-x64-r2r\publish>crossgen.exe /Platform_Assemblies_Paths "C:\Users\Administrator\Mark\r2r\win10-x64-r2r\publish" /ReadyToRun Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.dll`

Then we got
```
Microsoft (R) CoreCLR Native Image Generator - Version 4.5.22220.0
Copyright (c) Microsoft Corporation.  All rights reserved.

Native image C:\Users\Administrator\Mark\r2r\win10-x64-r2r\publish\Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.ni.dll generated successfully.
```

3. Rename them.

4. `C:\Users\Administrator\Mark\r2r\win10-x64-r2r\publish>crossgen.exe /Platform_Assemblies_Paths "C:\Users\Administrator\Mark\r2r\win10-x64-r2r\publish" /CreatePdb "C:\Users\Administrator\Mark\r2r\win10-x64-r2r\publish" "C:\Users\Administrator\Mark\r2r\win10-x64-r2r\publish\Microsoft.AspNetCore.Server.Kestrel.Core.dll"`

Then we got
```
Microsoft (R) CoreCLR Native Image Generator - Version 4.5.22220.0
Copyright (c) Microsoft Corporation.  All rights reserved.

Successfully generated PDB for native assembly 'C:\Users\Administrator\Mark\r2r\win10-x64-r2r\publish\Microsoft.AspNetCore.Server.Kestrel.Core.dll'.
```

5. `C:\Users\Administrator\Mark\r2r\win10-x64-r2r\publish>crossgen.exe /Platform_Assemblies_Paths "C:\Users\Administrator\Mark\r2r\win10-x64-r2r\publish" /CreatePdb "C:\Users\Administrator\Mark\r2r\win10-x64-r2r\publish" "C:\Users\Administrator\Mark\r2r\win10-x64-r2r\publish\Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.dll"`

Then we got
```
Microsoft (R) CoreCLR Native Image Generator - Version 4.5.22220.0
Copyright (c) Microsoft Corporation.  All rights reserved.

Successfully generated PDB for native assembly 'C:\Users\Administrator\Mark\r2r\win10-x64-r2r\publish\Microsoft.AspNetCore.Server.Kestrel.Transport.Abstractions.dll'.
```
