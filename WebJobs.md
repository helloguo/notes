# Azure WebJobs Study

## 1 Azure WebJobs Basics
"The Azure WebJobs SDK is a framework that simplifies the task of writing background processing code that runs in Azure. The Azure WebJobs SDK includes a declarative binding and trigger system that works with Azure Storage Blobs, Queues and Tables as well as Service Bus. The binding system makes it incredibly easy to write code that reads or writes Azure Storage objects. The trigger system automatically invokes a function in your code whenever any new data is received in a queue or blob." [1]

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

The `QueueTrigger` attribute binds the inputText parameter to the value of the queue message. And the `Blob` attribute binds a `TextWriter` object to a blob named "blobname" in a container named "containername"."[2]

As we can see from above sample code, an instance `JobHost host` is created first. Then `host` calls `RunAndBlock()`. But how is the funtion `ProcessQueueMessage()` getting invoked when `webjobsqueue` has new element? We will see this in the following section.

## 2 Azure WebJobs Source Code Analysis
### 2.1 `JobHost` and `JobHostConfiguration`
#### 2.1.1 `JobHost`
As we can see from above sample code, in order to use WebJobs SDK, we need to create an instance of `JobHost` first. Let's look at the constructor this class and the related class `JobHostConfiguration`.
```
    /// <summary>
    /// A <see cref="JobHost"/> is the execution container for jobs. Once started, the
    /// <see cref="JobHost"/> will manage and run job functions when they are triggered.
    /// </summary>
    public class JobHost : IDisposable, IJobInvoker
    {
        ....
        private readonly JobHostConfiguration _config;
        private JobHostContext _context;
        private IListener _listener;
        private ILogger _logger;
        ....

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHost"/> class, using a Microsoft Azure Storage connection
        /// string located in the connectionStrings section of the configuration file or in environment variables.
        /// </summary>
        public JobHost()
            : this(new JobHostConfiguration())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHost"/> class using the configuration provided.
        /// </summary>
        /// <param name="configuration">The job host configuration.</param>
        public JobHost(JobHostConfiguration configuration)
        {
            ....
            _config = configuration;
            ....
        }
    }
```

As we can see from above code, an instance of `JobHostConfiguration` is created when the constructor of `JobHost` is called. After that we set the instance of `JobHostConfiguration` to `_config`. So what is `JobHostConfiguration`?

#### 2.1.2 `JobHostConfiguration`
Let's look at the source code of class `JobHostConfiguration`.

```
    /// <summary>
    /// Represents the configuration settings for a <see cref="JobHost"/>.
    /// </summary>
    public sealed class JobHostConfiguration : IServiceProvider
    {
        ....
        private readonly ConcurrentDictionary<Type, object> _services = new ConcurrentDictionary<Type, object>();
        ....

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHostConfiguration"/> class.
        /// </summary>
        public JobHostConfiguration()
            : this(null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="JobHostConfiguration"/> class, using the
        /// specified connection string for both reading and writing data as well as Dashboard logging.
        /// </summary>
        /// <param name="dashboardAndStorageConnectionString">The Azure Storage connection string to use.
        /// <param name="configuration">A configuration object that will be used as the source of application settings.</param>
        /// </param>
        public JobHostConfiguration(string dashboardAndStorageConnectionString, IConfiguration configuration)
        {
            ....
            // add our built in services here
            AddService<IQueueConfiguration>(_queueConfiguration);
            AddService<IConsoleProvider>(ConsoleProvider);
            AddService<IStorageAccountProvider>(_storageAccountProvider);
            AddService<IExtensionRegistry>(extensions);
            AddService<StorageClientFactory>(new StorageClientFactory());
            AddService<INameResolver>(new DefaultNameResolver());
            AddService<IJobActivator>(DefaultJobActivator.Instance);
            AddService<ITypeLocator>(typeLocator);
            AddService<IConverterManager>(converterManager);
            AddService<IWebJobsExceptionHandler>(exceptionHandler);
            AddService<IFunctionResultAggregatorFactory>(new FunctionResultAggregatorFactory());
            ....
        }

        /// <summary>Gets or sets the job activator.</summary>
        /// <remarks>The job activator creates instances of job classes when calling instance methods.</remarks>
        public IJobActivator JobActivator
        {
            get
            {
                return GetService<IJobActivator>();
            }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException("value");
                }
                AddService<IJobActivator>(value);
            }
        }

        /// <summary>
        /// Adds the specified service instance, replacing any existing service.
        /// </summary>
        /// <typeparam name="TService">The service type</typeparam>
        /// <param name="serviceInstance">The service instance</param>
        public void AddService<TService>(TService serviceInstance)
        {
            AddService(typeof(TService), serviceInstance);
        }

        public void AddService(Type serviceType, object serviceInstance)
        {
            ....
            _services.AddOrUpdate(serviceType, serviceInstance, (key, existingValue) =>
            {
                // always replace existing values
                return serviceInstance;
            });
        }
    }
```

From above code, we can see `JobHostConfiguration` sets the storage account and adds necessary services Azure WebJobs needs including:
* JobActivator: creates instances of job classes when calling instance methods.
* TypeLocator: defines a locator that identifies types that may contain functions for `JobHost` to execute.
* NameResolver: defines a resolver for `name` variables in attribute values.
* ConverterManager: converts between types for parameter bindings. Parameter bindings call this to convert from user parameter types to underlying binding types.
* StorageClientFactory: creates Azure Storage clients.
* ....

#### 2.1.3 `RunAndBlock()`
We can see this path: `RunAndBlock()`->`Start()`->`StartAsync()`->`StartAsyncCore(cancellationToken)`as shown bellow, which is from source code [src/Microsoft.Azure.WebJobs.Host/JobHost.cs](https://github.com/Azure/azure-webjobs-sdk/blob/dev/src/Microsoft.Azure.WebJobs.Host/JobHost.cs).

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
            ....
            return StartAsyncCore(cancellationToken);
        }

        private async Task StartAsyncCore(CancellationToken cancellationToken)
        {
            await EnsureHostInitializedAsync(cancellationToken);
            await _listener.StartAsync(cancellationToken);
            ....
        }
```
We will look at these two lines of code `await EnsureHostInitializedAsync(cancellationToken)` and `await _listener.StartAsync(cancellationToken)` separately. These two lines of code initialize the host and start listener.

### 2.2 Initialize Host
Let's look at the source code of `EnsureHostInitializedAsync(CancellationToken cancellationToken)`.
```
        /// <summary>
        /// Ensure all required host services are initialized and the host is ready to start
        /// processing function invocations. This function does not start the listeners.
        /// If multiple threads call this, only one should do the initialization. The rest should wait.
        /// When this task is signalled, _context is initialized.
        /// </summary>
        private Task EnsureHostInitializedAsync(CancellationToken cancellationToken)
        {
            ....
            Task ignore = InitializeHostAsync(cancellationToken, tsc);
            ....
        }

        // Caller gaurantees this is single-threaded. 
        // Set initializationTask when complete, many threads can wait on that. 
        // When complete, the fields should be initialized to allow runtime usage. 
        private async Task InitializeHostAsync(CancellationToken cancellationToken, TaskCompletionSource<bool> initializationTask)
        {
            try
            {
                InitializeServices();

                var context = await _config.CreateJobHostContextAsync(_services, this, _shutdownTokenSource.Token, cancellationToken);

                // must call this BEFORE setting the results below
                // since listener startup is blocking on those members
                OnHostInitialized();

                _context = context;
                _listener = context.Listener;
                _logger = _context.LoggerFactory?.CreateLogger(LogCategories.Startup);

                initializationTask.SetResult(true);
            }
            ....
        }
