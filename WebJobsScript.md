# How does Azure WebJobs SDK Script work?

## Azure WebJobs Script Source Code Analysis
```
    public static class Program
    {
        public static void Main(string[] args)
        {
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
This code sample:
* 1. Create `ScriptHostManager`
* 2. `ScriptHostManager` calls `RunAndBlock()`


// This is how to create `ScriptHostManager` instance
### Class `ScriptHostConfiguration` // used by ScriptHostManager
```
    public class ScriptHostConfiguration
    {
        public ScriptHostConfiguration()
        {
            HostConfig = new JobHostConfiguration();
            FileWatchingEnabled = true;
            FileLoggingMode = FileLoggingMode.Never;
            RootScriptPath = Environment.CurrentDirectory;
            RootLogPath = Path.Combine(Path.GetTempPath(), "Functions");
            LogFilter = new LogCategoryFilter();
            HostHealthMonitor = new HostHealthMonitorConfiguration();
        }

        /// <summary>
        /// Gets or sets the <see cref="JobHostConfiguration"/>.
        /// </summary>
        public JobHostConfiguration HostConfig { get; set; }

        /// <summary>
        /// Gets or sets the path to the script function directory.
        /// </summary>
        public string RootScriptPath { get; set; }

        /// <summary>
        /// Gets or sets the root path for log files.
        /// </summary>
        public string RootLogPath { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the <see cref="ScriptHost"/> should
        /// monitor file for changes (default is true). When set to true, the host will
        /// automatically react to source/config file changes. When set to false no file
        /// monitoring will be performed.
        /// </summary>
        public bool FileWatchingEnabled { get; set; }

        /// <summary>
        /// Gets or sets the collection of directories (relative to RootScriptPath) that
        /// should be monitored for changes. If FileWatchingEnabled is true, these directories
        /// will be monitored. When a file is added/modified/deleted in any of these
        /// directories, the host will restart.
        /// </summary>
        public ICollection<string> WatchDirectories { get; set; }

        /// <summary>
        /// Gets or sets a value governing when logs should be written to disk.
        /// When enabled, logs will be written to the directory specified by
        /// <see cref="RootLogPath"/>.
        /// </summary>
        public FileLoggingMode FileLoggingMode { get; set; }

        /// <summary>
        /// Gets or sets the list of functions that should be run. This list can be used to filter
        /// the set of functions that will be enabled - it can be a subset of the actual
        /// function directories. When left null (the default) all discovered functions will
        /// be run.
        /// </summary>
        public ICollection<string> Functions { get; set; }

        /// <summary>
        /// Gets or sets a value indicating the timeout duration for all functions. If null,
        /// there is no timeout duration.
        /// </summary>
        public TimeSpan? FunctionTimeout { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the host is running
        /// outside of the normal Azure hosting environment. E.g. when running
        /// locally or via CLI.
        /// </summary>
        public bool IsSelfHost { get; set; }

        /// <summary>
        /// Gets or sets the <see cref="LogCategoryFilter"/> to use when constructing providers for the
        /// registered <see cref="ILoggerFactory"/>.
        /// </summary>
        public LogCategoryFilter LogFilter { get; set; }

        /// <summary>
        /// Gets or sets the set of <see cref="ScriptBindingProviders"/> to use when loading functions.
        /// </summary>
        internal ICollection<ScriptBindingProvider> BindingProviders { get; set; }

        /// <summary>
        /// Gets or sets a test hook for modifying the configuration after host.json has been processed.
        /// </summary>
        internal Action<ScriptHostConfiguration> OnConfigurationApplied { get; set; }

        /// <summary>
        /// Gets the <see cref="HostHealthMonitorConfiguration"/> to use.
        /// </summary>
        public HostHealthMonitorConfiguration HostHealthMonitor { get; }
    }
```

### Class `DefaultWorkerProcessFactory` // CreateWorkerProcess can create a working process
```
    internal class DefaultWorkerProcessFactory : IWorkerProcessFactory
    {
        public virtual Process CreateWorkerProcess(WorkerCreateContext context)
        {
            var startInfo = new ProcessStartInfo(context.Arguments.ExecutablePath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false,
                ErrorDialog = false,
                WorkingDirectory = context.WorkingDirectory,
                Arguments = GetArguments(context),
            };

            return new Process { StartInfo = startInfo };
        }
    }
```
### Class `JobObjectRegistry` // Registers processes on windows with a job object to ensure disposal after parent exit
```
    // Registers processes on windows with a job object to ensure disposal after parent exit
    internal class JobObjectRegistry : IDisposable, IProcessRegistry
    {
        private IntPtr _handle;
        private bool _disposed = false;

        public JobObjectRegistry()
        {
            _handle = CreateJobObject(IntPtr.Zero, null);

            var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = 0x2000
            };

            var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = info
            };

            int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
            Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);

            if (!SetInformationJobObject(_handle, JobObjectInfoType.ExtendedLimitInformation, extendedInfoPtr, (uint)length))
            {
                throw new Exception(string.Format("Unable to set information.  Error: {0}", Marshal.GetLastWin32Error()));
            }
        }

        public bool Register(Process proc)
        {
            return AssignProcessToJobObject(_handle, proc.Handle);
        }
    }
```
### Class `ProcessRegistryFactory` // create instance of JobObjectRegistry()
```
    internal class ProcessRegistryFactory
    {
        internal static IProcessRegistry Create()
        {
            // W3WP already manages job objects
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && !ScriptSettingsManager.Instance.IsAzureEnvironment)
            {
                return new JobObjectRegistry();
            }
            else
            {
                return new EmptyProcessRegistry();
            }
        }
    }
