## Languages
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

## APIs

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

For framework assemblies, add references by using the #r "AssemblyName" directive.


To use NuGet packages in a C# function, upload a project.json file to the function's folder in the function app's file system. Only the `.NET Framework 4.6` is supported, so make sure that your project.json file specifies `net46`. When you upload a project.json file, the runtime gets the packages and automatically adds references to the package assemblies. You don't need to add `#r "AssemblyName" `directives. To use the types defined in the NuGet packages; just add the required `using` statements to your run.csx file.


One example that shows different API surface is file IO related API.

On local machine, the following code shows how to read a file:
```
    class Program
    {
        static void Main(string[] args)
        {
            // put this file in local computer
            string path = @"C:\Users\guo\work\faas\testio\MyTest.txt";

            // Open the file to read from.
            string[] readText = File.ReadAllLines(path);
            foreach (string s in readText)
            {
                Console.WriteLine(s);
            }
        }
    }
```

On Azure cloud, we usually use Azure Blob to store files. Similarly, Azure Functions uses Azure Blob to access files as well. In order to use Azure Blob, we need to integrate Azure Blob Storage as input or output based on how we want to manipulate it. For the above example, we use the file as input. So we integrate Azure Blob Storage as Inputs. Afterward we will have a `function.json` in same directory with `run.csx`. The following code shows what `function.json` looks like:

```
{
  "bindings": [
    {
      "authLevel": "function",
      "name": "req",
      "type": "httpTrigger",
      "direction": "in"
    },
    {
      "name": "$return",
      "type": "http",
      "direction": "out"
    },
    {
      "type": "blob",
      "name": "myInputFile",
      "path": "myfiles/MyTest.txt",
      "connection": "AzureWebJobsStorage",
      "direction": "in"
    }
  ],
  "disabled": false
}
```

In order to dump the file, we can simply use `log` as shown below:
```
public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log, string myInputFile)
{
    log.Info(myInputFile);

    return req.CreateResponse(HttpStatusCode.OK);
}
```

## Work with other Azure Services
Azure Functions integrates with various Azure and 3rd-party services. These services can trigger your function and start execution, or they can serve as input and output for your code. The following service integrations are supported by Azure Functions (updated by 10/03/2017):
1. Azure Cosmos DB
2. Azure Event Hubs
3. Azure Event Grid
4. Azure Mobile Apps (tables)
5. Azure Notification Hubs
6. Azure Service Bus (queues and topics)
7. Azure Storage (blob, queues, and tables)
8. GitHub (webhooks)
9. On-premises (using Service Bus)
10. Twilio (SMS messages)


## References
1. https://docs.microsoft.com/en-us/azure/azure-functions/
2. Serverlessconf Austin '17 Keynote - John Gossman https://www.youtube.com/watch?v=O5QUBEPZrqM