```
`InitializeHostAsync(CancellationToken cancellationToken, TaskCompletionSource<bool> initializationTask)` does a few things:
* It calls `InitializeServices()` to ensure the static services are initialized.
* `_config`, whose type is `JobHostConfiguration`, calls `CreateJobHostContextAsync(_services, this, _shutdownTokenSource.Token, cancellationToken)` to create `context`, whose type is `JobHostContext`.
* It sets field `_context`, `_listener` and `_logger`. `_listener` is from line of code `CreateHostListener(hostListenerFactory, hostSharedQueue, heartbeatCommand, exceptionHandler, shutdownToken);` inside function`CreateJobHostContextAsync()`. So it's a `ShutdownListener`.

We will go over these three things one by one.

#### 2.2.1 `InitializeServices()`
```
        // Ensure the static services are initialized. 
        // These are derived from the underlying JobHostConfiguration. 
        // Caller ensures this is single threaded. 
        private void InitializeServices()
        {
            if (this._services != null)
            {
                return; // already Created 
            }

            var services = this._config.CreateStaticServices();

            _services = services;
        }
```
`_services` is an field of `JobHost` like this `private ServiceProviderWrapper _services`. These are services that are accessible without starting the execution container. They include the initial set of JobHostConfiguration services as well as additional services created. Additional services are created by `CreateStaticServices()`.

#### 2.2.2 `CreateJobHostContextAsync()`

Before we look at the function `CreateJobHostContextAsync()`, let's look at a few classes first:

##### Interface `IMethodInvoker`
It has four derived classes:
* `MethodInvokerWithReturnValue`
* `TaskMethodInvoker`
* `VoidMethodInvoker`
* `VoidTaskMethodInvoker`
```
    internal interface IMethodInvoker<TReflected, TReturnValue>
    {
        // The cancellation token, if any, is provided along with the other arguments.
        Task<TReturnValue> InvokeAsync(TReflected instance, object[] arguments);
    }

    internal class MethodInvokerWithReturnValue<TReflected, TReturnValue> : IMethodInvoker<TReflected, TReturnValue>
    {
        private readonly Func<TReflected, object[], TReturnValue> _lambda;

        public MethodInvokerWithReturnValue(Func<TReflected, object[], TReturnValue> lambda)
        {
            _lambda = lambda;
        }

        public Task<TReturnValue> InvokeAsync(TReflected instance, object[] arguments)
        {
            TReturnValue result = _lambda.Invoke(instance, arguments);
            return Task.FromResult(result);
        }
    }
    ....
```
##### Class `FunctionInvoker`
```
    internal class FunctionInvoker<TReflected, TReturnValue> : IFunctionInvoker
    {
        public FunctionInvoker(
            IReadOnlyList<string> parameterNames,
            IFactory<TReflected> instanceFactory,
            IMethodInvoker<TReflected, TReturnValue> methodInvoker)
        {
            ....
            _parameterNames = parameterNames;
            _instanceFactory = instanceFactory;
            _methodInvoker = methodInvoker;
        }

        public object CreateInstance()
        {
            TReflected instance = _instanceFactory.Create();
            return instance;
        }

        public async Task<object> InvokeAsync(object instance, object[] arguments)
        {
            ....
            return await _methodInvoker.InvokeAsync((TReflected) instance, arguments);            
        }
    }
```
##### Class `FunctionDescriptor`
This class represents an Azure WebJobs SDK function.
```
    public class FunctionDescriptor
    {
        public string Id { get; set; }
        public string FullName { get; set; }
        public string ShortName { get; set; }
        public IEnumerable<ParameterDescriptor> Parameters { get; set; }
        internal string LogName { get; set; }
        internal bool IsDisabled { get; set; }
        internal bool HasCancellationToken { get; set; }
        internal TriggerParameterDescriptor TriggerParameterDescriptor { get; set; }
        internal TimeoutAttribute TimeoutAttribute { get; set; }
        internal IEnumerable<SingletonAttribute> SingletonAttributes { get; set; }
        internal IEnumerable<IFunctionFilter> MethodLevelFilters { get; set; }
        internal IEnumerable<IFunctionFilter> ClassLevelFilters { get; set; }
    }
```
##### Class `FunctionInstance`
```
    internal class FunctionInstance : IFunctionInstance
    {
        private readonly Guid _id;
        private readonly Guid? _parentId;
        private readonly ExecutionReason _reason;
        private readonly IBindingSource _bindingSource;
        private readonly IFunctionInvoker _invoker;
        private readonly FunctionDescriptor _functionDescriptor;

        public FunctionInstance(Guid id, Guid? parentId, ExecutionReason reason, IBindingSource bindingSource,
            IFunctionInvoker invoker, FunctionDescriptor functionDescriptor)
        {
            _id = id;
            _parentId = parentId;
            _reason = reason;
            _bindingSource = bindingSource;
            _invoker = invoker;
            _functionDescriptor = functionDescriptor;
        }
        ....
    }
```
##### class `FunctionInstanceFactory`
`FunctionInstanceFactory` is used to create an instance of `FunctionInstance`.
```
    internal class FunctionInstanceFactory : IFunctionInstanceFactory
    {
        private readonly IFunctionBinding _binding;
        private readonly IFunctionInvoker _invoker;
        private readonly FunctionDescriptor _descriptor;

        public FunctionInstanceFactory(IFunctionBinding binding, IFunctionInvoker invoker, FunctionDescriptor descriptor)
        {
            _binding = binding;
            _invoker = invoker;
            _descriptor = descriptor;
        }

        public IFunctionInstance Create(FunctionInstanceFactoryContext context)
        {
            IBindingSource bindingSource = new BindingSource(_binding, context.Parameters);
            return new FunctionInstance(context.Id, context.ParentId, context.ExecutionReason, bindingSource, _invoker, _descriptor);
        }
    }
```
##### Class `FunctionDefinition`
```
    internal class FunctionDefinition : IFunctionDefinition
    {
        private readonly FunctionDescriptor _descriptor;
        private readonly IFunctionInstanceFactory _instanceFactory;
        private readonly IListenerFactory _listenerFactory;

        public FunctionDefinition(FunctionDescriptor descriptor, IFunctionInstanceFactory instanceFactory, IListenerFactory listenerFactory)
        {
            _descriptor = descriptor;
            _instanceFactory = instanceFactory;
            _listenerFactory = listenerFactory;
        }
    }
