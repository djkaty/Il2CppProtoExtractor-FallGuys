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
using System.Text;
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

            // All protobuf fields have this property attribute
            var protoMember = model.GetType("ProtoBuf.ProtoMemberAttribute");

            // Get all of the custom attributes generators for ProtoMember so we can determine field numbers
            var atts = model.CustomAttributeGenerators[protoMember];

            // Create a mapping of CAG virtual addresses to field numbers by reading the disassembly code
            var vaFieldMapping = atts.Select(a => new {
                VirtualAddress = a.VirtualAddress.Start,
                FieldNumber    = a.GetMethodBody()[0x0D]
            })
            // Fixup for generator which merges [Obsolete] and [ProtoMember]
            // We can improve upon this ugly hack with other features of Il2CppInspector in the future!
            .ToDictionary(kv => kv.VirtualAddress, kv => kv.FieldNumber);

            // Find CAGs which are used by other attribute types and shared with ProtoMember
            var sharedAtts = vaFieldMapping.Keys.Select(a => model.CustomAttributeGeneratorsByAddress[a].Where(a => a.AttributeType != protoMember))
                .Where(l => l.Any())
                .SelectMany(l => l);

            // Warn about shared mappings
            foreach (var item in sharedAtts)
                Console.WriteLine($"WARNING: Attribute generator {item.VirtualAddress.ToAddressString()} is shared with {item.AttributeType.FullName} - check disassembly listing");

            // Fixups we have determined from the disassembly
            vaFieldMapping[0x180055270] = 5;
            vaFieldMapping[0x180005660] = 7;
            vaFieldMapping[0x18002FCB0] = 1;

            // All attribute mappings
            foreach (var item in vaFieldMapping)
                Console.WriteLine($"{item.Key.ToAddressString()} = {item.Value}");

            // Let's iterate over all of the messages and find all of the fields
            // This is any field or property with the [ProtoMember] attribute
            foreach (var message in messages) {
                var name   = message.CSharpName;
                var fields = message.DeclaredFields.Where(f => f.CustomAttributes.Any(a => a.AttributeType == protoMember));
                var props  = message.DeclaredProperties.Where(p => p.CustomAttributes.Any(a => a.AttributeType == protoMember));

                var proto = new StringBuilder();
                proto.Append($"message {name} {{\n");

                // TODO: We are going to need to map these C# types to protobuf types!

                // Output C# fields
                foreach (var field in fields) {
                    var pmAtt = field.CustomAttributes.First(a => a.AttributeType == protoMember);
                    proto.Append($"  {field.FieldType.Name} {field.Name} = {vaFieldMapping[pmAtt.VirtualAddress.Start]};\n");
                }

                // Output C# properties
                foreach (var prop in props) {
                    var pmAtt = prop.CustomAttributes.First(a => a.AttributeType == protoMember);
                    proto.Append($"  {prop.PropertyType.Name} {prop.Name} = {vaFieldMapping[pmAtt.VirtualAddress.Start]};\n");
                }

                proto.Append("}\n");

                Console.WriteLine(proto);
            }
        }
    }
}