```
### Class `FunctionMetadata`
```
    public class FunctionMetadata
    {
        public FunctionMetadata()
        {
            Bindings = new Collection<BindingMetadata>();
        }

        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the primary entry point for the function (to disambiguate if there are multiple
        /// scripts in the function directory).
        /// </summary>
        public string ScriptFile { get; set; }

        /// <summary>
        /// Gets or sets the function root directory.
        /// </summary>
        public string FunctionDirectory { get; set; }

        /// <summary>
        /// Gets or sets the optional named entry point for a function.
        /// </summary>
        public string EntryPoint { get; set; }

        public ScriptType ScriptType { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the function is disabled.
        /// <remarks>
        /// A disabled function is still compiled and loaded into the host, but it will not
        /// be triggered automatically, and is not publicly addressable (except via admin invoke requests).
        /// </remarks>
        /// </summary>
        public bool IsDisabled { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether that this function is a direct invoke.
        /// </summary>
        public bool IsDirect { get; set; }

        public string FunctionId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Gets or sets a value indicating whether this function is a wrapper for Azure Function Proxy
        /// </summary>
        public bool IsProxy { get; set; }

        public Collection<BindingMetadata> Bindings { get; }

        public IEnumerable<BindingMetadata> InputBindings
        {
            get
            {
                return Bindings.Where(p => p.Direction != BindingDirection.Out);
            }
        }

        public IEnumerable<BindingMetadata> OutputBindings
        {
            get
            {
                return Bindings.Where(p => p.Direction != BindingDirection.In);
            }
        }
    }
```
### Class `ScriptInvocationContext`
```
    internal class ScriptInvocationContext
    {
        public FunctionMetadata FunctionMetadata { get; set; }

        public ExecutionContext ExecutionContext { get; set; }

        public IEnumerable<(string name, DataType type, object val)> Inputs { get; set; }

        public Dictionary<string, object> BindingData { get; set; }

        public CancellationToken CancellationToken { get; set; }

        public TaskCompletionSource<ScriptInvocationResult> ResultSource { get; set; }

        public ILogger Logger { get; set; }

        public System.Threading.ExecutionContext AsyncExecutionContext { get; set; }
    }
```
### Class `FunctionRegistrationContext`
```
    internal class FunctionRegistrationContext
    {
        public FunctionMetadata Metadata { get; set; }

        // A buffer block containing function invocations
        public BufferBlock<ScriptInvocationContext> InputBuffer { get; set; }
    }
```
### Class `WorkerDescription`
```
    public class WorkerDescription
    {
        /// <summary>
        /// Gets or sets the name of the supported language. This is the same name as the IConfiguration section for the worker.
        /// </summary>
        public string Language { get; set; }

        /// <summary>
        /// Gets or sets the supported file extension type. Functions are registered with workers based on extension.
        /// </summary>
        public string Extension { get; set; }

        /// <summary>
        /// Gets or sets the default executable path.
        /// </summary>
        public string DefaultExecutablePath { get; set; }

        /// <summary>
        /// Gets or sets the default path to the worker (relative to the bin/workers/{language} directory)
        /// </summary>
        public string DefaultWorkerPath { get; set; }
    }
```
### Class `ArgumentsDescription`
```
    public class ArgumentsDescription
    {
        /// <summary>
        /// Gets or sets the path to the executable (java, node, etc).
        /// </summary>
        public string ExecutablePath { get; set; }

        /// <summary>
        /// Gets or sets arguments to be passed to the executable. Optional.
        /// </summary>
        public List<string> ExecutableArguments { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the path to the worker file, i.e. nodejsWorker.js.
        /// </summary>
        public string WorkerPath { get; set; }

        /// <summary>
        /// Gets or sets arguments to be passed to the worker. Optional.
        /// </summary>
        public List<string> WorkerArguments { get; set; } = new List<string>();
    }
```

### Class `ScriptEvent`
```
    public class ScriptEvent
    {
        public ScriptEvent(string name, string source)
        {
            Name = name;
            Source = source;
        }

        public string Name { get; }

        public string Source { get; }
    }
```
### Class `RpcEvent` // StreamingMessage is defined in FunctionRpc.proto
```
    public class RpcEvent : ScriptEvent
    {
        internal RpcEvent(string workerId, StreamingMessage message, MessageOrigin origin = MessageOrigin.Host)
            : base(message.ContentCase.ToString(), EventSources.Rpc)
        {
            Message = message;
            Origin = origin;
            WorkerId = workerId;
        }

        public enum MessageOrigin
        {
            Worker,
            Host
        }
    }
```
### Class `OutboundEvent` // message origin is Host
```
    public class OutboundEvent : RpcEvent
    {
        public OutboundEvent(string workerId, StreamingMessage message) : base(workerId, message, MessageOrigin.Host)
        {
        }
    }
```
### Class `InboundEvent` // message origin is Worker
```
    public class InboundEvent : RpcEvent
    {
        public InboundEvent(string workerId, StreamingMessage message) : base(workerId, message, MessageOrigin.Worker)
        {
        }
    }
```
### Class `FunctionRpcService`
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
            ....
        }
    }
```
### Class `GrpcServer`
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
    }
```
### Class `LanguageWorkerChannel`
```
    internal class LanguageWorkerChannel : ILanguageWorkerChannel
    {
        public LanguageWorkerChannel(
            ScriptHostConfiguration scriptConfig,
            IScriptEventManager eventManager,
            IWorkerProcessFactory processFactory,
            IProcessRegistry processRegistry,
            IObservable<FunctionRegistrationContext> functionRegistrations, // TODO: where is this from?
            WorkerConfig workerConfig, // TODO: same, where is this from?
            Uri serverUri,
            ILoggerFactory loggerFactory)
        {
            _workerId = Guid.NewGuid().ToString();

            _scriptConfig = scriptConfig;
            _eventManager = eventManager;
            _processFactory = processFactory;
            _processRegistry = processRegistry;
            _functionRegistrations = functionRegistrations;
            _workerConfig = workerConfig;
            _serverUri = serverUri;

            _logger = loggerFactory.CreateLogger($"Worker.{workerConfig.Language}.{_workerId}");

            _inboundWorkerEvents = _eventManager.OfType<InboundEvent>()
                .Where(msg => msg.WorkerId == _workerId);

            _eventSubscriptions.Add(_inboundWorkerEvents
                .Where(msg => msg.MessageType == MsgType.RpcLog)
                .Subscribe(Log));

            if (scriptConfig.LogFilter.Filter("Worker", LogLevel.Trace))
            {
                _eventSubscriptions.Add(_eventManager.OfType<RpcEvent>()
                    .Where(msg => msg.WorkerId == _workerId)
                    .Subscribe(msg =>
                    {
                        var jsonMsg = JsonConvert.SerializeObject(msg, _verboseSerializerSettings);

                        // TODO: change to trace when ILogger & TraceWriter merge (issues with file trace writer)
                        _logger.LogInformation(jsonMsg);
                    }));
            }

            _eventSubscriptions.Add(_eventManager.OfType<FileEvent>()
                .Where(msg => Path.GetExtension(msg.FileChangeArguments.FullPath) == Config.Extension)
                .Throttle(TimeSpan.FromMilliseconds(300)) // debounce
                .Subscribe(msg => _eventManager.Publish(new HostRestartEvent())));

            StartWorker();
        }

        // start worker process and wait for an rpc start stream response
        internal void StartWorker()
        {
            _startSubscription = _inboundWorkerEvents.Where(msg => msg.MessageType == MsgType.StartStream)
                .Timeout(timeoutStart)
                .Take(1)
                .Subscribe(InitWorker, HandleWorkerError);

            var workerContext = new WorkerCreateContext()
            {
                RequestId = Guid.NewGuid().ToString(),
                WorkerId = _workerId,
                Arguments = _workerConfig.Arguments,
                WorkingDirectory = _scriptConfig.RootScriptPath,
                ServerUri = _serverUri,
            };

            _process = _processFactory.CreateWorkerProcess(workerContext);
            StartProcess(_workerId, _process);
        }

        // send capabilities to worker, wait for WorkerInitResponse
        internal void InitWorker(RpcEvent startEvent)
        {
            _processRegistry?.Register(_process);

            _inboundWorkerEvents.Where(msg => msg.MessageType == MsgType.WorkerInitResponse)
                .Timeout(timeoutInit)
                .Take(1)
                .Subscribe(WorkerReady, HandleWorkerError);

            Send(new StreamingMessage
            {
                WorkerInitRequest = new WorkerInitRequest()
                {
                    HostVersion = ScriptHost.Version
                }
            });
        }

        internal void WorkerReady(RpcEvent initEvent)
        {
            var initMessage = initEvent.Message.WorkerInitResponse;
            if (initMessage.Result.IsFailure(out Exception exc))
            {
                HandleWorkerError(exc);
                return;
            }

            // subscript to all function registrations in order to load functions
            _eventSubscriptions.Add(_functionRegistrations.Subscribe(Register));

            _eventSubscriptions.Add(_inboundWorkerEvents.Where(msg => msg.MessageType == MsgType.FunctionLoadResponse)
                .Subscribe((msg) => LoadResponse(msg.Message.FunctionLoadResponse)));

            _eventSubscriptions.Add(_inboundWorkerEvents.Where(msg => msg.MessageType == MsgType.InvocationResponse)
                .Subscribe((msg) => InvokeResponse(msg.Message.InvocationResponse)));

            _eventManager.Publish(new WorkerReadyEvent
            {
                Id = _workerId,
                Version = initMessage.WorkerVersion,
                Capabilities = initMessage.Capabilities,
                Config = _workerConfig,
            });
        }

        public void Register(FunctionRegistrationContext context)
        {
            FunctionMetadata metadata = context.Metadata;

            // associate the invocation input buffer with the function
            _functionInputBuffers[context.Metadata.FunctionId] = context.InputBuffer;

            // send a load request for the registered function
            FunctionLoadRequest request = new FunctionLoadRequest()
            {
                FunctionId = metadata.FunctionId,
                Metadata = new RpcFunctionMetadata()
                {
                    Name = metadata.Name,
                    Directory = metadata.FunctionDirectory,
                    EntryPoint = metadata.EntryPoint ?? string.Empty,
                    ScriptFile = metadata.ScriptFile ?? string.Empty
                }
            };

            foreach (var binding in metadata.Bindings)
            {
                request.Metadata.Bindings.Add(binding.Name, new BindingInfo
                {
                    Direction = (BindingInfo.Types.Direction)binding.Direction,
                    Type = binding.Type
                });
            }

            Send(new StreamingMessage
            {
                FunctionLoadRequest = request
            });
        }

        internal void LoadResponse(FunctionLoadResponse loadResponse)
        {
            if (loadResponse.Result.IsFailure(out Exception e))
            {
                _logger.LogError($"Function {loadResponse.FunctionId} failed to load", e);
            }
            else
            {
                var inputBuffer = _functionInputBuffers[loadResponse.FunctionId];

                // link the invocation inputs to the invoke call
                var invokeBlock = new ActionBlock<ScriptInvocationContext>(ctx => Invoke(ctx));
                var disposableLink = inputBuffer.LinkTo(invokeBlock);
                _inputLinks.Add(disposableLink);
            }
        }

        public void Invoke(ScriptInvocationContext context)
        {
            if (context.CancellationToken.IsCancellationRequested)
            {
                context.ResultSource.SetCanceled();
                return;
            }

            var functionMetadata = context.FunctionMetadata;

            InvocationRequest invocationRequest = new InvocationRequest()
            {
                FunctionId = functionMetadata.FunctionId,
                InvocationId = context.ExecutionContext.InvocationId.ToString(),
            };
            foreach (var pair in context.BindingData)
            {
                invocationRequest.TriggerMetadata.Add(pair.Key, pair.Value.ToRpc());
            }
            foreach (var input in context.Inputs)
            {
                invocationRequest.InputData.Add(new ParameterBinding()
                {
                    Name = input.name,
                    Data = input.val.ToRpc()
                });
            }

            context.AsyncExecutionContext = System.Threading.ExecutionContext.Capture();

            _executingInvocations.TryAdd(invocationRequest.InvocationId, context);

            Send(new StreamingMessage
            {
                InvocationRequest = invocationRequest
            });
        }

        internal void InvokeResponse(InvocationResponse invokeResponse)
        {
            if (_executingInvocations.TryRemove(invokeResponse.InvocationId, out ScriptInvocationContext context)
                && invokeResponse.Result.IsSuccess(context.ResultSource))
            {
                IDictionary<string, object> bindingsDictionary = invokeResponse.OutputData
                    .ToDictionary(binding => binding.Name, binding => binding.Data.ToObject());

                var result = new ScriptInvocationResult()
                {
                    Outputs = bindingsDictionary,
                    Return = invokeResponse?.ReturnValue?.ToObject()
                };
                context.ResultSource.SetResult(result);
            }
        }

        // TODO: move this out of LanguageWorkerChannel to WorkerProcessFactory
        internal void StartProcess(string workerId, Process process)
        {
            process.ErrorDataReceived += (sender, e) =>
            {
                // Java logs to stderr by default
                // TODO: per language stdout/err parser?
                if (e.Data != null)
                {
                    _logger.LogInformation(e.Data);
                }
            };
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    _logger.LogInformation(e.Data);
                }
            };
            process.EnableRaisingEvents = true;
            process.Exited += (s, e) =>
            {
                try
                {
                    if (process.ExitCode != 0)
                    {
                        HandleWorkerError(new Exception($"Worker process with pid {process.Id} exited with code {process.ExitCode}"));
                    }
                    process.WaitForExit();
                    process.Close();
                }
                catch
                {
                    HandleWorkerError(new Exception("Worker process is not attached"));
                }
            };

            _logger.LogInformation($"Start Process: {process.StartInfo.FileName} {process.StartInfo.Arguments}");

            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
        }

        private void Send(StreamingMessage msg)
        {
            _eventManager.Publish(new OutboundEvent(_workerId, msg));
        }
    }
```

### Class `WorkerConfig`
```
    internal class WorkerConfig
    {
        public WorkerDescription Description { get; set; }

        public ArgumentsDescription Arguments { get; set; }

        public string Extension => Description.Extension;

        public string Language => Description.Language;
    }
```
### Interface `IWorkerProviders`
```
    /// <summary>
    /// Enables a language worker implementor to specify how to create and configure the language worker process.
    /// </summary>
    public interface IWorkerProvider
    {
        /// <summary>
        /// Get the static description of the worker.
        /// </summary>
        /// <returns>The static description of the worker.</returns>
        WorkerDescription GetDescription();

        /// <summary>
        /// Tries to configure the arguments with any configuration / environment specific settings.
        /// </summary>
        /// <param name="args">The default arguments constructed by the host.</param>
        /// <param name="config">The host-level IConfiguration.</param>
        /// <param name="logger">The startup ILogger.</param>
        /// <returns>A bool that indicates if the args were configured successfully.</returns>
        bool TryConfigureArguments(ArgumentsDescription args, IConfiguration config, ILogger logger);
    }
```
### Class `JavaWorkerProvider`
```
    internal class JavaWorkerProvider : IWorkerProvider
    {
        public WorkerDescription GetDescription() => new WorkerDescription
        {
            Language = "Java",
            Extension = ".jar",
            DefaultWorkerPath = "azure-functions-java-worker.jar",
        };

        public bool TryConfigureArguments(ArgumentsDescription args, IConfiguration config, ILogger logger)
        {
            var options = new DefaultWorkerOptions();
            config.GetSection("workers:java").Bind(options);
            var env = new JavaEnvironment();
            config.Bind(env);
            if (string.IsNullOrEmpty(env.JAVA_HOME))
            {
                logger.LogError("Unable to configure java worker. Could not find JAVA_HOME app setting.");
                return false;
            }

            args.ExecutablePath = Path.GetFullPath(Path.Combine(env.ResolveJavaHome(), "bin", "java"));
            args.ExecutableArguments.Add("-jar");

            if (options.TryGetDebugPort(out int debugPort))
            {
                if (!env.HasJavaOpts)
                {
                    var debugOpts = $"-agentlib:jdwp=transport=dt_socket,server=y,suspend=n,address={debugPort}";
                    args.ExecutableArguments.Add(debugOpts);
                }
                else
                {
                    logger.LogWarning("Both JAVA_OPTS and debug port settings found. Defaulting to JAVA_OPTS.");
                }
            }

            if (env.HasJavaOpts)
            {
                args.ExecutableArguments.Add(env.JAVA_OPTS);
            }
            return true;
        }

        private class JavaEnvironment
        {
            public string JAVA_HOME { get; set; } = string.Empty;

            public string JAVA_OPTS { get; set; } = string.Empty;

            public string WEBSITE_INSTANCE_ID { get; set; } = string.Empty;

            public bool IsAzureEnvironment => !string.IsNullOrEmpty(WEBSITE_INSTANCE_ID);

            public bool HasJavaOpts => !string.IsNullOrEmpty(JAVA_OPTS);

            public string ResolveJavaHome()
            {
                if (IsAzureEnvironment)
                {
                    return Path.Combine(JAVA_HOME, "..", "zulu8.23.0.3-jdk8.0.144-win_x64");
                }
                else
                {
                    return JAVA_HOME;
                }
            }
        }
    }
```
### Class `NodeWorkerProvider`
```
    internal class NodeWorkerProvider : IWorkerProvider
    {
        public WorkerDescription GetDescription() => new WorkerDescription
        {
            Language = "Node",
            Extension = ".js",
            DefaultExecutablePath = "node",
            DefaultWorkerPath = Path.Combine("dist", "src", "nodejsWorker.js"),
        };

        public bool TryConfigureArguments(ArgumentsDescription args, IConfiguration config, ILogger logger)
        {
            var options = new DefaultWorkerOptions();
            config.GetSection("workers:node").Bind(options);
            if (options.TryGetDebugPort(out int debugPort))
            {
                args.ExecutableArguments.Add($"--inspect={debugPort}");
            }
            return true;
        }
    }
```
### Class `WorkerConfigFactory`
```
    // Gets fully configured WorkerConfigs from IWorkerProviders
    internal class WorkerConfigFactory
    {
        private readonly IConfiguration _config;
        private readonly ILogger _logger;
        private readonly string _assemblyDir;

        public IEnumerable<WorkerConfig> GetConfigs(IEnumerable<IWorkerProvider> providers)
        {
            foreach (var provider in providers)
            {
                var description = provider.GetDescription();
                var languageSection = _config.GetSection($"workers:{description.Language}");

                // get explicit worker path from config, or build relative path from default
                var workerPath = languageSection.GetSection("path").Value;
                if (string.IsNullOrEmpty(workerPath) && !string.IsNullOrEmpty(description.DefaultWorkerPath))
                {
                    workerPath = Path.Combine(_assemblyDir, "workers", description.Language.ToLower(), description.DefaultWorkerPath);
                }

                var arguments = new ArgumentsDescription()
                {
                    ExecutablePath = description.DefaultExecutablePath,
                    WorkerPath = workerPath
                };

                if (provider.TryConfigureArguments(arguments, _config, _logger))
                {
                    yield return new WorkerConfig()
                    {
                        Description = description,
                        Arguments = arguments
                    };
                }
                else
                {
                    _logger.LogError($"Could not configure language worker {description.Language}.");
                }
            }
        }
    }
```
### Interface `IFunctionRegistry`
```
    internal interface IFunctionRegistry : IDisposable
    {
        // Tests if the function metadata is supported by a known language worker
        bool IsSupported(FunctionMetadata metadata);

        // Registers a supported function with the dispatcher
        void Register(FunctionRegistrationContext context);
    }
```
### Class `FunctionRegistry`
```
    internal class FunctionRegistry : IFunctionRegistry
    {
        public FunctionRegistry(
            IScriptEventManager manager,
            IRpcServer server,
            CreateChannel channelFactory,
            IEnumerable<WorkerConfig> workers)
        {
            _eventManager = manager;
            _server = server;
            _channelFactory = channelFactory;
            _workerConfigs = workers?.ToList() ?? new List<WorkerConfig>();

            _workerErrorSubscription = _eventManager.OfType<WorkerErrorEvent>()
                .Subscribe(WorkerError);
        }

        internal WorkerState CreateWorkerState(WorkerConfig config)
        {
            var state = new WorkerState();
            state.Channel = _channelFactory(config, state.Functions);
            return state;
        }

        public void Register(FunctionRegistrationContext context)
        {
            WorkerConfig workerConfig = _workerConfigs.First(config => config.Extension == Path.GetExtension(context.Metadata.ScriptFile));
            var state = _channelState.GetOrAdd(workerConfig, CreateWorkerState);
            state.Functions.OnNext(context);
        }

        internal class WorkerState
        {
            internal ILanguageWorkerChannel Channel { get; set; }

            internal List<Exception> Errors { get; set; } = new List<Exception>();

            // Registered list of functions which can be replayed if the worker fails to start / errors
            internal ReplaySubject<FunctionRegistrationContext> Functions { get; set; } = new ReplaySubject<FunctionRegistrationContext>();
        }
    }
```
### Interface `ICompilation`
```
    public interface ICompilation
    {
        ImmutableArray<Diagnostic> GetDiagnostics();

        Task<object> EmitAsync(CancellationToken cancellationToken);
    }

    public interface ICompilation<TOutput> : ICompilation
    {
        new Task<TOutput> EmitAsync(CancellationToken cancellationToken);
    }
```
### Interface `IDotNetCompilation`
```
    public interface IDotNetCompilation : ICompilation<Assembly>
    {
        FunctionSignature GetEntryPointSignature(IFunctionEntryPointResolver entryPointResolver);
    }
```

### Class ``
```
    public sealed class CSharpCompilation : IDotNetCompilation
    {
        private readonly Compilation _compilation;

        public async Task<Assembly> EmitAsync(CancellationToken cancellationToken)
        {
            try
            {
                using (var assemblyStream = new MemoryStream())
                using (var pdbStream = new MemoryStream())
                {
                    var compilationWithAnalyzers = _compilation.WithAnalyzers(GetAnalyzers());
                    var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
                    var emitOptions = new EmitOptions().WithDebugInformationFormat(PlatformHelper.IsWindows ? DebugInformationFormat.Pdb : DebugInformationFormat.PortablePdb);
                    var emitResult = compilationWithAnalyzers.Compilation.Emit(assemblyStream, pdbStream, options: emitOptions, cancellationToken: cancellationToken);

                    diagnostics = diagnostics.AddRange(emitResult.Diagnostics);

                    if (diagnostics.Any(di => di.Severity == DiagnosticSeverity.Error))
                    {
                        throw new CompilationErrorException("Script compilation failed.", diagnostics);
                    }

                    // Check if cancellation was requested while we were compiling,
                    // and if so quit here.
                    cancellationToken.ThrowIfCancellationRequested();

                    return Assembly.Load(assemblyStream.GetBuffer(), pdbStream.GetBuffer());
                }
            }
            catch (Exception exc) when (!(exc is CompilationErrorException))
            {
                throw new CompilationServiceException($"C# compilation service error: {exc.Message}", exc);
            }
        }
    }
```
### Interface `IFunctionInvoker`
```
    public interface IFunctionInvoker
    {
        /// <summary>
        /// Gets the <see cref="ILogger"/> for this function.
        /// </summary>
        ILogger FunctionLogger { get; }

        /// <summary>
        /// Invoke the function using the specified parameters.
        /// </summary>
        /// <param name="parameters">The parameters.</param>
        /// <returns>A <see cref="Task"/> for the invocation.</returns>
        Task<object> Invoke(object[] parameters);

        /// <summary>
        /// This method is called by the host when invocation exceptions occur
        /// outside of the invocation. This allows the invoker to inspect/log the
        /// exception as necessary.
        /// </summary>
        /// <param name="ex">The <see cref="Exception"/> that occurred.</param>
        void OnError(Exception ex);
    }
```
### Class `FunctionInvokerBase`
```
    public abstract class FunctionInvokerBase : IFunctionInvoker, IDisposable
    {
        public ScriptHost Host { get; }

        public ILogger FunctionLogger { get; }

        public FunctionMetadata Metadata { get; }

        public async Task<object> Invoke(object[] parameters)
        {
            FunctionInvocationContext context = GetContextFromParameters(parameters, Metadata);
            return await InvokeCore(parameters, context);
        }

        private static FunctionInvocationContext GetContextFromParameters(object[] parameters, FunctionMetadata metadata)
        {
            // We require the ExecutionContext, so this will throw if one is not found.
            ExecutionContext functionExecutionContext = parameters.OfType<ExecutionContext>().First();
            functionExecutionContext.FunctionDirectory = metadata.FunctionDirectory;
            functionExecutionContext.FunctionName = metadata.Name;

            // These may not be present, so null is okay.
            Binder binder = parameters.OfType<Binder>().FirstOrDefault();
            ILogger logger = parameters.OfType<ILogger>().FirstOrDefault();

            FunctionInvocationContext context = new FunctionInvocationContext
            {
                ExecutionContext = functionExecutionContext,
                Binder = binder,
                Logger = logger
            };

            return context;
        }

        protected abstract Task<object> InvokeCore(object[] parameters, FunctionInvocationContext context);
    }
```
### Class `DotNetFunctionInvoker`
```
    public sealed class DotNetFunctionInvoker : FunctionInvokerBase
    {
        private readonly FunctionAssemblyLoader _assemblyLoader;
        private readonly string _triggerInputName;
        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;
        private readonly IFunctionEntryPointResolver _functionEntryPointResolver;
        private readonly ICompilationService<IDotNetCompilation> _compilationService;
        private readonly FunctionLoader<MethodInfo> _functionLoader;
        private readonly IMetricsLogger _metricsLogger;

        private FunctionSignature _functionSignature;
        private IFunctionMetadataResolver _metadataResolver;
        private Func<Task> _reloadScript;
        private Action _onReferencesChanged;
        private Action _restorePackages;
        private string[] _watchedFileTypes;
        private int _compilerErrorCount;

        internal DotNetFunctionInvoker(ScriptHost host,
            FunctionMetadata functionMetadata,
            Collection<FunctionBinding> inputBindings,
            Collection<FunctionBinding> outputBindings,
            IFunctionEntryPointResolver functionEntryPointResolver,
            FunctionAssemblyLoader assemblyLoader,
            ICompilationServiceFactory<ICompilationService<IDotNetCompilation>, IFunctionMetadataResolver> compilationServiceFactory,
            IFunctionMetadataResolver metadataResolver = null)
            : base(host, functionMetadata)
        {
            _metricsLogger = Host.ScriptConfig.HostConfig.GetService<IMetricsLogger>();
            _functionEntryPointResolver = functionEntryPointResolver;
            _assemblyLoader = assemblyLoader;
            _metadataResolver = metadataResolver ?? CreateMetadataResolver(host, functionMetadata, FunctionLogger);
            _compilationService = compilationServiceFactory.CreateService(functionMetadata.ScriptType, _metadataResolver);
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
            _triggerInputName = functionMetadata.Bindings.FirstOrDefault(b => b.IsTrigger).Name;

            InitializeFileWatcher();

            _functionLoader = new FunctionLoader<MethodInfo>(CreateFunctionTarget);

            _reloadScript = ReloadScriptAsync;
            _reloadScript = _reloadScript.Debounce();

            _onReferencesChanged = OnReferencesChanged;
            _onReferencesChanged = _onReferencesChanged.Debounce();

            _restorePackages = RestorePackages;
            _restorePackages = _restorePackages.Debounce();
        }

        protected override async Task<object> InvokeCore(object[] parameters, FunctionInvocationContext context)
        {
            // Separate system parameters from the actual method parameters
            object[] originalParameters = parameters;
            MethodInfo function = await GetFunctionTargetAsync();

            int actualParameterCount = function.GetParameters().Length;
            parameters = parameters.Take(actualParameterCount).ToArray();

            object result = function.Invoke(null, parameters);

            // after the function executes, we have to copy values back into the original
            // array to ensure object references are maintained (since we took a copy above)
            for (int i = 0; i < parameters.Length; i++)
            {
                originalParameters[i] = parameters[i];
            }

            // unwrap the task
            if (result is Task)
            {
                result = await ((Task)result).ContinueWith(t => GetTaskResult(t), TaskContinuationOptions.ExecuteSynchronously);
            }

            return result;
        }

        private async Task<MethodInfo> CreateFunctionTarget(CancellationToken cancellationToken)
        {
            try
            {
                await VerifyPackageReferencesAsync();

                string eventName = string.Format(MetricEventNames.FunctionCompileLatencyByLanguageFormat, _compilationService.Language);
                using (_metricsLogger.LatencyEvent(eventName))
                {
                    IDotNetCompilation compilation = await _compilationService.GetFunctionCompilationAsync(Metadata);

                    Assembly assembly = await compilation.EmitAsync(cancellationToken);
                    _assemblyLoader.CreateOrUpdateContext(Metadata, assembly, _metadataResolver, FunctionLogger);

                    FunctionSignature functionSignature = compilation.GetEntryPointSignature(_functionEntryPointResolver);

                    ImmutableArray<Diagnostic> bindingDiagnostics = ValidateFunctionBindingArguments(functionSignature, _triggerInputName, _inputBindings, _outputBindings, throwIfFailed: true);
                    TraceCompilationDiagnostics(bindingDiagnostics);

                    _compilerErrorCount = 0;

                    // Set our function entry point signature
                    _functionSignature = functionSignature;

                    return _functionSignature.GetMethod(assembly);
                }
            }
            ....
        }
    }
```
### Class `WorkerLanguageInvoker`
```
    internal class WorkerLanguageInvoker : FunctionInvokerBase
    {
        private readonly Collection<FunctionBinding> _inputBindings;
        private readonly Collection<FunctionBinding> _outputBindings;
        private readonly BindingMetadata _trigger;
        private readonly Action<ScriptInvocationResult> _handleScriptReturnValue;
        private readonly BufferBlock<ScriptInvocationContext> _invocationBuffer;

        internal WorkerLanguageInvoker(ScriptHost host, BindingMetadata trigger, FunctionMetadata functionMetadata,
            Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings, BufferBlock<ScriptInvocationContext> invocationBuffer)
            : base(host, functionMetadata)
        {
            _trigger = trigger;
            _inputBindings = inputBindings;
            _outputBindings = outputBindings;
            _invocationBuffer = invocationBuffer;

            InitializeFileWatcherIfEnabled();

            if (_outputBindings.Any(p => p.Metadata.IsReturn))
            {
                _handleScriptReturnValue = HandleReturnParameter;
            }
            else
            {
                _handleScriptReturnValue = HandleOutputDictionary;
            }
        }

        protected override async Task<object> InvokeCore(object[] parameters, FunctionInvocationContext context)
        {
            string invocationId = context.ExecutionContext.InvocationId.ToString();

            // TODO: fix extensions and remove
            object triggerValue = TransformInput(parameters[0], context.Binder.BindingData);
            var triggerInput = (_trigger.Name, _trigger.DataType ?? DataType.String, triggerValue);
            var inputs = new[] { triggerInput }.Concat(await BindInputsAsync(context.Binder));

            ScriptInvocationContext invocationContext = new ScriptInvocationContext()
            {
                FunctionMetadata = Metadata,
                BindingData = context.Binder.BindingData,
                ExecutionContext = context.ExecutionContext,
                Inputs = inputs,
                ResultSource = new TaskCompletionSource<ScriptInvocationResult>(),

                // TODO: link up cancellation token to parameter descriptors
                CancellationToken = CancellationToken.None,
                Logger = context.Logger
            };

            ScriptInvocationResult result;
            _invocationBuffer.Post(invocationContext);
            result = await invocationContext.ResultSource.Task;

            await BindOutputsAsync(triggerValue, context.Binder, result);
            return result.Return;
        }
    }
```
### Class `FunctionDescriptorProvider`
```
    public abstract class FunctionDescriptorProvider
    {
        public virtual bool TryCreate(FunctionMetadata functionMetadata, out FunctionDescriptor functionDescriptor)
        {
            ....
            ValidateFunction(functionMetadata);

            // parse the bindings
            Collection<FunctionBinding> inputBindings = FunctionBinding.GetBindings(Config, functionMetadata.InputBindings, FileAccess.Read);
            Collection<FunctionBinding> outputBindings = FunctionBinding.GetBindings(Config, functionMetadata.OutputBindings, FileAccess.Write);

            BindingMetadata triggerMetadata = functionMetadata.InputBindings.FirstOrDefault(p => p.IsTrigger);
            string scriptFilePath = Path.Combine(Config.RootScriptPath, functionMetadata.ScriptFile ?? string.Empty);
            functionDescriptor = null;
            IFunctionInvoker invoker = null;

            try
            {
                invoker = CreateFunctionInvoker(scriptFilePath, triggerMetadata, functionMetadata, inputBindings, outputBindings);

                Collection<CustomAttributeBuilder> methodAttributes = new Collection<CustomAttributeBuilder>();
                Collection<ParameterDescriptor> parameters = GetFunctionParameters(invoker, functionMetadata, triggerMetadata, methodAttributes, inputBindings, outputBindings);

                functionDescriptor = new FunctionDescriptor(functionMetadata.Name, invoker, functionMetadata, parameters, methodAttributes, inputBindings, outputBindings);

                return true;
            }
            ....
        }
    }
```
### Class `DotNetFunctionDescriptorProvider`
```
    internal sealed class DotNetFunctionDescriptorProvider : FunctionDescriptorProvider, IDisposable
    {
        private readonly FunctionAssemblyLoader _assemblyLoader;
        private readonly ICompilationServiceFactory<ICompilationService<IDotNetCompilation>, IFunctionMetadataResolver> _compilationServiceFactory;

        public DotNetFunctionDescriptorProvider(ScriptHost host, ScriptHostConfiguration config)
           : this(host, config, new DotNetCompilationServiceFactory(config.HostConfig.LoggerFactory))
        {
        }

        public DotNetFunctionDescriptorProvider(ScriptHost host, ScriptHostConfiguration config,
            ICompilationServiceFactory<ICompilationService<IDotNetCompilation>, IFunctionMetadataResolver> compilationServiceFactory)
            : base(host, config)
        {
            _assemblyLoader = new FunctionAssemblyLoader(config.RootScriptPath);
            _compilationServiceFactory = compilationServiceFactory;
        }

        public override bool TryCreate(FunctionMetadata functionMetadata, out FunctionDescriptor functionDescriptor)
        {
            if (functionMetadata == null)
            {
                throw new ArgumentNullException("functionMetadata");
            }

            functionDescriptor = null;

            // We can only handle script types supported by the current compilation service factory
            if (!_compilationServiceFactory.SupportedScriptTypes.Contains(functionMetadata.ScriptType))
            {
                return false;
            }

            return base.TryCreate(functionMetadata, out functionDescriptor);
        }

        protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            return new DotNetFunctionInvoker(Host, functionMetadata, inputBindings, outputBindings, new FunctionEntryPointResolver(functionMetadata.EntryPoint), _assemblyLoader, _compilationServiceFactory);
        }
    }