```
##### Class `FunctionExecutor`
```
    internal class FunctionExecutor : IFunctionExecutor
    {
        ....
        public FunctionExecutor(IFunctionInstanceLogger functionInstanceLogger, IFunctionOutputLogger functionOutputLogger,
                IWebJobsExceptionHandler exceptionHandler,
                IAsyncCollector<FunctionInstanceLogEntry> functionEventCollector = null,
                ILoggerFactory loggerFactory = null,
                IEnumerable<IFunctionFilter> globalFunctionFilters = null)
        {
            _functionInstanceLogger = functionInstanceLogger ?? throw new ArgumentNullException(nameof(functionInstanceLogger));
            _functionOutputLogger = functionOutputLogger ?? throw new ArgumentNullException(nameof(functionOutputLogger));
            _exceptionHandler = exceptionHandler ?? throw new ArgumentNullException(nameof(exceptionHandler));
            _functionEventCollector = functionEventCollector;
            _loggerFactory = loggerFactory;
            _resultsLogger = _loggerFactory?.CreateLogger(LogCategories.Results);
            _globalFunctionFilters = globalFunctionFilters ?? Enumerable.Empty<IFunctionFilter>();
        }

        public async Task<IDelayedException> TryExecuteAsync(IFunctionInstance functionInstance, CancellationToken cancellationToken)
        {
            ....
            using (parameterHelper)
            {
                try
                {
                    ....
                    functionStartedMessageId = await ExecuteWithLoggingAsync(functionInstance, functionStartedMessage, instanceLogEntry, parameterHelper, logger, cancellationToken);
                    ....
                }
                ....
            }
            ....
        }

        private async Task<string> ExecuteWithLoggingAsync(IFunctionInstance instance, FunctionStartedMessage message,
            FunctionInstanceLogEntry instanceLogEntry, ParameterHelper parameterHelper, ILogger logger, CancellationToken cancellationToken)
        {
            ....
            await ExecuteWithLoggingAsync(instance, parameterHelper, outputDefinition, logger, functionCancellationTokenSource);
            ....
        }

        private async Task ExecuteWithLoggingAsync(IFunctionInstance instance,
            ParameterHelper parameterHelper,
            IFunctionOutputDefinition outputDefinition,
            ILogger logger,
            CancellationTokenSource functionCancellationTokenSource)
        {
            ....

            try
            {
                await ExecuteWithWatchersAsync(instance, parameterHelper, logger, functionCancellationTokenSource);
                ....
            }
            ....
        }

        internal async Task ExecuteWithWatchersAsync(IFunctionInstance instance,
            ParameterHelper parameterHelper,
            ILogger logger,
            CancellationTokenSource functionCancellationTokenSource)
        {
            IFunctionInvoker invoker = instance.Invoker;
            ....

            object jobInstance = parameterHelper.JobInstance;
            using (CancellationTokenSource timeoutTokenSource = new CancellationTokenSource())
            {
                ....
                try
                {
                    var filters = GetFilters<IFunctionInvocationFilter>(_globalFunctionFilters, instance.FunctionDescriptor, jobInstance);

                    invoker = FunctionInvocationFilterInvoker.Create(invoker, filters, instance, parameterHelper, logger);

                    await InvokeAsync(invoker, parameterHelper, timeoutTokenSource, functionCancellationTokenSource,
                        throwOnTimeout, timerInterval, instance);
                }
                ....
            }
            ....
        }

        internal static async Task InvokeAsync(IFunctionInvoker invoker, ParameterHelper parameterHelper, CancellationTokenSource timeoutTokenSource,
            CancellationTokenSource functionCancellationTokenSource, bool throwOnTimeout, TimeSpan timerInterval, IFunctionInstance instance)
        {
            object[] invokeParameters = parameterHelper.InvokeParameters;
            ....
            Task<object> invokeTask = invoker.InvokeAsync(parameterHelper.JobInstance, invokeParameters);
            ....

            object returnValue = await invokeTask;

            parameterHelper.SetReturnValue(returnValue);
        }
    }
```
##### Class `TriggeredFunctionExecutor`
```
    internal class TriggeredFunctionExecutor<TTriggerValue> : ITriggeredFunctionExecutor
    {
        private FunctionDescriptor _descriptor;
        private ITriggeredFunctionInstanceFactory<TTriggerValue> _instanceFactory;
        private IFunctionExecutor _executor;

        public async Task<FunctionResult> TryExecuteAsync(TriggeredFunctionData input, CancellationToken cancellationToken)
        {
            var context = new FunctionInstanceFactoryContext<TTriggerValue>()
            {
                TriggerValue = (TTriggerValue)input.TriggerValue,
                ParentId = input.ParentId
            };

            if (input.InvokeHandler != null)
            {
                context.InvokeHandler = async next =>
                {
                    await input.InvokeHandler(next);

                    // NOTE: The InvokeHandler code path currently does not support flowing the return 
                    // value back to the trigger.
                    return null;
                };
            }

            IFunctionInstance instance = _instanceFactory.Create(context);
            IDelayedException exception = await _executor.TryExecuteAsync(instance, cancellationToken);

            FunctionResult result = exception != null ?
                new FunctionResult(exception.Exception)
                : new FunctionResult(true);

            return result;
        }
    }
```
##### Class `HeartbeatFunctionExecutor`
```
    internal class HeartbeatFunctionExecutor : IFunctionExecutor
    {
        private readonly IRecurrentCommand _heartbeatCommand;
        private readonly IWebJobsExceptionHandler _exceptionHandler;
        private readonly IFunctionExecutor _innerExecutor;

        public async Task<IDelayedException> TryExecuteAsync(IFunctionInstance instance, CancellationToken cancellationToken)
        {
            IDelayedException result;

            using (ITaskSeriesTimer timer = CreateHeartbeatTimer(_exceptionHandler))
            {
                await _heartbeatCommand.TryExecuteAsync(cancellationToken);
                timer.Start();

                result = await _innerExecutor.TryExecuteAsync(instance, cancellationToken);

                await timer.StopAsync(cancellationToken);
            }

            return result;
        }
    }
```
##### Class `AbortListenerFunctionExecutor`
```
    internal class AbortListenerFunctionExecutor : IFunctionExecutor
    {
        private readonly IListenerFactory _abortListenerFactory;
        private readonly IFunctionExecutor _innerExecutor;

        public async Task<IDelayedException> TryExecuteAsync(IFunctionInstance instance, CancellationToken cancellationToken)
        {
            IDelayedException result;

            using (IListener listener = await _abortListenerFactory.CreateAsync(cancellationToken))
            {
                await listener.StartAsync(cancellationToken);

                result = await _innerExecutor.TryExecuteAsync(instance, cancellationToken);

                await listener.StopAsync(cancellationToken);
            }

            return result;
        }
    }
```
##### Class `ShutdownFunctionExecutor`
```
    internal class ShutdownFunctionExecutor : IFunctionExecutor
    {
        private readonly CancellationToken _shutdownToken;
        private readonly IFunctionExecutor _innerExecutor;

        public async Task<IDelayedException> TryExecuteAsync(IFunctionInstance instance, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource callCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
                _shutdownToken, cancellationToken))
            {
                return await _innerExecutor.TryExecuteAsync(instance, callCancellationSource.Token);
            }
        }
    }
