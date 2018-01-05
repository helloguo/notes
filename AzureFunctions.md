Languages:
    There are three levels of support:
        Generally available (GA) - Fully supported and approved for production use.
        Preview - Not yet supported but is expected to reach GA status in the future.
        Experimental - Not supported and might be abandoned in the future; no guarantee of eventual preview or GA status.

    Two versions of the Azure Functions runtime are available. The 1.x runtime is GA. It's the only runtime that is approved for production applications. The 2.x runtime is currently in preview, so the languages it supports are in preview. The following table shows which languages are supported in each runtime version.

    Language                1.x                     2.x
    C#                      GA                      Preview
    JavaScript              GA                      Preview
    F#                      GA	
    Java                                            Preview
    Python	                Experimental	
    PHP                     Experimental	
    TypeScript              Experimental	
    Batch (.cmd, .bat)      Experimental	
    Bash	                Experimental	
    PowerShell              Experimental


The following namespaces are automatically imported and are therefore optional:
1. System
2. System.Collections.Generic
3. System.IO
4. System.Linq
5. System.Net.Http
6. System.Threading.Tasks
7. Microsoft.Azure.WebJobs
8. Microsoft.Azure.WebJobs.Host


The following assemblies are automatically added by the Azure Functions hosting environment:
1. mscorlib
2. System
3. System.Core
4. System.Xml
5. System.Net.Http
6. Microsoft.Azure.WebJobs
7. Microsoft.Azure.WebJobs.Host
8. Microsoft.Azure.WebJobs.Extensions
9. System.Web.Http
10. System.Net.Http.Formatting


The following assemblies may be referenced by simple-name (for example, #r "AssemblyName"):
1. Newtonsoft.Json
2. Microsoft.WindowsAzure.Storage
3. Microsoft.ServiceBus
4. Microsoft.AspNet.WebHooks.Receivers
5. Microsoft.AspNet.WebHooks.Common
6. Microsoft.Azure.NotificationHubs


To use NuGet packages in a C# function, upload a project.json file to the function's folder in the function app's file system. Only the .NET Framework 4.6 is supported, so make sure that your project.json file specifies `net46`.