```
### Class `WorkerFunctionDescriptorProvider`
```
    internal class WorkerFunctionDescriptorProvider : FunctionDescriptorProvider
    {
        private IFunctionRegistry _dispatcher;

        public WorkerFunctionDescriptorProvider(ScriptHost host, ScriptHostConfiguration config, IFunctionRegistry dispatcher)
            : base(host, config)
        {
            _dispatcher = dispatcher;
        }

        public override bool TryCreate(FunctionMetadata functionMetadata, out FunctionDescriptor functionDescriptor)
        {
            if (functionMetadata == null)
            {
                throw new ArgumentNullException(nameof(functionMetadata));
            }
            functionDescriptor = null;
            return _dispatcher.IsSupported(functionMetadata)
                && base.TryCreate(functionMetadata, out functionDescriptor);
        }

        protected override IFunctionInvoker CreateFunctionInvoker(string scriptFilePath, BindingMetadata triggerMetadata, FunctionMetadata functionMetadata, Collection<FunctionBinding> inputBindings, Collection<FunctionBinding> outputBindings)
        {
            var inputBuffer = new BufferBlock<ScriptInvocationContext>();
            _dispatcher.Register(new FunctionRegistrationContext
            {
                Metadata = functionMetadata,
                InputBuffer = inputBuffer
            });
            return new WorkerLanguageInvoker(Host, triggerMetadata, functionMetadata, inputBindings, outputBindings, inputBuffer);
        }
    }
