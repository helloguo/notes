# How does Azure WebJobs SDK Script work?

## gRPC basics
[gRPC](https://grpc.io/) is "an open source remote procedure call (RPC) system initially developed at Google. It uses HTTP/2 for transport, Protocol Buffers as the interface description language, and provides features such as authentication, bidirectional streaming and flow control, blocking or nonblocking bindings, and cancellation and timeouts. It generates cross-platform client and server bindings for many languages."[1] Azure WebJobs SDK Script uses gRPC to make remote procedure call.

### .proto file
gRPC uses a protobuffer file to generate codes for users. A .proto file usually has:
1. `syntax`, which defines the version of protobuffer.
2. `package` name so that the generated codes are in this package.
3. `service`, which has a service name and one or multiple `rpc` methods. The `rpc` methods take a request and return a reply.
4. `message`, which is sent between servers and clients. 

For example[2]:
```
syntax = "proto3";

option java_multiple_files = true;
option java_package = "io.grpc.examples.helloworld";
option java_outer_classname = "HelloWorldProto";
option objc_class_prefix = "HLW";

package helloworld;

// The greeting service definition.
service Greeter {
  // Sends a greeting
  rpc SayHello (HelloRequest) returns (HelloReply) {}
}

// The request message containing the user's name.
message HelloRequest {
  string name = 1;
}

// The response message containing the greetings
message HelloReply {
  string message = 1;
}
```

Running the appropriate command for your OS regenerates the following files in the directory:

* Greeter/Helloworld.cs contains all the protocol buffer code to populate, serialize, and retrieve our request and response message types
* Greeter/HelloworldGrpc.cs provides generated client and server classes, including:
    - an abstract class Greeter.GreeterBase to inherit from when defining Greeter service implementations
    - a class Greeter.GreeterClient that can be used to access remote Greeter instances

### Server side
We need to create a server side class:
1. Build a server and add port.
2. Add or bind service to this server.
3. `server.Start()`
The important thing is the implementation of the service. Usually inside the implementation of the service, we implement the methods (e.g. `SayHello`), which take reques, build a response and send the response back by using response observer.

For example:
```
    class GreeterImpl : Greeter.GreeterBase
    {
        // Server side handler of the SayHello RPC
        public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            return Task.FromResult(new HelloReply { Message = "Hello " + request.Name });
        }
    }

    class Program
    {
        const int Port = 50051;

        public static void Main(string[] args)
        {
            Server server = new Server
            {
                Services = { Greeter.BindService(new GreeterImpl()) },
                Ports = { new ServerPort("localhost", Port, ServerCredentials.Insecure) }
            };
            server.Start();

            Console.WriteLine("Greeter server listening on port " + Port);
            Console.WriteLine("Press any key to stop the server...");
            Console.ReadKey();

            server.ShutdownAsync().Wait();
        }
    }
``` 
### Client side
We need to create a client side class:
1. Create a channel, which is used to talk to the server.
2. Create a `stub` (e.g. `var client` in following example) using this channel.
3. Create a request.
4. `stub` calls the method, which has the same signature of .proto file.

For example
```
    class Program
    {
        public static void Main(string[] args)
        {
            Channel channel = new Channel("127.0.0.1:50051", ChannelCredentials.Insecure);

            var client = new Greeter.GreeterClient(channel);
            String user = "you";

            var reply = client.SayHello(new HelloRequest { Name = user });
            Console.WriteLine("Greeting: " + reply.Message);

            channel.ShutdownAsync().Wait();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
```

## Azure WebJobs SDK basics
" The Azure WebJobs SDK is a framework that simplifies the task of writing background processing code that runs in Azure. The Azure WebJobs SDK includes a declarative binding and trigger system that works with Azure Storage Blobs, Queues and Tables as well as Service Bus. The binding system makes it incredibly easy to write code that reads or writes Azure Storage objects. The trigger system automatically invokes a function in your code whenever any new data is received in a queue or blob." [3]

 Here is an `Azure WebJobs SDK` example copied form [github](https://github.com/Azure/azure-webjobs-sdk/wiki/Introduction), which polls a queue and creates a blob for each queue message received:
 ```
     public static void Main()
    {
        JobHost host = new JobHost();
        host.RunAndBlock();
    }

    public static void ProcessQueueMessage([QueueTrigger("webjobsqueue")] string inputText, 
        [Blob("containername/blobname")]TextWriter writer)
    {
        writer.WriteLine(inputText);
    }
 ```

"The `JobHost` object is a container for a set of background functions. The `JobHost` object monitors the functions, watches for events that trigger them, and executes the functions when trigger events occur. You call a `JobHost` method to indicate whether you want the container process to run on the current thread or a background thread. In the example, the `RunAndBlock` method runs the process continuously on the current thread.

Because the `ProcessQueueMessage` method in this example has a `QueueTrigger` attribute, the trigger for that function is the reception of a new queue message. The `JobHost` object watches for new queue messages on the specified queue ("webjobsqueue" in this sample) and when one is found, it calls `ProcessQueueMessage`.

The `QueueTrigger` attribute binds the inputText parameter to the value of the queue message. And the `Blob` attribute binds a `TextWriter` object to a blob named "blobname" in a container named "containername"."[4]

```
        /// <summary>Runs the host and blocks the current thread while the host remains running.</summary>
        public void RunAndBlock()
        {
            Start();

            // Wait for someone to begin stopping (_shutdownWatcher, Stop, or Dispose).
            _stoppingTokenSource.Token.WaitHandle.WaitOne();

            // Don't return until all executing functions have completed.
            Stop();
        }

        /// <summary>Starts the host.</summary>
        public void Start()
        {
            Task.Run(() => StartAsync()).GetAwaiter().GetResult();
        }

        /// <summary>Starts the host.</summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="Task"/> that will start the host.</returns>
        public Task StartAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();

            if (Interlocked.CompareExchange(ref _state, StateStarting, StateNotStarted) != StateNotStarted)
            {
                throw new InvalidOperationException("Start has already been called.");
            }

            return StartAsyncCore(cancellationToken);
        }

        private async Task StartAsyncCore(CancellationToken cancellationToken)
        {
            await EnsureHostInitializedAsync(cancellationToken);

            await _listener.StartAsync(cancellationToken);

            OnHostStarted();

            string msg = "Job host started";
            _logger?.LogInformation(msg);

            _state = StateStarted;
        }
```

As we can see, `_listener` calls `StartAsync()`, which starts listening and returns a task that completes when the listener is fully started. WebJobs defines multiple listens such as `BlobListener`,`CompositeListener`,`FunctionListener`,`HeartbeatListener`,`ListenerFactoryListener`,`NullListener`,`ShutdownListener`,`TimerListener`,`QueueListener`,`SingletonListener`, `EventHubListener` and `ServiceBusListener`.

So what do these listeners do? And how are they created and invoked?

```
    internal class FunctionDefinition : IFunctionDefinition
    {
        private readonly FunctionDescriptor _descriptor;
        private readonly IFunctionInstanceFactory _instanceFactory;
        private readonly IListenerFactory _listenerFactory;
        ....
    }
```
`FunctionDefinition` has three fields: `FunctionDescriptor _descriptor`, `IFunctionInstanceFactory _instanceFactory` and `IListenerFactory _listenerFactory` as shown following.

```
    public class FunctionDescriptor
    {
        /// <summary>Gets or sets the ID of the function.</summary>
        public string Id { get; set; }

        /// <summary>Gets or sets the fully qualified name of the function. This is 'Namespace.Class.Method' </summary>
        public string FullName { get; set; }

        /// <summary>Gets or sets the display name of the function. This is commonly 'Class.Method' </summary>
        public string ShortName { get; set; }

        /// <summary>Gets or sets the function's parameters.</summary>
        public IEnumerable<ParameterDescriptor> Parameters { get; set; }

        /// <summary>Gets or sets the name used for logging. This is 'Method' or the value overwritten by [FunctionName] </summary>
        [JsonIgnore]
        internal string LogName { get; set; }

        /// <summary>Gets or sets whether this method is disabled. </summary>
        [JsonIgnore]
        internal bool IsDisabled { get; set; }
        ....
    }

    internal class FunctionInstanceFactory : IFunctionInstanceFactory
    {
        private readonly IFunctionBinding _binding;
        private readonly IFunctionInvoker _invoker;
        private readonly FunctionDescriptor _descriptor;
        ....
        public IFunctionInstance Create(FunctionInstanceFactoryContext context)
        {
            IBindingSource bindingSource = new BindingSource(_binding, context.Parameters);
            return new FunctionInstance(context.Id, context.ParentId, context.ExecutionReason, bindingSource, _invoker, _descriptor);
        }
    }

    internal class QueueListenerFactory : IListenerFactory
    {
        private static string poisonQueueSuffix = "-poison";

        private readonly IStorageQueue _queue;
        private readonly IStorageQueue _poisonQueue;
        private readonly IQueueConfiguration _queueConfiguration;
        private readonly IWebJobsExceptionHandler _exceptionHandler;
        private readonly IContextSetter<IMessageEnqueuedWatcher> _messageEnqueuedWatcherSetter;
        private readonly ISharedContextProvider _sharedContextProvider;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ITriggeredFunctionExecutor _executor;
        ....

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public Task<IListener> CreateAsync(CancellationToken cancellationToken)
        {
            QueueTriggerExecutor triggerExecutor = new QueueTriggerExecutor(_executor);

            SharedQueueWatcher sharedWatcher = _sharedContextProvider.GetOrCreateInstance<SharedQueueWatcher>(
                new SharedQueueWatcherFactory(_messageEnqueuedWatcherSetter));

            IListener listener = new QueueListener(_queue, _poisonQueue, triggerExecutor, _exceptionHandler, _loggerFactory,
                sharedWatcher, _queueConfiguration);

            return Task.FromResult(listener);
        }
    }
```

`FunctionInstanceFactory` has three fields: `IFunctionBinding _binding`, `IFunctionInvoker _invoker` and `FunctionDescriptor _descriptor` as shown following:

```
    internal class FunctionBinding : IFunctionBinding
    {
        private readonly FunctionDescriptor _descriptor;
        private readonly IReadOnlyDictionary<string, IBinding> _bindings;
        private readonly SingletonManager _singletonManager;
        ....

        // Create a bindingContext. 
        // parameters takes precedence over existingBindingData.
        internal static BindingContext NewBindingContext(
            ValueBindingContext context, 
            IReadOnlyDictionary<string, object> existingBindingData,  
            IDictionary<string, object> parameters)
        {
            ....
        }

        public async Task<IReadOnlyDictionary<string, IValueProvider>> BindAsync(ValueBindingContext context, IDictionary<string, object> parameters)
        {
            ....
        }
    }

    internal class FunctionInvoker<TReflected, TReturnValue> : IFunctionInvoker
    {
        private readonly IReadOnlyList<string> _parameterNames;
        private readonly IFactory<TReflected> _instanceFactory;
        private readonly IMethodInvoker<TReflected, TReturnValue> _methodInvoker;
        ....

        public object CreateInstance()
        {
            TReflected instance = _instanceFactory.Create();
            return instance;
        }

        public async Task<object> InvokeAsync(object instance, object[] arguments)
        {
            // Return a task immediately in case the method is not async.
            await Task.Yield();

            return await _methodInvoker.InvokeAsync((TReflected) instance, arguments);            
        }
    }
```

`FunctionInvoker` has one field named `IMethodInvoker<TReflected, TReturnValue> _methodInvoker`, which has the following defination:
```
    internal class TaskMethodInvoker<TReflected, TReturnType> : IMethodInvoker<TReflected, TReturnType>
    {
        private readonly Func<TReflected, object[], Task<TReturnType>> _lambda;

        public TaskMethodInvoker(Func<TReflected, object[], Task<TReturnType>> lambda)
        {
            _lambda = lambda;
        }

        public Task<TReturnType> InvokeAsync(TReflected instance, object[] arguments)
        {
            Task<TReturnType> task = _lambda.Invoke(instance, arguments);
            ThrowIfWrappedTaskInstance(task);
            return task;
        }
        ....
    }
```

`QueueListenerFactory` has one interesting field `ITriggeredFunctionExecutor _executor` and one method `CreateAsync()` to create a `Task` as shown following:
```

        public Task<IListener> CreateAsync(CancellationToken cancellationToken)
        {
            QueueTriggerExecutor triggerExecutor = new QueueTriggerExecutor(_executor);

            SharedQueueWatcher sharedWatcher = _sharedContextProvider.GetOrCreateInstance<SharedQueueWatcher>(
                new SharedQueueWatcherFactory(_messageEnqueuedWatcherSetter));

            IListener listener = new QueueListener(_queue, _poisonQueue, triggerExecutor, _exceptionHandler, _loggerFactory,
                sharedWatcher, _queueConfiguration);

            return Task.FromResult(listener);
        }

    internal class QueueTriggerExecutor : ITriggerExecutor<IStorageQueueMessage>
    {
        private readonly ITriggeredFunctionExecutor _innerExecutor;

        public async Task<FunctionResult> ExecuteAsync(IStorageQueueMessage value, CancellationToken cancellationToken)
        {
            Guid? parentId = QueueCausalityManager.GetOwner(value);
            TriggeredFunctionData input = new TriggeredFunctionData
            {
                ParentId = parentId,
                TriggerValue = value
            };
            return await _innerExecutor.TryExecuteAsync(input, cancellationToken);
        }
    }       
```
## gRPC with Azure WebJobs SDK Script
### .proto file
[FunctionRpc.proto](https://github.com/Azure/azure-webjobs-sdk-script/blob/dev/src/WebJobs.Script.Grpc/Proto/FunctionRpc.proto) defines a service named `FunctionRpc`, inside which a method `EventStream` is declared. The method `EventStream` takes a stream request with type `StreamingMessage` and returns a stream response with type `StreamingMessage`.

```
syntax = "proto3";
....

package FunctionRpc;

import "google/protobuf/duration.proto";

// Interface exported by the server.
service FunctionRpc {
 rpc EventStream (stream StreamingMessage) returns (stream StreamingMessage) {}
}

message StreamingMessage {
  string request_id = 1;
  oneof content {
    StartStream start_stream = 20; 

    // Host sends capabilities/init data to worker
    WorkerInitRequest worker_init_request = 17;
    // Worker responds after initializing with its capabilities & status
    WorkerInitResponse worker_init_response = 16;

    // Worker periodically sends empty heartbeat message to host
    WorkerHeartbeat worker_heartbeat = 15;

    // Host sends terminate message to worker.
    // Worker terminates if it can, otherwise host terminates after a grace period
    WorkerTerminate worker_terminate = 14;

    // Add any worker relevant status to response
    WorkerStatusRequest worker_status_request = 12;
    WorkerStatusResponse worker_status_response = 13;

    // On file change event, host sends notification to worker
    FileChangeEventRequest file_change_event_request = 6;

    // Worker requests a desired action (restart worker, reload function)
    WorkerActionResponse worker_action_response = 7;
    
    // Host sends required metadata to worker to load function
    FunctionLoadRequest function_load_request = 8;
    // Worker responds after loading with the load result
    FunctionLoadResponse function_load_response = 9;
    
    InvocationRequest invocation_request = 4;
    InvocationResponse invocation_response = 5;
    // Host sends cancel message to attempt to cancel an invocation. 
    // If an invocation is cancelled, host will receive an invocation response with status cancelled.
    InvocationCancel invocation_cancel = 21;

    RpcLog rpc_log = 2;
  }
}

message StartStream {
  string worker_id = 2;
}

message WorkerInitRequest {
....
}

message WorkerInitResponse {
....
}

message StatusResult {
....
}

message WorkerHeartbeat {}

message WorkerTerminate {
  google.protobuf.Duration grace_period = 1;
}

message FileChangeEventRequest {
....
}

message WorkerActionResponse {
....
}

message WorkerStatusRequest{
....
}

message WorkerStatusResponse {
....
}

message FunctionLoadRequest {
  // unique function identifier (avoid name collisions, facilitate reload case)
  string function_id = 1;
  RpcFunctionMetadata metadata = 2;
}

message FunctionLoadResponse {
  string function_id = 1;
  StatusResult result = 2;
  // TODO: return type expected?
}

message RpcFunctionMetadata {
  // TODO: do we want the host's name - the language worker might do a better job of assignment than the host
  string name = 4;

  string directory = 1;
  string script_file = 2;
  string entry_point = 3;

  map<string, BindingInfo> bindings = 6;

  // not adding disabled or excluded as those (currently) are only relevant to host
}

message InvocationRequest {
  string invocation_id = 1;
  string function_id = 2;
  repeated ParameterBinding input_data = 3;
  map<string, TypedData> trigger_metadata = 4;
}

message InvocationCancel {
....
}

message InvocationResponse {
  string invocation_id = 1;
  repeated ParameterBinding output_data = 2;
  TypedData return_value = 4;
  StatusResult result = 3;
}

message TypedData {
....
}

message ParameterBinding {
....
}

message BindingInfo {
....
}

message RpcLog {
....
}

message RpcException {
....
}

message RpcHttp {
....
}
```

From above .proto file, we expect gRPC could generate client and server classes, including:
- an abstract class FunctionRpc.FunctionRpcBase
- a class FunctionRpc.FunctionRpcClient


### Server side

[GrpcServer.cs](https://github.com/Azure/azure-webjobs-sdk-script/blob/dev/src/WebJobs.Script.Grpc/Server/GrpcServer.cs):

```
    public class GrpcServer : IRpcServer, IDisposable
    {
        private Server _server;
        private bool _disposed = false;

        public GrpcServer(FunctionRpc.FunctionRpcBase serviceImpl)
        {
            _server = new Server
            {
                Services = { FunctionRpc.BindService(serviceImpl) },
                Ports = { new ServerPort("127.0.0.1", ServerPort.PickUnused, ServerCredentials.Insecure) }
            };
        }

        public Uri Uri => new Uri($"http://127.0.0.1:{_server.Ports.First().BoundPort}");

        public Task StartAsync()
        {
            _server.Start();
            return Task.CompletedTask;
        }

        public Task ShutdownAsync() => _server.ShutdownAsync();
        ....
    }
```

The service implementation `serviceImpl` is in file
[FunctionRpcService.cs](https://github.com/Azure/azure-webjobs-sdk-script/blob/dev/src/WebJobs.Script/Rpc/FunctionRpcService.cs). The method `EventStream` is also defined here:

```
    internal class FunctionRpcService : FunctionRpc.FunctionRpcBase
    {
        private readonly IScriptEventManager _eventManager;

        public FunctionRpcService(IScriptEventManager eventManager)
        {
            _eventManager = eventManager;
        }

        public override async Task EventStream(IAsyncStreamReader<StreamingMessage> requestStream, IServerStreamWriter<StreamingMessage> responseStream, ServerCallContext context)
        {
            var cancelSource = new TaskCompletionSource<bool>();
            IDisposable outboundEventSubscription = null;
            try
            {
                context.CancellationToken.Register(() => cancelSource.TrySetResult(false));

                Func<Task<bool>> messageAvailable = async () =>
                {
                    // GRPC does not accept cancellation tokens for individual reads, hence wrapper
                    var requestTask = requestStream.MoveNext(CancellationToken.None);
                    var completed = await Task.WhenAny(cancelSource.Task, requestTask);
                    return completed.Result;
                };

                if (await messageAvailable())
                {
                    string workerId = requestStream.Current.StartStream.WorkerId;
                    outboundEventSubscription = _eventManager.OfType<OutboundEvent>()
                        .Where(evt => evt.WorkerId == workerId)
                        .ObserveOn(NewThreadScheduler.Default)
                        .Subscribe(evt =>
                        {
                            // WriteAsync only allows one pending write at a time
                            // For each responseStream subscription, observe as a blocking write, in series, on a new thread
                            // Alternatives - could wrap responseStream.WriteAsync with a SemaphoreSlim to control concurrent access
                            responseStream.WriteAsync(evt.Message).GetAwaiter().GetResult();
                        });

                    do
                    {
                        _eventManager.Publish(new InboundEvent(workerId, requestStream.Current));
                    }
                    while (await messageAvailable());
                }
            }
            catch (Exception exc)
            {
                // TODO: do this properly so it can be associated with workerid, if possible
                _eventManager.Publish(new WorkerErrorEvent(null, exc));
            }
            finally
            {
                outboundEventSubscription?.Dispose();

                // ensure cancellationSource task completes
                cancelSource.TrySetResult(false);
            }
        }
    }
```

When is the class `GrpcServer` object is created and initialized? The method [Initialize()](https://github.com/Azure/azure-webjobs-sdk-script/blob/dev/src/WebJobs.Script/Host/ScriptHost.cs#L277) inside file [ScriptHost.cs](https://github.com/Azure/azure-webjobs-sdk-script/blob/dev/src/WebJobs.Script/Host/ScriptHost.cs) instance class `GrpcServer` like this
```
                var serverImpl = new FunctionRpcService(EventManager);
                var server = new GrpcServer(serverImpl);

                // TODO: async initialization of script host - hook into startasync method?
                server.StartAsync().GetAwaiter().GetResult();
```

// TODO

## How does Azure WebJobs SDK Script start?
 `Azure WebJobs SDK Script` is built on `Azure WebJobs SDK`.

Then let's look at `Azure WebJobs SDK Script`.

[src/WebJobs.Script.Host/Program.cs](https://github.com/Azure/azure-webjobs-sdk-script/blob/dev/src/WebJobs.Script.Host/Program.cs) has `Main()` function as shown here. `ScriptHostConfiguration` is defined in file [src/WebJobs.Script/Config/ScriptHostConfiguration.cs](https://github.com/Azure/azure-webjobs-sdk-script/blob/dev/src/WebJobs.Script/Config/ScriptHostConfiguration.cs).

```
    public static class Program
    {
        public static void Main(string[] args)
        {
            ....
            string rootPath = Environment.CurrentDirectory;
            ....
            var config = new ScriptHostConfiguration()
            {
                RootScriptPath = rootPath,
                IsSelfHost = true
            };

            var scriptHostManager = new ScriptHostManager(config);
            scriptHostManager.RunAndBlock();
        }
    }
```
`ScriptHostManager` is defined in file [src/WebJobs.Script/Host/ScriptHostManager.cs](https://github.com/Azure/azure-webjobs-sdk-script/blob/dev/src/WebJobs.Script/Host/ScriptHostManager.cs). And `RunAndBlock()` is defined as following. `_scriptHostFactory`, whose type is `ScriptHostFactory`, calls method `Create()` to create a new instance of `ScriptHost`.

```
        public void RunAndBlock(CancellationToken cancellationToken = default(CancellationToken))
        {
            _consecutiveErrorCount = 0;
            do
            {
                ScriptHost newInstance = null;

                try
                {
                    ....
                    newInstance = _scriptHostFactory.Create(_environment, EventManager, _settingsManager, _config, _loggerProviderFactory);

                    _currentInstance = newInstance;
                    lock (_liveInstances)
                    {
                        _liveInstances.Add(newInstance);
                        _hostStartCount++;
                    }

                    newInstance.HostInitializing += OnHostInitializing;
                    newInstance.HostInitialized += OnHostInitialized;
                    newInstance.HostStarted += OnHostStarted;
                    newInstance.Initialize();

                    newInstance.StartAsync(cancellationToken).GetAwaiter().GetResult();
                    ....
                }
                catch (Exception ex)
                {
                    ....
                }
            }
            while (!_stopped && !cancellationToken.IsCancellationRequested);
        }
```

`ScriptHost` is defined in file [src/WebJobs.Script/Host/ScriptHost.cs](https://github.com/Azure/azure-webjobs-sdk-script/blob/dev/src/WebJobs.Script/Host/ScriptHost.cs). The new instance of `ScriptHost` adds a few event handlers and calls `Initialize()`:
```
        /// <summary>
        /// Performs all required initialization on the host. Must be called before the host is started.
        /// </summary>
        public void Initialize()
        {
            ....
            using (metricsLogger.LatencyEvent(MetricEventNames.HostStartupLatency))
            {
                // read host.json and apply to JobHostConfiguration
                string hostConfigFilePath = Path.Combine(ScriptConfig.RootScriptPath, ScriptConstants.HostMetadataFileName);
                ....

                Func<string, FunctionDescriptor> funcLookup = (name) => this.GetFunctionOrNull(name);
                _hostConfig.AddService(funcLookup);

                // Before configuration has been fully read, configure a default logger factory
                // to ensure we can log any configuration errors. There's no filters at this point,
                // but that's okay since we can't build filters until we apply configuration below.
                // We'll recreate the loggers after config is read. We initialize the public logger
                // to the startup logger until we've read configuration settings and can create the real logger.
                // The "startup" logger is used in this class for startup related logs. The public logger is used
                // for all other logging after startup.
                ConfigureLoggerFactory();
                Logger = _startupLogger = _hostConfig.LoggerFactory.CreateLogger(LogCategories.Startup);
                string readingFileMessage = string.Format(CultureInfo.InvariantCulture, "Reading host configuration file '{0}'", hostConfigFilePath);

                string json = File.ReadAllText(hostConfigFilePath);
                JObject hostConfigObject;
                try
                {
                    hostConfigObject = JObject.Parse(json);
                }
                catch (JsonException ex)
                {
                    // If there's a parsing error, write out the previous messages without filters to ensure
                    // they're logged
                    _startupLogger.LogInformation(readingFileMessage);
                    throw new FormatException(string.Format("Unable to parse {0} file.", ScriptConstants.HostMetadataFileName), ex);
                }

                string sanitizedJson = SanitizeHostJson(hostConfigObject);
                string readFileMessage = $"Host configuration file read:{Environment.NewLine}{sanitizedJson}";

                ApplyConfiguration(hostConfigObject, ScriptConfig);

                // now the configuration has been read and applied re-create the logger
                // factory and loggers ensuring that filters and settings have been applied
                ConfigureLoggerFactory(recreate: true);
                _startupLogger = _hostConfig.LoggerFactory.CreateLogger(LogCategories.Startup);
                Logger = _hostConfig.LoggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral);

                // Allow tests to modify anything initialized by host.json
                ScriptConfig.OnConfigurationApplied?.Invoke(ScriptConfig);

                // only after configuration has been applied and loggers have been created, raise the initializing event
                HostInitializing?.Invoke(this, EventArgs.Empty);

                // Do not log these until after all the configuration is done so the proper filters are applied.
                _startupLogger.LogInformation(readingFileMessage);
                _startupLogger.LogInformation(readFileMessage);

                // If they set the host id in the JSON, emit a warning that this could cause issues and they shouldn't do it.
                if (ScriptConfig.HostConfig?.HostConfigMetadata?["id"] != null)
                {
                    _startupLogger.LogWarning("Host id explicitly set in the host.json. It is recommended that you remove the \"id\" property in your host.json.");
                }

                if (string.IsNullOrEmpty(_hostConfig.HostId))
                {
                    _hostConfig.HostId = Utility.GetDefaultHostId(_settingsManager, ScriptConfig);
                }
                if (string.IsNullOrEmpty(_hostConfig.HostId))
                {
                    throw new InvalidOperationException("An 'id' must be specified in the host configuration.");
                }

                _debugModeFileWatcher = new AutoRecoveringFileSystemWatcher(hostLogPath, ScriptConstants.DebugSentinelFileName,
                    includeSubdirectories: false, changeTypes: WatcherChangeTypes.Created | WatcherChangeTypes.Changed);

                _debugModeFileWatcher.Changed += OnDebugModeFileChanged;

                var storageString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Storage);
                if (storageString == null)
                {
                    // Disable core storage
                    _hostConfig.StorageConnectionString = null;
                }

                var serverImpl = new FunctionRpcService(EventManager);
                var server = new GrpcServer(serverImpl);

                // TODO: async initialization of script host - hook into startasync method?
                server.StartAsync().GetAwaiter().GetResult();
                var processFactory = new DefaultWorkerProcessFactory();

                try
                {
                    _processRegistry = ProcessRegistryFactory.Create();
                }
                catch (Exception e)
                {
                    _startupLogger.LogWarning(e, "Unable to create process registry");
                }

                CreateChannel channelFactory = (config, registrations) =>
                {
                    return new LanguageWorkerChannel(
                        ScriptConfig,
                        EventManager,
                        processFactory,
                        _processRegistry,
                        registrations,
                        config,
                        server.Uri,
                        _hostConfig.LoggerFactory);
                };

                var configFactory = new WorkerConfigFactory(ScriptSettingsManager.Instance.Configuration, _startupLogger);
                var workerConfigs = configFactory.GetConfigs(new List<IWorkerProvider>()
                {
                    new NodeWorkerProvider(),
                    new JavaWorkerProvider()
                });

                _functionDispatcher = new FunctionRegistry(EventManager, server, channelFactory, workerConfigs);

                _eventSubscriptions.Add(EventManager.OfType<WorkerErrorEvent>()
                    .Subscribe(evt =>
                    {
                        HandleHostError(evt.Exception);
                    }));

                _eventSubscriptions.Add(EventManager.OfType<HostRestartEvent>()
                    .Subscribe((msg) => ScheduleRestartAsync(false)
                    .ContinueWith(t => _startupLogger.LogCritical(t.Exception.Message),
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously)));

                if (ScriptConfig.FileWatchingEnabled)
                {
                    _fileEventSource = new FileWatcherEventSource(EventManager, EventSources.ScriptFiles, ScriptConfig.RootScriptPath);

                    _eventSubscriptions.Add(EventManager.OfType<FileEvent>()
                        .Where(f => string.Equals(f.Source, EventSources.ScriptFiles, StringComparison.Ordinal))
                        .Subscribe(e => OnFileChanged(e.FileChangeArguments)));
                }

                // If a file change should result in a restart, we debounce the event to
                // ensure that only a single restart is triggered within a specific time window.
                // This allows us to deal with a large set of file change events that might
                // result from a bulk copy/unzip operation. In such cases, we only want to
                // restart after ALL the operations are complete and there is a quiet period.
                _restart = RestartAsync;
                _restart = _restart.Debounce(500);

                _shutdown = Shutdown;
                _shutdown = _shutdown.Debounce(500);

                // take a snapshot so we can detect function additions/removals
                _directorySnapshot = Directory.EnumerateDirectories(ScriptConfig.RootScriptPath).ToImmutableArray();

                // Scan the function.json early to determine the requirements.
                var functionMetadata = ReadFunctionMetadata(ScriptConfig, _startupLogger, FunctionErrors, _settingsManager);
                var usedBindingTypes = DiscoverBindingTypes(functionMetadata);

                var bindingProviders = LoadBindingProviders(ScriptConfig, hostConfigObject, _startupLogger, usedBindingTypes);
                ScriptConfig.BindingProviders = bindingProviders;

                var coreBinder = bindingProviders.OfType<CoreExtensionsScriptBindingProvider>().First();
                coreBinder.AppDirectory = ScriptConfig.RootScriptPath;

                // Allow BindingProviders to initialize
                foreach (var bindingProvider in ScriptConfig.BindingProviders)
                {
                    try
                    {
                        bindingProvider.Initialize();
                    }
                    catch (Exception ex)
                    {
                        // If we're unable to initialize a binding provider for any reason, log the error
                        // and continue
                        string errorMsg = string.Format("Error initializing binding provider '{0}'", bindingProvider.GetType().FullName);
                        _startupLogger.LogError(0, ex, errorMsg);
                    }
                }

                var directTypes = GetDirectTypes(functionMetadata);

                LoadDirectlyReferencesExtensions(directTypes);

                LoadCustomExtensions();

                // Do this after we've loaded the custom extensions. That gives an extension an opportunity to plug in their own implementations.
                if (storageString != null)
                {
                    var lockManager = (IDistributedLockManager)Services.GetService(typeof(IDistributedLockManager));
                    _blobLeaseManager = PrimaryHostCoordinator.Create(lockManager, TimeSpan.FromSeconds(15), _hostConfig.HostId, InstanceId, _hostConfig.LoggerFactory);
                }

                // Create the lease manager that will keep handle the primary host blob lease acquisition and renewal
                // and subscribe for change notifications.
                if (_blobLeaseManager != null)
                {
                    _blobLeaseManager.HasLeaseChanged += BlobLeaseManagerHasLeaseChanged;
                }

                _descriptorProviders = new List<FunctionDescriptorProvider>()
                {
                    new DotNetFunctionDescriptorProvider(this, ScriptConfig),
                    new WorkerFunctionDescriptorProvider(this, ScriptConfig, _functionDispatcher),
                };

                // read all script functions and apply to JobHostConfiguration
                Collection<FunctionDescriptor> functions = GetFunctionDescriptors(functionMetadata);
                Collection<CustomAttributeBuilder> typeAttributes = new Collection<CustomAttributeBuilder>();
                string typeName = string.Format(CultureInfo.InvariantCulture, "{0}.{1}", GeneratedTypeNamespace, GeneratedTypeName);

                string generatingMsg = string.Format(CultureInfo.InvariantCulture, "Generating {0} job function(s)", functions.Count);
                _startupLogger.LogInformation(generatingMsg);

                Type type = FunctionGenerator.Generate(HostAssemblyName, typeName, typeAttributes, functions);
                List<Type> types = new List<Type>();
                types.Add(type);

                types.AddRange(directTypes);

                _hostConfig.TypeLocator = new TypeLocator(types);

                Functions = functions;

                if (ScriptConfig.FileLoggingMode != FileLoggingMode.Never)
                {
                    PurgeOldLogDirectories();
                }
            }
        }
```

The next thing is `newInstance.StartAsync(cancellationToken).GetAwaiter().GetResult();`. The method `StartAsync()` is inherited from class `JobHost` in . 

```
        /// <summary>Starts the host.</summary>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>A <see cref="Task"/> that will start the host.</returns>
        public Task StartAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();

            if (Interlocked.CompareExchange(ref _state, StateStarting, StateNotStarted) != StateNotStarted)
            {
                throw new InvalidOperationException("Start has already been called.");
            }

            return StartAsyncCore(cancellationToken);
        }

        private async Task StartAsyncCore(CancellationToken cancellationToken)
        {
            await EnsureHostInitializedAsync(cancellationToken);

            await _listener.StartAsync(cancellationToken);

            OnHostStarted();

            string msg = "Job host started";
            _logger?.LogInformation(msg);

            _state = StateStarted;
        }
```

`_listener` could be BlobListener, TimerListener, QueueListener and other listeners.
## References
1. https://en.wikipedia.org/wiki/GRPC
2. https://github.com/grpc/grpc/tree/master/examples/csharp/helloworld
3. https://github.com/Azure/azure-webjobs-sdk
4. https://github.com/Azure/azure-webjobs-sdk/wiki/Introduction