```
##### Class `QueueTriggerExecutor`
```
    internal class QueueTriggerExecutor : ITriggerExecutor<IStorageQueueMessage>
    {
        private readonly ITriggeredFunctionExecutor _innerExecutor;

        public QueueTriggerExecutor(ITriggeredFunctionExecutor innerExecutor)
        {
            _innerExecutor = innerExecutor;
        }

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
##### Class `FunctionIndex`
```
    internal interface IFunctionIndexLookup
    {
        IFunctionDefinition Lookup(string functionId);

        IFunctionDefinition Lookup(MethodInfo method);

        // This uses the function's short name ("Class.Method"), which can also be overriden
        // by the FunctionName attribute. 
        IFunctionDefinition LookupByName(string name);
    }

    internal interface IFunctionIndex : IFunctionIndexLookup
    {
        IEnumerable<IFunctionDefinition> ReadAll();

        IEnumerable<FunctionDescriptor> ReadAllDescriptors();

        IEnumerable<MethodInfo> ReadAllMethods();
    }

    internal class FunctionIndex : IFunctionIndex, IFunctionIndexCollector
    {
        private readonly IDictionary<string, IFunctionDefinition> _functionsById;
        private readonly IDictionary<MethodInfo, IFunctionDefinition> _functionsByMethod;
        private readonly ICollection<FunctionDescriptor> _functionDescriptors;

        public FunctionIndex()
        {
            _functionsById = new Dictionary<string, IFunctionDefinition>();
            _functionsByMethod = new Dictionary<MethodInfo, IFunctionDefinition>();
            _functionDescriptors = new List<FunctionDescriptor>();
        }

        public void Add(IFunctionDefinition function, FunctionDescriptor descriptor, MethodInfo method)
        {
            string id = descriptor.Id;

            if (_functionsById.ContainsKey(id))
            {
                throw new InvalidOperationException("Method overloads are not supported. " +
                    "There are multiple methods with the name '" + id + "'.");
            }

            _functionsById.Add(id, function);
            _functionsByMethod.Add(method, function);
            _functionDescriptors.Add(descriptor);
        }
    }
```
##### Interface `ITriggerBinding`
```
    /// <summary>
    /// Interface defining a trigger parameter binding.
    /// </summary>
    public interface ITriggerBinding
    {
        /// <summary>
        /// The trigger value type that this binding binds to.
        /// </summary>
        Type TriggerValueType { get; }

        /// <summary>
        /// Gets the binding data contract.
        /// </summary>
        IReadOnlyDictionary<string, Type> BindingDataContract { get; }

        /// <summary>
        /// Perform a bind to the specified value using the specified binding context.
        /// </summary>
        /// <param name="value">The value to bind to. 
        /// This is commonly passed from the listener via <see cref="ITriggeredFunctionExecutor.TryExecuteAsync(TriggeredFunctionData, System.Threading.CancellationToken)"/>  </param>
        /// <param name="context">The binding context.</param>
        /// <returns>A task that returns the <see cref="ITriggerData"/> for the binding.</returns>
        Task<ITriggerData> BindAsync(object value, ValueBindingContext context);

        /// <summary>
        /// Creates a <see cref="IListener"/> for the trigger parameter.
        /// </summary>
        /// <param name="context">The <see cref="ListenerFactoryContext"/> to use.</param>
        /// <returns>The <see cref="IListener"/>.</returns>
        Task<IListener> CreateListenerAsync(ListenerFactoryContext context);

        /// <summary>
        /// Get a description of the binding.
        /// </summary>
        /// <returns>The <see cref="ParameterDescriptor"/></returns>
        ParameterDescriptor ToParameterDescriptor();
    }
```
##### Class `QueueTriggerBinding`
```
    internal class QueueTriggerBinding : ITriggerBinding
    {
        private readonly string _parameterName;
        private readonly IStorageQueue _queue;
        private readonly ITriggerDataArgumentBinding<IStorageQueueMessage> _argumentBinding;
        private readonly IReadOnlyDictionary<string, Type> _bindingDataContract;
        private readonly IQueueConfiguration _queueConfiguration;
        private readonly IWebJobsExceptionHandler _exceptionHandler;
        private readonly IContextSetter<IMessageEnqueuedWatcher> _messageEnqueuedWatcherSetter;
        private readonly ISharedContextProvider _sharedContextProvider;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IObjectToTypeConverter<IStorageQueueMessage> _converter;

        ....
        public async Task<ITriggerData> BindAsync(object value, ValueBindingContext context)
        {
            IStorageQueueMessage message = null;

            if (!_converter.TryConvert(value, out message))
            {
                throw new InvalidOperationException("Unable to convert trigger to IStorageQueueMessage.");
            }

            ITriggerData triggerData = await _argumentBinding.BindAsync(message, context);
            IReadOnlyDictionary<string, object> bindingData = CreateBindingData(message, triggerData.BindingData);

            return new TriggerData(triggerData.ValueProvider, bindingData);
        }

        public Task<IListener> CreateListenerAsync(ListenerFactoryContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            var factory = new QueueListenerFactory(_queue, _queueConfiguration, _exceptionHandler,
                    _messageEnqueuedWatcherSetter, _sharedContextProvider, _loggerFactory, context.Executor);

            return factory.CreateAsync(context.CancellationToken);
        }
    }
```
##### Class `QueueListener`
```
    internal sealed class QueueListener : IListener, ITaskSeriesCommand, INotificationCommand
    {
        private readonly ITaskSeriesTimer _timer;
        private readonly IDelayStrategy _delayStrategy;
        private readonly IStorageQueue _queue;
        private readonly IStorageQueue _poisonQueue;
        private readonly ITriggerExecutor<IStorageQueueMessage> _triggerExecutor;
        private readonly IWebJobsExceptionHandler _exceptionHandler;
        private readonly IMessageEnqueuedWatcher _sharedWatcher;
        private readonly List<Task> _processing = new List<Task>();
        private readonly object _stopWaitingTaskSourceLock = new object();
        private readonly IQueueConfiguration _queueConfiguration;
        private readonly QueueProcessor _queueProcessor;
        private readonly TimeSpan _visibilityTimeout;

        private bool _foundMessageSinceLastDelay;
        private bool _disposed;
        private TaskCompletionSource<object> _stopWaitingTaskSource;

        // for testing
        internal TimeSpan MinimumVisibilityRenewalInterval { get; set; } = TimeSpan.FromMinutes(1);

        public Task StartAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            _timer.Start();
            return Task.FromResult(0);
        }

        public async Task<TaskSeriesCommandResult> ExecuteAsync(CancellationToken cancellationToken)
        {
            ....
            IEnumerable<IStorageQueueMessage> batch;
            try
            {
                batch = await _queue.GetMessagesAsync(_queueProcessor.BatchSize,
                    _visibilityTimeout,
                    options: null,
                    operationContext: null,
                    cancellationToken: cancellationToken);
            }
            ....

            if (batch == null)
            {
                return CreateBackoffResult();
            }

            bool foundMessage = false;
            foreach (var message in batch)
            {
                ....
                Task task = ProcessMessageAsync(message, _visibilityTimeout, cancellationToken);
                _processing.Add(task);
            }
            ....

            _foundMessageSinceLastDelay = true;
            return CreateSucceededResult();
        }

        internal async Task ProcessMessageAsync(IStorageQueueMessage message, TimeSpan visibilityTimeout, CancellationToken cancellationToken)
        {
            try
            {
                ....
                FunctionResult result = null;
                using (ITaskSeriesTimer timer = CreateUpdateMessageVisibilityTimer(_queue, message, visibilityTimeout, _exceptionHandler))
                {
                    timer.Start();

                    result = await _triggerExecutor.ExecuteAsync(message, cancellationToken);

                    await timer.StopAsync(cancellationToken);
                }

                await _queueProcessor.CompleteProcessingMessageAsync(message.SdkObject, result, cancellationToken);
            }
            ....
        }
    }
```
##### Class `QueueListenerFactory`
```
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
##### Class `TriggerWrapper`
```
        // Wrapper for leveraging existing input pipeline and converter manager to get a ValueProvider.
        // Forwards all other calls to the inner binding. 
        class TriggerWrapper : ITriggerBinding
        {
            private readonly ITriggerBinding _inner;
            private readonly IBinding _binding;

            public Task<IListener> CreateListenerAsync(ListenerFactoryContext context)
            {
                return _inner.CreateListenerAsync(context);
            }
        }
```
##### Class `ListenerFactoryContext`
```
    /// <summary>
    /// Context object used passed to <see cref="ITriggerBinding.CreateListenerAsync"/>.
    /// </summary>
    public class ListenerFactoryContext
    {
        private readonly SharedQueueHandler _sharedQueue;
        private IDispatchQueueHandler _dispatchQueue;

        /// <summary>
        /// Return a queue that user can add function triggering messages,
        /// These messages will later be dequeued and processed by the registered handler
        /// </summary>
        /// <param name="handler">
        /// Call back function that handles messages in queue, an implementation of 
        /// <see cref="IMessageHandler.TryExecuteAsync(Newtonsoft.Json.Linq.JObject, System.Threading.CancellationToken)"/>
        /// If you have registered once, you can retrieve the same queue by passing a null to this function
        /// </param>
        /// <returns> The <see cref="IDispatchQueueHandler"/> is used to enqueue messages </returns>
        public IDispatchQueueHandler GetDispatchQueue(IMessageHandler handler)
        {
            if (_dispatchQueue == null)
            {
                ....
                if (_sharedQueue.RegisterHandler(Descriptor.Id, handler))
                {
                    _dispatchQueue = new DispatchQueueHandler(_sharedQueue, Descriptor.Id);
                }
                else
                {
                    // failed to register messageHandler, fall back to in memory implementation
                    _dispatchQueue = new InMemoryDispatchQueueHandler(handler);
                }
            }
            ....
            return _dispatchQueue;
        }
    }
```
##### Class `ListenerFactory`
```
        private class ListenerFactory : IListenerFactory
        {
            private readonly FunctionDescriptor _descriptor;
            private readonly ITriggeredFunctionExecutor _executor;
            private readonly ITriggerBinding _binding;
            private readonly SharedQueueHandler _sharedQueue;

            public ListenerFactory(FunctionDescriptor descriptor, ITriggeredFunctionExecutor executor, ITriggerBinding binding, SharedQueueHandler sharedQueue)
            {
                _descriptor = descriptor;
                _executor = executor;
                _binding = binding;
                _sharedQueue = sharedQueue;
            }

            public async Task<IListener> CreateAsync(CancellationToken cancellationToken)
            {
                ListenerFactoryContext context = new ListenerFactoryContext(_descriptor, _executor, _sharedQueue, cancellationToken);
                return await _binding.CreateListenerAsync(context);
            }
        }
```
##### Class `MethodInvokerFactory`
`MethodInvokerFactory.Create<TReflected, TReturnValue>(method)` uses:
* [ParameterExpression](https://msdn.microsoft.com/en-us/library/system.linq.expressions.parameterexpression(v=vs.110).aspx) represents a named parameter expression.
* [Expression](https://msdn.microsoft.com/en-us/library/system.linq.expressions.expression(v=vs.110).aspx.) provides the base class from which the classes that represent expression tree nodes are derived.
* [BlockExpression](https://msdn.microsoft.com/en-us/library/system.linq.expressions.blockexpression(v=vs.110).aspx) represents a block that contains a sequence of expressions where variables can be defined.
* [LambdaExpression](https://msdn.microsoft.com/en-us/library/system.linq.expressions.lambdaexpression(v=vs.110).aspx) describes a lambda expression. This captures a block of code that is similar to a .NET method body.
* [LambdaExpression.Compile()](https://msdn.microsoft.com/en-us/library/bb356928(v=vs.110).aspx) produces a delegate that represents the lambda expression.
```
    internal static class MethodInvokerFactory
    {
        public static IMethodInvoker<TReflected, TReturnValue> Create<TReflected, TReturnValue>(MethodInfo method)
        {
            ....
            // Parameter to invoker: TReflected instance
            ParameterExpression instanceParameter = Expression.Parameter(typeof(TReflected), "instance");

            // Parameter to invoker: object[] arguments
            ParameterExpression argumentsParameter = Expression.Parameter(typeof(object[]), "arguments");

            // Local variables passed as arguments to Call
            List<ParameterExpression> localVariables = new List<ParameterExpression>();

            // Pre-Call, copy from arguments array to local variables.
            List<Expression> arrayToLocalsAssignments = new List<Expression>();

            // Post-Call, copy from local variables back to arguments array.
            List<Expression> localsToArrayAssignments = new List<Expression>();

            // If the method returns a value: T returnValue
            ParameterExpression returnValue;

            Type returnType = method.ReturnType;
            if (returnType == typeof(void))
            {
                returnValue = null;
            }
            else
            {
                returnValue = Expression.Parameter(returnType);
            }

            ParameterInfo[] parameterInfos = method.GetParameters();
            Debug.Assert(parameterInfos != null);

            for (int index = 0; index < parameterInfos.Length; index++)
            {
                ParameterInfo parameterInfo = parameterInfos[index];
                Type argumentType = parameterInfo.ParameterType;

                if (argumentType.IsByRef)
                {
                    // The type of the local variable (and object in the arguments array) should be T rather than T&.
                    argumentType = argumentType.GetElementType();
                }

                // T argumentN
                ParameterExpression localVariable = Expression.Parameter(argumentType);
                localVariables.Add(localVariable);

                // arguments[index]
                Expression arrayAccess = Expression.ArrayAccess(argumentsParameter, Expression.Constant(index));

                // Pre-Call:
                // T argumentN = (T)arguments[index];
                Expression arrayAccessAsT = Expression.Convert(arrayAccess, argumentType);
                Expression assignArrayToLocal = Expression.Assign(localVariable, arrayAccessAsT);
                arrayToLocalsAssignments.Add(assignArrayToLocal);

                // Post-Call:
                // arguments[index] = (object)argumentN;
                Expression localAsObject = Expression.Convert(localVariable, typeof(object));
                Expression assignLocalToArray = Expression.Assign(arrayAccess, localAsObject);
                localsToArrayAssignments.Add(assignLocalToArray);
            }

            Expression callInstance;

            if (method.IsStatic)
            {
                callInstance = null;
            }
            else
            {
                callInstance = instanceParameter;
            }

            // Instance call:
            // instance.method(param0, param1, ...);
            // Static call:
            // method(param0, param1, ...);
            Expression call = Expression.Call(callInstance, method, localVariables);
            Expression callResult;

            if (returnType == typeof(void))
            {
                callResult = call;
            }
            else
            {
                // T returnValue = method(param0, param1, ...);
                callResult = Expression.Assign(returnValue, call);
            }

            List<Expression> blockExpressions = new List<Expression>();
            // T0 argument0 = (T0)arguments[0];
            // T1 argument1 = (T1)arguments[1];
            // ...
            blockExpressions.AddRange(arrayToLocalsAssignments);
            // Call(argument0, argument1, ...);
            // or
            // T returnValue = Call(param0, param1, ...);
            blockExpressions.Add(callResult);
            // arguments[0] = (object)argument0;
            // arguments[1] = (object)argument1;
            // ...
            blockExpressions.AddRange(localsToArrayAssignments);

            if (returnValue != null)
            {
                // return returnValue;
                blockExpressions.Add(returnValue);
            }

            List<ParameterExpression> blockVariables = new List<ParameterExpression>();
            blockVariables.AddRange(localVariables);

            if (returnValue != null)
            {
                blockVariables.Add(returnValue);
            }

            Expression block = Expression.Block(blockVariables, blockExpressions);

            if (call.Type == typeof(void))
            {
                // for: public void JobMethod()
                var lambda = Expression.Lambda<Action<TReflected, object[]>>(
                    block,
                    instanceParameter,
                    argumentsParameter);
                Action<TReflected, object[]> compiled = lambda.Compile();
                return new VoidMethodInvoker<TReflected, TReturnValue>(compiled);
            }
            else if (call.Type == typeof(Task))
            {
                // for: public Task JobMethod()
                var lambda = Expression.Lambda<Func<TReflected, object[], Task>>(
                    block,
                    instanceParameter,
                    argumentsParameter);
                Func<TReflected, object[], Task> compiled = lambda.Compile();
                return new VoidTaskMethodInvoker<TReflected, TReturnValue>(compiled);
            }
            else if (typeof(Task).IsAssignableFrom(call.Type))
            {
                // for: public Task<TReturnValue> JobMethod()
                var lambda = Expression.Lambda<Func<TReflected, object[], Task<TReturnValue>>>(
                    block,
                    instanceParameter,
                    argumentsParameter);
                Func<TReflected, object[], Task<TReturnValue>> compiled = lambda.Compile();
                return new TaskMethodInvoker<TReflected, TReturnValue>(compiled);
            }
            else
            {
                // for: public TReturnValue JobMethod()
                var lambda = Expression.Lambda<Func<TReflected, object[], TReturnValue>>(
                    block,
                    instanceParameter,
                    argumentsParameter);
                Func<TReflected, object[], TReturnValue> compiled = lambda.Compile();
                return new MethodInvokerWithReturnValue<TReflected, TReturnValue>(compiled);
            }
        }
    }
```
##### Class `FunctionInvokerFactory`
```
    internal static class FunctionInvokerFactory
    {
        public static IFunctionInvoker Create(MethodInfo method, IJobActivator activator)
        {

            Type reflectedType = method.ReflectedType;
            MethodInfo genericMethodDefinition = typeof(FunctionInvokerFactory).GetMethod("CreateGeneric",
                BindingFlags.NonPublic | BindingFlags.Static);
            Debug.Assert(genericMethodDefinition != null);

            Type returnType;
            if (!TypeUtility.TryGetReturnType(method, out returnType))
            {
                returnType = typeof(object);
            }

            MethodInfo genericMethod = genericMethodDefinition.MakeGenericMethod(reflectedType, returnType);
            Debug.Assert(genericMethod != null);
            Func<MethodInfo, IJobActivator, IFunctionInvoker> lambda =
                (Func<MethodInfo, IJobActivator, IFunctionInvoker>)Delegate.CreateDelegate(
                typeof(Func<MethodInfo, IJobActivator, IFunctionInvoker>), genericMethod);
            return lambda.Invoke(method, activator);
        }

        private static IFunctionInvoker CreateGeneric<TReflected, TReturnValue>(
            MethodInfo method,
            IJobActivator activator)
        {
            Debug.Assert(method != null);

            List<string> parameterNames = method.GetParameters().Select(p => p.Name).ToList();

            IMethodInvoker<TReflected, TReturnValue> methodInvoker = MethodInvokerFactory.Create<TReflected, TReturnValue>(method);

            IFactory<TReflected> instanceFactory = CreateInstanceFactory<TReflected>(method, activator);

            return new FunctionInvoker<TReflected, TReturnValue>(parameterNames, instanceFactory, methodInvoker);
        }
    }
```
##### Class `FunctionIndexer`
There are different bindiings:
* Trigger Bindings are bindings that monitor external event sources and cause a job function to be executed when they occur. An example is the QueueTriggerAttribute binding (e.g. [QueueTrigger("myqueue")]).
 * Non-Trigger Bindings are bindings to an external storage system. An example is the TableAttribute binding (e.g. [Table("mytable")]) which allows a job function to read/write to an Azure Storage Table.

```
    internal class FunctionIndexer
    {
        public const string ReturnParamName = "$return";

        private static readonly BindingFlags PublicMethodFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        private readonly ITriggerBindingProvider _triggerBindingProvider;
        private readonly IBindingProvider _bindingProvider;
        private readonly IJobActivator _activator;
        private readonly INameResolver _nameResolver;
        private readonly IFunctionExecutor _executor;
        private readonly HashSet<Assembly> _jobAttributeAssemblies;
        private readonly SingletonManager _singletonManager;
        private readonly ILogger _logger;
        private readonly SharedQueueHandler _sharedQueue;
        private readonly TimeoutAttribute _defaultTimeout;

        public async Task IndexTypeAsync(Type type, IFunctionIndexCollector index, CancellationToken cancellationToken)
        {
            foreach (MethodInfo method in type.GetMethods(PublicMethodFlags).Where(IsJobMethod))
            {
                try
                {
                    await IndexMethodAsync(method, index, cancellationToken);
                }
                ....
            }
        }

        public async Task IndexMethodAsync(MethodInfo method, IFunctionIndexCollector index, CancellationToken cancellationToken)
        {
            try
            {
                await IndexMethodAsyncCore(method, index, cancellationToken);
            }
            ....
        }

        internal async Task IndexMethodAsyncCore(MethodInfo method, IFunctionIndexCollector index, CancellationToken cancellationToken)
        {
            ....
            ITriggerBinding triggerBinding = null;
            ParameterInfo triggerParameter = null;
            IEnumerable<ParameterInfo> parameters = method.GetParameters();

            // set trigger triggerBinding and triggerParameter for this method
            foreach (ParameterInfo parameter in parameters)
            {
                ....
            }

            Dictionary<string, IBinding> nonTriggerBindings = new Dictionary<string, IBinding>();
            IReadOnlyDictionary<string, Type> bindingDataContract;

            // set triggerBinding as TriggerWrapper;
            if (triggerBinding != null)
            {
                ....
            }

            ....
            ReturnParameterInfo returnParameter = null;

            // set returnParameter and add it to parameters
            if (TypeUtility.TryGetReturnType(method, out Type methodReturnType))
            {
                ....
            }

            // set nonTriggerBindings
            foreach (ParameterInfo parameter in parameters)
            {
                ....
                nonTriggerBindings.Add(parameter.Name, binding);
            }

            ....
            string triggerParameterName = triggerParameter != null ? triggerParameter.Name : null;
            FunctionDescriptor functionDescriptor = CreateFunctionDescriptor(method, triggerParameterName, triggerBinding, nonTriggerBindings);
            IFunctionInvoker invoker = FunctionInvokerFactory.Create(method, _activator);
            IFunctionDefinition functionDefinition;

            if (triggerBinding != null)
            {
                Type triggerValueType = triggerBinding.TriggerValueType;
                var methodInfo = typeof(FunctionIndexer).GetMethod("CreateTriggeredFunctionDefinition", BindingFlags.Instance | BindingFlags.NonPublic).MakeGenericMethod(triggerValueType);
                functionDefinition = (FunctionDefinition)methodInfo.Invoke(this, new object[] { triggerBinding, triggerParameterName, functionDescriptor, nonTriggerBindings, invoker });
                ....
            }
            ....

            index.Add(functionDefinition, functionDescriptor, method);
        }

        private FunctionDefinition CreateTriggeredFunctionDefinition<TTriggerValue>(
            ITriggerBinding triggerBinding, string parameterName, FunctionDescriptor descriptor,
            IReadOnlyDictionary<string, IBinding> nonTriggerBindings, IFunctionInvoker invoker)
        {
            ITriggeredFunctionBinding<TTriggerValue> functionBinding = new TriggeredFunctionBinding<TTriggerValue>(descriptor, parameterName, triggerBinding, nonTriggerBindings, _singletonManager);
            ITriggeredFunctionInstanceFactory<TTriggerValue> instanceFactory = new TriggeredFunctionInstanceFactory<TTriggerValue>(functionBinding, invoker, descriptor);
            ITriggeredFunctionExecutor triggerExecutor = new TriggeredFunctionExecutor<TTriggerValue>(descriptor, _executor, instanceFactory);
            IListenerFactory listenerFactory = new ListenerFactory(descriptor, triggerExecutor, triggerBinding, _sharedQueue);

            return new FunctionDefinition(descriptor, instanceFactory, listenerFactory);
        }
    }
```
##### Class `FunctionIndexProvider`
```
    internal class FunctionIndexProvider : IFunctionIndexProvider
    {
        private readonly ITypeLocator _typeLocator;
        private readonly ITriggerBindingProvider _triggerBindingProvider;
        private readonly IBindingProvider _bindingProvider;
        private readonly IJobActivator _activator;
        private readonly IFunctionExecutor _executor;
        private readonly IExtensionRegistry _extensions;
        private readonly SingletonManager _singletonManager;
        private readonly ILoggerFactory _loggerFactory;
        private readonly SharedQueueHandler _sharedQueue;
        private readonly TimeoutAttribute _defaultTimeout;
        private IFunctionIndex _index;

        ....
        public async Task<IFunctionIndex> GetAsync(CancellationToken cancellationToken)
        {
            if (_index == null)
            {
                _index = await CreateAsync(cancellationToken);
            }

            return _index;
        }

        private async Task<IFunctionIndex> CreateAsync(CancellationToken cancellationToken)
        {
            FunctionIndex index = new FunctionIndex();
            FunctionIndexer indexer = new FunctionIndexer(_triggerBindingProvider, _bindingProvider, _activator, _executor, _extensions, _singletonManager, _loggerFactory, null, _sharedQueue);
            IReadOnlyList<Type> types = _typeLocator.GetTypes();

            foreach (Type type in types)
            {
                await indexer.IndexTypeAsync(type, index, cancellationToken);
            }

            return index;
        }
    }
```
##### Class `ListenerFactoryListener`
```
    internal class ListenerFactoryListener : IListener
    {
        private readonly IListenerFactory _factory;
        private readonly SharedQueueHandler _sharedQueue;
        private readonly CancellationTokenSource _cancellationSource;

        private IListener _listener;
        private CancellationTokenRegistration _cancellationRegistration;
        private bool _disposed;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (_listener != null)
            {
                throw new InvalidOperationException("The listener has already been started.");
            }

            return StartAsyncCore(cancellationToken);
        }

        private async Task StartAsyncCore(CancellationToken cancellationToken)
        {
            // create sharedQueue so that once the listener started, they can enqueue
            await _sharedQueue.InitializeAsync(cancellationToken);
            _listener = await _factory.CreateAsync(cancellationToken);
            _cancellationRegistration = _cancellationSource.Token.Register(_listener.Cancel);
            await _listener.StartAsync(cancellationToken); // composite listener, startAsync in parallel
            // start sharedQueue after other listeners
            await _sharedQueue.StartQueueAsync(cancellationToken);
        }
    }