```
### Class `FunctionGenerator`
```
    public static class FunctionGenerator
    {
        private static Dictionary<string, IFunctionInvoker> _invokerMap = new Dictionary<string, IFunctionInvoker>();

        // TODO: make this private
        public static IFunctionInvoker GetInvoker(string method)
        {
            return _invokerMap[method];
        }

        public static Type Generate(string functionAssemblyName, string typeName, Collection<CustomAttributeBuilder> typeAttributes, Collection<FunctionDescriptor> functions)
        {
            if (functions == null)
            {
                throw new ArgumentNullException("functions");
            }

            AssemblyName assemblyName = new AssemblyName(functionAssemblyName);
            AssemblyBuilder assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);

            ModuleBuilder mb = assemblyBuilder.DefineDynamicModule(assemblyName.Name);

            TypeBuilder tb = mb.DefineType(typeName, TypeAttributes.Public);

            if (typeAttributes != null)
            {
                foreach (CustomAttributeBuilder attributeBuilder in typeAttributes)
                {
                    tb.SetCustomAttribute(attributeBuilder);
                }
            }

            foreach (FunctionDescriptor function in functions)
            {
                if (function.Metadata.IsDirect)
                {
                    continue;
                }

                var retValue = function.Parameters.Where(x => x.Name == ScriptConstants.SystemReturnParameterName).FirstOrDefault();
                var parameters = function.Parameters.Where(x => x != retValue).ToArray();

                MethodBuilder methodBuilder = tb.DefineMethod(function.Name, MethodAttributes.Public | MethodAttributes.Static);
                Type[] types = parameters.Select(p => p.Type).ToArray();
                methodBuilder.SetParameters(types);

                Type innerReturnType = null;

                if (retValue == null)
                {
                    methodBuilder.SetReturnType(typeof(Task));
                }
                else
                {
                    // It's critical to set the proper return type for the binder.
                    // The return parameters was added a MakeByRefType, so need to get the inner type.
                    innerReturnType = retValue.Type.GetElementType();

                    // Task<> derives from Task.
                    var actualReturnType = typeof(Task<>).MakeGenericType(innerReturnType);

                    methodBuilder.SetReturnType(actualReturnType);

                    ParameterBuilder parameterBuilder = methodBuilder.DefineParameter(0, ParameterAttributes.Retval, null);

                    if (retValue.CustomAttributes != null)
                    {
                        foreach (CustomAttributeBuilder attributeBuilder in retValue.CustomAttributes)
                        {
                            parameterBuilder.SetCustomAttribute(attributeBuilder);
                        }
                    }
                }

                if (function.CustomAttributes != null)
                {
                    foreach (CustomAttributeBuilder attributeBuilder in function.CustomAttributes)
                    {
                        methodBuilder.SetCustomAttribute(attributeBuilder);
                    }
                }

                for (int i = 0; i < parameters.Length; i++)
                {
                    ParameterDescriptor parameter = parameters[i];
                    ParameterBuilder parameterBuilder = methodBuilder.DefineParameter(i + 1, parameter.Attributes, parameter.Name);
                    if (parameter.CustomAttributes != null)
                    {
                        foreach (CustomAttributeBuilder attributeBuilder in parameter.CustomAttributes)
                        {
                            parameterBuilder.SetCustomAttribute(attributeBuilder);
                        }
                    }
                }

                _invokerMap[function.Name] = function.Invoker;

                MethodInfo invokeMethod = function.Invoker.GetType().GetMethod("Invoke");
                MethodInfo getInvoker = typeof(FunctionGenerator).GetMethod("GetInvoker", BindingFlags.Static | BindingFlags.Public);

                ILGenerator il = methodBuilder.GetILGenerator();

                LocalBuilder argsLocal = il.DeclareLocal(typeof(object[]));
                LocalBuilder invokerLocal = il.DeclareLocal(typeof(IFunctionInvoker));

                il.Emit(OpCodes.Nop);

                // declare an array for all parameter values
                il.Emit(OpCodes.Ldc_I4, parameters.Length);
                il.Emit(OpCodes.Newarr, typeof(object));
                il.Emit(OpCodes.Stloc, argsLocal);

                // copy each parameter into the arg array
                for (int i = 0; i < parameters.Length; i++)
                {
                    ParameterDescriptor parameter = parameters[i];

                    il.Emit(OpCodes.Ldloc, argsLocal);
                    il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Ldarg, i);

                    // For Out and Ref types, need to do an indirection.
                    if (parameter.Type.IsByRef)
                    {
                        il.Emit(OpCodes.Ldind_Ref);
                    }

                    // Box value types
                    if (parameter.Type.IsValueType)
                    {
                        il.Emit(OpCodes.Box, parameter.Type);
                    }

                    il.Emit(OpCodes.Stelem_Ref);
                }

                // get the invoker instance
                il.Emit(OpCodes.Ldstr, function.Name);
                il.Emit(OpCodes.Call, getInvoker);
                il.Emit(OpCodes.Stloc, invokerLocal);

                // now call the invoker, passing in the args
                il.Emit(OpCodes.Ldloc, invokerLocal);
                il.Emit(OpCodes.Ldloc, argsLocal);
                il.Emit(OpCodes.Callvirt, invokeMethod); // pushes a Task<object>

                if (parameters.Any(p => p.Type.IsByRef))
                {
                    LocalBuilder taskLocal = il.DeclareLocal(typeof(Task<object>));
                    LocalBuilder taskAwaiterLocal = il.DeclareLocal(typeof(TaskAwaiter<object>));

                    // We need to wait on the function's task if we have any out/ref
                    // parameters to ensure they have been populated before we copy them back

                    // Store the result into a local Task
                    // and load it onto the evaluation stack
                    il.Emit(OpCodes.Stloc, taskLocal);
                    il.Emit(OpCodes.Ldloc, taskLocal);

                    // Call "GetAwaiter" on the Task
                    il.Emit(OpCodes.Callvirt, typeof(Task<object>).GetMethod("GetAwaiter", Type.EmptyTypes));

                    // Call "GetResult", which will synchonously wait for the Task to complete
                    il.Emit(OpCodes.Stloc, taskAwaiterLocal);
                    il.Emit(OpCodes.Ldloca, taskAwaiterLocal);
                    il.Emit(OpCodes.Call, typeof(TaskAwaiter<object>).GetMethod("GetResult"));
                    il.Emit(OpCodes.Pop); // ignore GetResult();

                    // Copy back out and ref parameters
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var param = parameters[i];
                        if (!param.Type.IsByRef)
                        {
                            continue;
                        }

                        il.Emit(OpCodes.Ldarg, i);

                        il.Emit(OpCodes.Ldloc, argsLocal);
                        il.Emit(OpCodes.Ldc_I4, i);
                        il.Emit(OpCodes.Ldelem_Ref);
                        il.Emit(OpCodes.Castclass, param.Type.GetElementType());

                        il.Emit(OpCodes.Stind_Ref);
                    }

                    il.Emit(OpCodes.Ldloc, taskLocal);
                }

                // Need to coerce.
                if (innerReturnType != null)
                {
                    var m = typeof(FunctionGenerator).GetMethod("Coerce", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    var m2 = m.MakeGenericMethod(innerReturnType);
                    il.Emit(OpCodes.Call, m2);
                }

                il.Emit(OpCodes.Ret);
            }

            Type t = tb.CreateTypeInfo().AsType();

            return t;
        }

        public static async Task<T> Coerce<T>(Task<object> src)
        {
            var val = await src;
            return (T)val;
        }
    }
