## Azure WebJobs SDK basics
" The Azure WebJobs SDK is a framework that simplifies the task of writing background processing code that runs in Azure. The Azure WebJobs SDK includes a declarative binding and trigger system that works with Azure Storage Blobs, Queues and Tables as well as Service Bus. The binding system makes it incredibly easy to write code that reads or writes Azure Storage objects. The trigger system automatically invokes a function in your code whenever any new data is received in a queue or blob." [1]

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

As we can see from above sample code, an instance `JobHost host` is created first. Then `host` calls `RunAndBlock()`. We will analyze how this works, especially focus on how the funtion `ProcessQueueMessage()` is invoked.

We can see this path: `RunAndBlock()`->`Start()`->`StartAsync()`->`StartAsyncCore(cancellationToken)`->`EnsureHostInitializedAsync(cancellationToken)` as shown in following sample code, which is from source code [src/Microsoft.Azure.WebJobs.Host/JobHost.cs](https://github.com/Azure/azure-webjobs-sdk/blob/dev/src/Microsoft.Azure.WebJobs.Host/JobHost.cs).

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

            // Need to confirm, which _listener is it?
            await _listener.StartAsync(cancellationToken);

            OnHostStarted();

            string msg = "Job host started";
            _logger?.LogInformation(msg);

            _state = StateStarted;
        }

        /// <summary>
        /// Ensure all required host services are initialized and the host is ready to start
        /// processing function invocations. This function does not start the listeners.
        /// If multiple threads call this, only one should do the initialization. The rest should wait.
        /// When this task is signalled, _context is initialized.
        /// </summary>
        private Task EnsureHostInitializedAsync(CancellationToken cancellationToken)
        {
            ....
            TaskCompletionSource<bool> tsc = null;

            lock (_lock)
            {
                if (_initializationRunning == null)
                {
                    // This thread wins the race and owns initialing. 
                    tsc = new TaskCompletionSource<bool>();
                    _initializationRunning = tsc.Task;
                }
            }

            if (tsc != null)
            {
                // Ignore the return value and use tsc so that all threads are awaiting the same thing. 
                Task ignore = InitializeHostAsync(cancellationToken, tsc);
            }

            return _initializationRunning;
        }