```
##### Class `HeartbeatListener`
```
    internal class HeartbeatListener : IListener
    {
        private readonly IRecurrentCommand _heartbeatCommand;
        private readonly IListener _innerListener;
        private readonly ITaskSeriesTimer _timer;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _innerListener.StartAsync(cancellationToken);

            await _heartbeatCommand.TryExecuteAsync(cancellationToken);
            _timer.Start();
        }
    }
```
##### Class `ShutdownListener`
```
    internal class ShutdownListener : IListener
    {
        private readonly CancellationToken _shutdownToken;
        private readonly CancellationTokenRegistration _shutdownRegistration;
        private readonly IListener _innerListener;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using (CancellationTokenSource combinedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _shutdownToken))
            {
                await _innerListener.StartAsync(combinedCancellationSource.Token);
            }
        }
    }
```
##### Class `CompositeListenerFactory`
```
    internal sealed class CompositeListenerFactory : IListenerFactory
    {
        private readonly IEnumerable<IListenerFactory> _listenerFactories;

        public async Task<IListener> CreateAsync(CancellationToken cancellationToken)
        {
            List<IListener> listeners = new List<IListener>();

            foreach (IListenerFactory listenerFactory in _listenerFactories)
            {
                IListener listener = await listenerFactory.CreateAsync(cancellationToken);
                listeners.Add(listener);
            }

            return new CompositeListener(listeners);
        }
    }
