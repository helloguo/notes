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

## References
1. https://en.wikipedia.org/wiki/GRPC
2. https://github.com/grpc/grpc/tree/master/examples/csharp/helloworld