```

Inside the function `EnsureHostInitializedAsync(cancellationToken)`, [CancellationToken](https://msdn.microsoft.com/en-us/library/system.threading.cancellationtoken(v=vs.110).aspx) propagates notification that operations should be canceled while [TaskCompletionSource](https://msdn.microsoft.com/en-us/library/dd449174(v=vs.110).aspx) represents the producer side of a `Task<TResult>` unbound to a delegate, providing access to the consumer side through the Task property. It calls the function `InitializeHostAsync(cancellationToken, tsc)` as shown bellow:

```
        // Caller gaurantees this is single-threaded. 
        // Set initializationTask when complete, many threads can wait on that. 
        // When complete, the fields should be initialized to allow runtime usage. 
        private async Task InitializeHostAsync(CancellationToken cancellationToken, TaskCompletionSource<bool> initializationTask)
        {
            try
            {
                // Ensure the static services are initialized. 
                // These are derived from the underlying JobHostConfiguration.
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
            catch (Exception e)
            {
                initializationTask.SetException(e);
            }
        }
```


Before we go through the function `InitializeHostAsync`, we need to understand the class `JobHostConfiguration`, which represents the configuration settings for a `JobHost`. The instance of class `JobHostConfiguration` `_config` is created when we instantiate `JobHost`. It sets the storage account and adds necessary services Azure WebJobs needs such as:
* JobActivator: creates instances of job classes when calling instance methods.
* TypeLocator: defines a locator that identifies types that may contain functions for `JobHost` to execute.
* NameResolver: defines a resolver for `name` variables in attribute values.
* ConverterManager: converts between types for parameter bindings. Parameter bindings call this to convert from user parameter types to underlying binding types.
* StorageClientFactory: creates Azure Storage clients.
* ....


```
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
            IExtensionRegistry extensions = new DefaultExtensionRegistry();
            ITypeLocator typeLocator = new DefaultTypeLocator(ConsoleProvider.Out, extensions);
            IConverterManager converterManager = new ConverterManager();
            IWebJobsExceptionHandler exceptionHandler = new WebJobsExceptionHandler();

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
```

As we can see from function `InitializeHostAsync(CancellationToken cancellationToken, TaskCompletionSource<bool> initializationTask)`, it calls `_config.CreateJobHostContextAsync(_services, this, _shutdownTokenSource.Token, cancellationToken)`. Then let's look at function `CreateJobHostContextAsync()`. It is a big function and we will look at piece by piece:

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

            using (CancellationTokenSource combinedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdownToken))
            {
                ....
                if (functionExecutor == null)
                {
                    var extensionRegistry = config.GetService<IExtensionRegistry>();
                    var globalFunctionFilters = extensionRegistry.GetFunctionFilters();

                    functionExecutor = new FunctionExecutor(functionInstanceLogger, functionOutputLogger, exceptionHandler, functionEventCollector, loggerFactory, globalFunctionFilters);
                    services.AddService(functionExecutor);
                }

                if (functionIndexProvider == null)
                {
                    var defaultTimeout = config.FunctionTimeout?.ToAttribute();
                    functionIndexProvider = new FunctionIndexProvider(
                        services.GetService<ITypeLocator>(),
                        triggerBindingProvider,
                        bindingProvider,
                        activator,
                        functionExecutor,
                        extensions,
                        singletonManager,                        
                        loggerFactory,
                        hostSharedQueue,
                        defaultTimeout);

                    // Important to set this so that the func we passed to DynamicHostIdProvider can pick it up. 
                    services.AddService<IFunctionIndexProvider>(functionIndexProvider);
                }

                IFunctionIndex functions = await functionIndexProvider.GetAsync(combinedCancellationToken);
                IListenerFactory functionsListenerFactory = new HostListenerFactory(functions.ReadAll(), singletonManager, activator, nameResolver, loggerFactory);

                IFunctionExecutor hostCallExecutor;
                IListener listener;
                HostOutputMessage hostOutputMessage;

                string hostId = await hostIdProvider.GetHostIdAsync(cancellationToken);
                ....

                if (dashboardAccount == null)
                {
                    ....
                }
                else
                {
                    string sharedQueueName = HostQueueNames.GetHostQueueName(hostId);
                    IStorageQueueClient dashboardQueueClient = dashboardAccount.CreateQueueClient();
                    IStorageQueue sharedQueue = dashboardQueueClient.GetQueueReference(sharedQueueName);
                    IListenerFactory sharedQueueListenerFactory = new HostMessageListenerFactory(sharedQueue,
                        queueConfiguration, exceptionHandler, loggerFactory, functions,
                        functionInstanceLogger, functionExecutor);

                    Guid hostInstanceId = Guid.NewGuid();
                    string instanceQueueName = HostQueueNames.GetHostQueueName(hostInstanceId.ToString("N"));
                    IStorageQueue instanceQueue = dashboardQueueClient.GetQueueReference(instanceQueueName);
                    IListenerFactory instanceQueueListenerFactory = new HostMessageListenerFactory(instanceQueue,
                        queueConfiguration, exceptionHandler, loggerFactory, functions,
                        functionInstanceLogger, functionExecutor);

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

                    IEnumerable<MethodInfo> indexedMethods = functions.ReadAllMethods();
                    Assembly hostAssembly = GetHostAssembly(indexedMethods);
                    string displayName = hostAssembly != null ? AssemblyNameCache.GetName(hostAssembly).Name : "Unknown";
                    ....

                    hostCallExecutor = CreateHostCallExecutor(instanceQueueListenerFactory, heartbeatCommand,
                        exceptionHandler, shutdownToken, functionExecutor);
                    IListenerFactory hostListenerFactory = new CompositeListenerFactory(functionsListenerFactory,
                        sharedQueueListenerFactory, instanceQueueListenerFactory);
                    listener = CreateHostListener(hostListenerFactory, hostSharedQueue, heartbeatCommand, exceptionHandler, shutdownToken);

                    // Publish this to Azure logging account so that a web dashboard can see it. 
                    await LogHostStartedAsync(functions, hostOutputMessage, hostInstanceLogger, combinedCancellationToken);
                }

                functionExecutor.HostOutputMessage = hostOutputMessage;

                IEnumerable<FunctionDescriptor> descriptors = functions.ReadAllDescriptors();
                int descriptorsCount = descriptors.Count();
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

We have `functionExecutor` and `functionIndexProvider`. Then `functionIndexProvider` calls `GetAsync(combinedCancellationToken)`:
```
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

            // _typeLocator identifies types that may contain functions for JobHost to execute
            IReadOnlyList<Type> types = _typeLocator.GetTypes();

            foreach (Type type in types)
            {
                await indexer.IndexTypeAsync(type, index, cancellationToken);
            }

            return index;
        }