```
### Class `ScriptHost`
```
    public class ScriptHost : JobHost
    {
        protected internal ScriptHost(IScriptHostEnvironment environment,
            IScriptEventManager eventManager,
            ScriptHostConfiguration scriptConfig = null,
            ScriptSettingsManager settingsManager = null,
            ILoggerProviderFactory loggerProviderFactory = null,
            ProxyClientExecutor proxyClient = null)
            : base(scriptConfig.HostConfig)
        {
            scriptConfig = scriptConfig ?? new ScriptHostConfiguration();
            _hostConfig = scriptConfig.HostConfig;

            if (!Path.IsPathRooted(scriptConfig.RootScriptPath))
            {
                scriptConfig.RootScriptPath = Path.Combine(Environment.CurrentDirectory, scriptConfig.RootScriptPath);
            }
            ScriptConfig = scriptConfig;
            _scriptHostEnvironment = environment;
            FunctionErrors = new Dictionary<string, Collection<string>>(StringComparer.OrdinalIgnoreCase);

            EventManager = eventManager;

            _settingsManager = settingsManager ?? ScriptSettingsManager.Instance;
            _proxyClient = proxyClient;

            _loggerProviderFactory = loggerProviderFactory ?? new DefaultLoggerProviderFactory();
        }

        public event EventHandler HostInitializing;
        public event EventHandler HostInitialized;
        public event EventHandler HostStarted;
        public event EventHandler IsPrimaryChanged;

        /// <summary>
        /// Gets the collection of all valid Functions. For functions that are in error
        /// and were unable to load successfully, consult the <see cref="FunctionErrors"/> collection.
        /// </summary>
        public virtual Collection<FunctionDescriptor> Functions { get; private set; }

        public virtual async Task CallAsync(string method, Dictionary<string, object> arguments, CancellationToken cancellationToken = default(CancellationToken))
        {
            await base.CallAsync(method, arguments, cancellationToken);
        }

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
                ....

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
                ....

                // take a snapshot so we can detect function additions/removals
                _directorySnapshot = Directory.EnumerateDirectories(ScriptConfig.RootScriptPath).ToImmutableArray();

                // Scan the function.json early to determine the requirements.
                // FunctionMetadata is defined in WebJobs
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
                    ....
                }

                var directTypes = GetDirectTypes(functionMetadata);

                LoadDirectlyReferencesExtensions(directTypes);

                LoadCustomExtensions();
                ....

                _descriptorProviders = new List<FunctionDescriptorProvider>()
                {
                    new DotNetFunctionDescriptorProvider(this, ScriptConfig),
                    new WorkerFunctionDescriptorProvider(this, ScriptConfig, _functionDispatcher),
                };

                // read all script functions and apply to JobHostConfiguration
                Collection<FunctionDescriptor> functions = GetFunctionDescriptors(functionMetadata);
                Collection<CustomAttributeBuilder> typeAttributes = new Collection<CustomAttributeBuilder>();
                string typeName = string.Format(CultureInfo.InvariantCulture, "{0}.{1}", GeneratedTypeNamespace, GeneratedTypeName);

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
    }
