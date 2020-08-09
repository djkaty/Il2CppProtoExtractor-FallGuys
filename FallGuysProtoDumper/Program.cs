// This is an example showing how to use the .NET type model in Il2CppInspector
// to re-construct .proto files for applications using protobuf-net

// Copyright 2020 Katy Coe - http://www.djkaty.com - https://github.com/djkaty

// This example uses "Fall Guys: Ultimate Knockout" as the target:
// Steam package: https://steamdb.info/sub/369927/
// Game version: 2020-08-04
// GameAssembly.dll - CRC32: F448429A
// global-metadata.dat - CRC32: 98DFE664

// Il2CppInspector: https://github.com/djkaty/Il2CppInspector
// protobuf-net: https://github.com/protobuf-net/protobuf-net

using System;
using System.Linq;
using Il2CppInspector.Reflection;

namespace FallGuysProtoDumper
{
    class Program
    {
        // Set the path to your metadata and binary files here
        public static string MetadataFile = @"F:\Source\Repos\Il2CppInspector\Il2CppTests\TestBinaries\FallGuys\global-metadata.dat";
        public static string BinaryFile = @"F:\Source\Repos\Il2CppInspector\Il2CppTests\TestBinaries\FallGuys\GameAssembly.dll";

        static void Main(string[] args) {

            // First we load the binary and metadata files into Il2CppInspector
            // There is only one image so we use [0] to select this image
            Console.WriteLine("Loading package...");
            var package = Il2CppInspector.Il2CppInspector.LoadFromFile(BinaryFile, MetadataFile, silent: true)[0];

            // Now we create the .NET type model from the package
            // This creates a .NET Reflection-style interface we can query with Linq
            Console.WriteLine("Creating type model...");
            var model = new TypeModel(package);

            // All protobuf messages have this class attribute
            var protoContract = model.GetType("ProtoBuf.ProtoContractAttribute");

            // Get all the messages by searching for types with [ProtoContract]
            var messages = model.TypesByDefinitionIndex.Where(t => t.CustomAttributes.Any(a => a.AttributeType == protoContract));

            // Print all of the message names
            foreach (var message in messages)
                Console.WriteLine(message.GetScopedCSharpName(Scope.Empty));
        }
    }
}