```

`indexer.IndexTypeAsync(type, index, cancellationToken)`:
```
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
```

In above code, it first instantiates a `functionDescriptor`, whose defination is shown below:
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
```
Then it calls `FunctionInvokerFactory.Create(method, _activator)` to create a `FunctionInvoker` like this `new FunctionInvoker<TReflected, TReturnValue>(parameterNames, instanceFactory, methodInvoker)`with input parameters `parameterNames`, `instanceFactory` and `methodInvoker`:
```
        public static IFunctionInvoker Create(MethodInfo method, IJobActivator activator)
        {
            ....
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

        private static IFactory<TReflected> CreateInstanceFactory<TReflected>(MethodInfo method,
            IJobActivator jobActivator)
        {
            Debug.Assert(method != null);

            if (method.IsStatic)
            {
                return NullInstanceFactory<TReflected>.Instance;
            }
            else
            {
                return new ActivatorInstanceFactory<TReflected>(jobActivator);
            }
        }
```
`MethodInvokerFactory.Create<TReflected, TReturnValue>(method)`. It uses:
* [ParameterExpression](https://msdn.microsoft.com/en-us/library/system.linq.expressions.parameterexpression(v=vs.110).aspx) represents a named parameter expression.
* [Expression](https://msdn.microsoft.com/en-us/library/system.linq.expressions.expression(v=vs.110).aspx.) provides the base class from which the classes that represent expression tree nodes are derived.
* [BlockExpression](https://msdn.microsoft.com/en-us/library/system.linq.expressions.blockexpression(v=vs.110).aspx) represents a block that contains a sequence of expressions where variables can be defined.
* [LambdaExpression](https://msdn.microsoft.com/en-us/library/system.linq.expressions.lambdaexpression(v=vs.110).aspx) describes a lambda expression. This captures a block of code that is similar to a .NET method body.
* [LambdaExpression.Compile()](https://msdn.microsoft.com/en-us/library/bb356928(v=vs.110).aspx) produces a delegate that represents the lambda expression.
```
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
            ....

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
```
`FunctionInvoker`:
```
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

Then it create an instance of class `TriggeredFunctionExecutor`, which has _descriptor, _executor and _instanceFactory.
```
        public TriggeredFunctionExecutor(FunctionDescriptor descriptor, IFunctionExecutor executor, ITriggeredFunctionInstanceFactory<TTriggerValue> instanceFactory)
        {
            _descriptor = descriptor;
            _executor = executor;
            _instanceFactory = instanceFactory;
        }

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
```
Then it creates an instance of Class `ListenerFactory`:
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

    // how queue listenerfactory looks like    
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

Until now, `await EnsureHostInitializedAsync(cancellationToken);` is done. Then the next line of code is `_listener.StartAsync(cancellationToken);`

`_listener` is created from this function `CreateHostListener()`:

```
        IStorageBlockBlob blob = dashboardAccount.CreateBlobClient()
                        .GetContainerReference(heartbeatDescriptor.SharedContainerName)
                        .GetBlockBlobReference(heartbeatDescriptor.SharedDirectoryName + "/" + heartbeatDescriptor.InstanceBlobName);
        IRecurrentCommand heartbeatCommand = new UpdateHostHeartbeatCommand(new HeartbeatCommand(blob));

        IListenerFactory hostListenerFactory = new CompositeListenerFactory(functionsListenerFactory,
                        sharedQueueListenerFactory, instanceQueueListenerFactory);
        listener = CreateHostListener(hostListenerFactory, hostSharedQueue, heartbeatCommand, exceptionHandler, shutdownToken);
```

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

    internal class HeartbeatListener : IListener
    {
        ....
        public HeartbeatListener(IRecurrentCommand heartbeatCommand,
            IWebJobsExceptionHandler exceptionHandler, IListener innerListener)
        {
            _heartbeatCommand = heartbeatCommand;
            _innerListener = innerListener;
            _timer = CreateTimer(exceptionHandler);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _innerListener.StartAsync(cancellationToken);

            await _heartbeatCommand.TryExecuteAsync(cancellationToken);
            _timer.Start();
        }
    }

    internal class ShutdownListener : IListener
    {
        ....
        public ShutdownListener(CancellationToken shutdownToken, IListener innerListener)
        {
            _shutdownToken = shutdownToken;
            _shutdownRegistration = shutdownToken.Register(Cancel);
            _innerListener = innerListener;
        }

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

* `ShutdownListener` calls `StartAsync()` 
    * `HeartbeatListener` calls `StartAsync()`
        * `ListenerFactoryListener` calls `StartAsync()` and `UpdateHostHeartbeatCommand` calls `TryExecuteAsync`.

```
    internal class ListenerFactoryListener : IListener
    {
        private readonly IListenerFactory _factory;
        private readonly SharedQueueHandler _sharedQueue;
        private readonly CancellationTokenSource _cancellationSource;

        private IListener _listener;
        private CancellationTokenRegistration _cancellationRegistration;
        private bool _disposed;

        public ListenerFactoryListener(IListenerFactory factory, SharedQueueHandler sharedQueue)
        {
            _factory = factory;
            _sharedQueue = sharedQueue;
            _cancellationSource = new CancellationTokenSource();
        }

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
As we can see, `_listener` calls `StartAsync()`, which starts listening and returns a task that completes when the listener is fully started. WebJobs defines multiple listens such as `BlobListener`,`CompositeListener`,`FunctionListener`,`HeartbeatListener`,`ListenerFactoryListener`,`NullListener`,`ShutdownListener`,`TimerListener`,`QueueListener`,`SingletonListener`, `EventHubListener` and `ServiceBusListener`.

## References
1. https://github.com/Azure/azure-webjobs-sdk
2. https://github.com/Azure/azure-webjobs-sdk/wiki/Introduction