```
##### Class `HostListenerFactory`
```
    internal class HostListenerFactory : IListenerFactory
    {
        private static readonly MethodInfo JobActivatorCreateMethod = typeof(IJobActivator).GetMethod("CreateInstance", BindingFlags.Public | BindingFlags.Instance).GetGenericMethodDefinition();
        private const string IsDisabledFunctionName = "IsDisabled";
        private readonly IEnumerable<IFunctionDefinition> _functionDefinitions;
        private readonly SingletonManager _singletonManager;
        private readonly IJobActivator _activator;
        private readonly INameResolver _nameResolver;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;

        public async Task<IListener> CreateAsync(CancellationToken cancellationToken)
        {
            List<IListener> listeners = new List<IListener>();

            foreach (IFunctionDefinition functionDefinition in _functionDefinitions)
            {
                // Determine if the function is disabled
                if (functionDefinition.Descriptor.IsDisabled)
                {
                    string msg = string.Format("Function '{0}' is disabled", functionDefinition.Descriptor.ShortName);
                    _logger?.LogInformation(msg);
                    continue;
                }

                IListenerFactory listenerFactory = functionDefinition.ListenerFactory;
                if (listenerFactory == null)
                {
                    continue;
                }

                IListener listener = await listenerFactory.CreateAsync(cancellationToken);

                // if the listener is a Singleton, wrap it with our SingletonListener
                SingletonAttribute singletonAttribute = SingletonManager.GetListenerSingletonOrNull(listener.GetType(), functionDefinition.Descriptor);
                if (singletonAttribute != null)
                {
                    listener = new SingletonListener(functionDefinition.Descriptor, singletonAttribute, _singletonManager, listener, _loggerFactory);
                }

                // wrap the listener with a function listener to handle exceptions
                listener = new FunctionListener(listener, functionDefinition.Descriptor, _loggerFactory);
                listeners.Add(listener);
            }

            return new CompositeListener(listeners);
        }
    }