```
### Class `ScriptHostFactory` // used by ScriptHostManager
```
    public sealed class ScriptHostFactory : IScriptHostFactory
    {
        public ScriptHost Create(
            IScriptHostEnvironment environment,
            IScriptEventManager eventManager,
            ScriptSettingsManager settingsManager,
            ScriptHostConfiguration config,
            ILoggerProviderFactory loggerProviderFactory)
        {
            return new ScriptHost(environment, eventManager, config, settingsManager, loggerProviderFactory);
        }
    }
```
### Class `ScriptEventManager` // used by ScriptHostManager
```
    public sealed class ScriptEventManager : IScriptEventManager, IDisposable
    {
        private readonly Subject<ScriptEvent> _subject = new Subject<ScriptEvent>();
        private bool _disposed = false;

        public void Publish(ScriptEvent scriptEvent) => _subject.OnNext(scriptEvent);

        public IDisposable Subscribe(IObserver<ScriptEvent> observer) => _subject.Subscribe(observer);
    }
```

### Class `HostPerformanceManager` // used by ScriptHostManager
```
    public class HostPerformanceManager
    {
        private readonly ScriptSettingsManager _settingsManager;
        private readonly HostHealthMonitorConfiguration _healthMonitorConfig;

        public virtual bool IsUnderHighLoad(Collection<string> exceededCounters = null, ILogger logger = null)
        {
            var counters = GetPerformanceCounters(logger);
            if (counters != null)
            {
                return IsUnderHighLoad(counters, exceededCounters, _healthMonitorConfig.CounterThreshold);
            }

            return false;
        }

        internal static bool IsUnderHighLoad(ApplicationPerformanceCounters counters, Collection<string> exceededCounters = null, float threshold = HostHealthMonitorConfiguration.DefaultCounterThreshold)
        {
            bool exceeded = false;

            // determine all counters whose limits have been exceeded
            exceeded |= ThresholdExceeded("Connections", counters.Connections, counters.ConnectionLimit, threshold, exceededCounters);
            exceeded |= ThresholdExceeded("Threads", counters.Threads, counters.ThreadLimit, threshold, exceededCounters);
            exceeded |= ThresholdExceeded("Processes", counters.Processes, counters.ProcessLimit, threshold, exceededCounters);
            exceeded |= ThresholdExceeded("NamedPipes", counters.NamedPipes, counters.NamedPipeLimit, threshold, exceededCounters);
            exceeded |= ThresholdExceeded("Sections", counters.Sections, counters.SectionLimit, threshold, exceededCounters);

            return exceeded;
        }

        internal static bool ThresholdExceeded(string name, long currentValue, long limit, float threshold, Collection<string> exceededCounters = null)
        {
            if (limit <= 0)
            {
                // no limit to apply
                return false;
            }

            float currentUsage = (float)currentValue / limit;
            bool exceeded = currentUsage > threshold;
            if (exceeded && exceededCounters != null)
            {
                exceededCounters.Add(name);
            }
            return exceeded;
        }

        internal ApplicationPerformanceCounters GetPerformanceCounters(ILogger logger = null)
        {
            string json = _settingsManager.GetSetting(EnvironmentSettingNames.AzureWebsiteAppCountersName);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    // TEMP: need to parse this specially to work around bug where
                    // sometimes an extra garbage character occurs after the terminal
                    // brace
                    int idx = json.LastIndexOf('}');
                    if (idx > 0)
                    {
                        json = json.Substring(0, idx + 1);
                    }

                    return JsonConvert.DeserializeObject<ApplicationPerformanceCounters>(json);
                }
                catch (JsonReaderException ex)
                {
                    logger.LogError($"Failed to deserialize application performance counters. JSON Content: \"{json}\"", ex);
                }
            }

            return null;
        }
    }