```
##### Class `CompositeListenerFactory`
```
    internal sealed class CompositeListenerFactory : IListenerFactory
    {
        private readonly IEnumerable<IListenerFactory> _listenerFactories;

        public async Task<IListener> CreateAsync(CancellationToken cancellationToken)
        {
            List<IListener> listeners = new List<IListener>();

            foreach (IListenerFactory listenerFactory in _listenerFactories)
            {
                IListener listener = await listenerFactory.CreateAsync(cancellationToken);
                listeners.Add(listener);
            }

            return new CompositeListener(listeners);
        }
    }
```
##### Class `JobHostContext`
```
    // JobHostContext are the fields that a JobHost needs to operate at runtime. 
    // This is created from a JobHostConfiguration. 
    internal sealed class JobHostContext : IDisposable
    {
        private readonly IFunctionIndexLookup _functionLookup;
        private readonly IFunctionExecutor _executor;
        private readonly IListener _listener;
        private readonly IAsyncCollector<FunctionInstanceLogEntry> _functionEventCollector; // optional        
        private readonly ILoggerFactory _loggerFactory;
        ....
        public JobHostContext(IFunctionIndexLookup functionLookup,
            IFunctionExecutor executor,
            IListener listener,
            IAsyncCollector<FunctionInstanceLogEntry> functionEventCollector = null,
            ILoggerFactory loggerFactory = null)
        {
            _functionLookup = functionLookup;
            _executor = executor;
            _listener = listener;
            _functionEventCollector = functionEventCollector;
            _loggerFactory = loggerFactory;
        }
        ....
    }