```
### Class `ScriptHostManager`
```
    /// <summary>
    /// Class encapsulating a <see cref="ScriptHost"/> an keeping a singleton
    /// instance always alive, restarting as necessary.
    /// </summary>
    public class ScriptHostManager : IScriptHostEnvironment, IDisposable
    {
        public ScriptHostManager(
            ScriptHostConfiguration config,
            IScriptEventManager eventManager = null,
            IScriptHostEnvironment environment = null,
            ILoggerProviderFactory loggerProviderFactory = null,
            HostPerformanceManager hostPerformanceManager = null)
            : this(config, ScriptSettingsManager.Instance, new ScriptHostFactory(), eventManager, environment, loggerProviderFactory, hostPerformanceManager)
        {
            if (config.FileWatchingEnabled)
            {
                // We only setup a subscription here as the actual ScriptHost will create the publisher
                // when initialized.
                _fileEventSubscription = EventManager.OfType<FileEvent>()
                     .Where(f => string.Equals(f.Source, EventSources.ScriptFiles, StringComparison.Ordinal))
                     .Subscribe(e => OnScriptFileChanged(null, e.FileChangeArguments));
            }
        }

        public ScriptHostManager(ScriptHostConfiguration config,
            ScriptSettingsManager settingsManager,
            IScriptHostFactory scriptHostFactory,
            IScriptEventManager eventManager = null,
            IScriptHostEnvironment environment = null,
            ILoggerProviderFactory loggerProviderFactory = null,
            HostPerformanceManager hostPerformanceManager = null)
        {
            ....
            scriptHostFactory = scriptHostFactory ?? new ScriptHostFactory();
            _environment = environment ?? this;
            _config = config;
            _settingsManager = settingsManager;
            _scriptHostFactory = scriptHostFactory;
            _loggerProviderFactory = loggerProviderFactory;

            EventManager = eventManager ?? new ScriptEventManager();

            _structuredLogWriter = new StructuredLogWriter(EventManager, config.RootLogPath);
            _performanceManager = hostPerformanceManager ?? new HostPerformanceManager(settingsManager, _config.HostHealthMonitor);

            if (ShouldMonitorHostHealth)
            {
                _hostHealthCheckTimer = new Timer(OnHostHealthCheckTimer, null, TimeSpan.Zero, _config.HostHealthMonitor.HealthCheckInterval);
                _healthCheckWindow = new SlidingWindow<bool>(_config.HostHealthMonitor.HealthCheckWindow);
            }
        }

```


// This is `ScriptHostManager` calls `RunAndBlock()`
```
    public class ScriptHostManager : IScriptHostEnvironment, IDisposable
    {
        ....
        public void RunAndBlock(CancellationToken cancellationToken = default(CancellationToken))
        {
            _consecutiveErrorCount = 0;
            do
            {
                ScriptHost newInstance = null;

                try
                {
                    ....
                    OnInitializeConfig(_config);
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

                    // log any function initialization errors
                    LogErrors(newInstance);

                    LastError = null;
                    _consecutiveErrorCount = 0;
                    _restartDelayTokenSource = null;

                    // Wait for a restart signal. This event will automatically reset.
                    // While we're restarting, it is possible for another restart to be
                    // signaled. That is fine - the restart will be processed immediately
                    // once we get to this line again. The important thing is that these
                    // restarts are only happening on a single thread.
                    WaitHandle.WaitAny(new WaitHandle[]
                    {
                        cancellationToken.WaitHandle,
                        _restartHostEvent,
                        _stopEvent
                    });

                    // Orphan the current host instance. We're stopping it, so it won't listen for any new functions
                    // it will finish any currently executing functions and then clean itself up.
                    // Spin around and create a new host instance.
                    Task.Run(() => Orphan(newInstance)
                        .ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                            {
                                t.Exception.Handle(e => true);
                            }
                        }, TaskContinuationOptions.ExecuteSynchronously));
                }
                ....
            }
            while (!_stopped && !cancellationToken.IsCancellationRequested);
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


## References
1. https://en.wikipedia.org/wiki/GRPC
2. https://github.com/grpc/grpc/tree/master/examples/csharp/helloworld
3. https://github.com/Azure/azure-webjobs-sdk
4. https://github.com/Azure/azure-webjobs-sdk/wiki/Introduction