```

Let's look at `CreateJobHostContextAsync`. The important part is shown bellow:
```
        // Do the full runtime intitialization. This includes static initialization. 
        // This mainly means:
        // - indexing the functions 
        // - spinning up the listeners (so connecting to the services)
        public static async Task<JobHostContext> CreateJobHostContextAsync(
            this JobHostConfiguration config,
            ServiceProviderWrapper services, // Results from first phase
            JobHost host,
            CancellationToken shutdownToken,
            CancellationToken cancellationToken)
        {
            // get necessary services first
            ....

                IFunctionIndex functions = await functionIndexProvider.GetAsync(combinedCancellationToken);
                IListenerFactory functionsListenerFactory = new HostListenerFactory(functions.ReadAll(), singletonManager, activator, nameResolver, loggerFactory);

                IFunctionExecutor hostCallExecutor;
                IListener listener;
                ....

                if (dashboardAccount == null)
                {
                    ....
                }
                else
                {
                    ....
                    HeartbeatDescriptor heartbeatDescriptor = new HeartbeatDescriptor
                    {
                        SharedContainerName = HostContainerNames.Hosts,
                        SharedDirectoryName = HostDirectoryNames.Heartbeats + "/" + hostId,
                        InstanceBlobName = hostInstanceId.ToString("N"),
                        ExpirationInSeconds = (int)HeartbeatIntervals.ExpirationInterval.TotalSeconds
                    };

                    IStorageBlockBlob blob = dashboardAccount.CreateBlobClient()
                        .GetContainerReference(heartbeatDescriptor.SharedContainerName)
                        .GetBlockBlobReference(heartbeatDescriptor.SharedDirectoryName + "/" + heartbeatDescriptor.InstanceBlobName);
                    IRecurrentCommand heartbeatCommand = new UpdateHostHeartbeatCommand(new HeartbeatCommand(blob));

                    hostCallExecutor = CreateHostCallExecutor(instanceQueueListenerFactory, heartbeatCommand,
                        exceptionHandler, shutdownToken, functionExecutor);
                    IListenerFactory hostListenerFactory = new CompositeListenerFactory(functionsListenerFactory,
                        sharedQueueListenerFactory, instanceQueueListenerFactory);
                    listener = CreateHostListener(hostListenerFactory, hostSharedQueue, heartbeatCommand, exceptionHandler, shutdownToken);
                    ....
                }
                ....

                return new JobHostContext(
                    functions,
                    hostCallExecutor,
                    listener,
                    functionEventCollector,
                    loggerFactory);
            }
        }
```

* `IFunctionIndex functions = await functionIndexProvider.GetAsync(combinedCancellationToken);`
    - `functionIndexProvider` calls `GetAsync(combinedCancellationToken)` to get `FunctionIndex`.` GetAsync()`-> `CreateAsync()` -> `IndexTypeAsync()` -> `IndexMethodAsync()` -> `IndexMethodAsyncCore()`. This line of code prepares all the functions. It know the name, the methodinfo, the parameters and so on.
* `IListenerFactory functionsListenerFactory = new HostListenerFactory(functions.ReadAll(), singletonManager, activator, nameResolver, loggerFactory);`
    - Create `HostListenerFactory` with the `functionDefinitions`
* `hostCallExecutor = CreateHostCallExecutor(instanceQueueListenerFactory, heartbeatCommand, exceptionHandler, shutdownToken, functionExecutor);`
    - Create `HeartbeatFunctionExecutor`, `AbortListenerFunctionExecutor` and `ShutdownFunctionExecutor`. Returns `ShutdownFunctionExecutor`.
* `IListenerFactory hostListenerFactory = new CompositeListenerFactory(functionsListenerFactory, sharedQueueListenerFactory, instanceQueueListenerFactory);`
* `listener = CreateHostListener(hostListenerFactory, hostSharedQueue, heartbeatCommand, exceptionHandler, shutdownToken);`
    - Create `ListenerFactoryListener`. `HeartbeatListener` and `ShutdownListener`. Returns `ShutdownListener`.
    ```
            private static IListener CreateHostListener(IListenerFactory allFunctionsListenerFactory, SharedQueueHandler sharedQueue,
            IRecurrentCommand heartbeatCommand, IWebJobsExceptionHandler exceptionHandler,
            CancellationToken shutdownToken)
        {
            IListener factoryListener = new ListenerFactoryListener(allFunctionsListenerFactory, sharedQueue);
            IListener heartbeatListener = new HeartbeatListener(heartbeatCommand, exceptionHandler, factoryListener);
            IListener shutdownListener = new ShutdownListener(shutdownToken, heartbeatListener);
            return shutdownListener;
        }
    ```

Until now, `await EnsureHostInitializedAsync(cancellationToken);` is done. Then the next line of code is `_listener.StartAsync(cancellationToken);`
#### 2.3 Start Listener
`_listener` is created from this function `CreateHostListener()`:

* `ShutdownListener` calls `StartAsync()` 
    * `HeartbeatListener` calls `StartAsync()`
        * `ListenerFactoryListener` calls `StartAsync()` and `UpdateHostHeartbeatCommand` calls `TryExecuteAsync`.


As we can see, `_listener` calls `StartAsync()`, which starts listening and returns a task that completes when the listener is fully started. WebJobs defines multiple listens such as `BlobListener`,`CompositeListener`,`FunctionListener`,`HeartbeatListener`,`ListenerFactoryListener`,`NullListener`,`ShutdownListener`,`TimerListener`,`QueueListener`,`SingletonListener`, `EventHubListener` and `ServiceBusListener`.

## References
1. https://github.com/Azure/azure-webjobs-sdk
2. https://github.com/Azure/azure-webjobs-sdk/wiki/Introduction